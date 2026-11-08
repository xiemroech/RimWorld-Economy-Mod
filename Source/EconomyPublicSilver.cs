using System.Collections.Generic;
using RimWorld;
using Verse;
using UnityEngine;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 背包里的“公共白银”（没有物主标签的白银）不该被小人一直带着：
    /// 优先搬进公共仓库；找不到能放的地方就直接丢在脚下。
    /// 私有白银（有物主标签）完全不碰，那是工资/零钱。
    /// 事件驱动：只有在白银进入背包 / 归属变回公共时才登记，平时零开销。
    /// </summary>
    public static class EconomyPublicSilver
    {
        /// <summary>登记后等一小会儿再处理，避免在交易/搬运的中途插入任务。</summary>
        private const int SettleDelay = 180;
        private const int MaxPerPass = 4;

        private static readonly Dictionary<Pawn, int> dueTick = new Dictionary<Pawn, int>();

        // 诊断：发现/成功/失败计数，每游戏小时打印一次
        private static int lastDiag = -99999, diagFound, diagOk, diagNoCell, diagCantQueue;

        public static void ResetRuntime()
        {
            dueTick.Clear();
            lastFoundLog.Clear();
            lastSweep = -99999;
            lastDiag = -99999;
            diagFound = diagOk = diagNoCell = diagCantQueue = 0;
        }

        /// <summary>有白银进了某个殖民者的背包（或有白银变回公共）时调用。</summary>
        public static void MarkDirty(Pawn p)
        {
            if (p == null || p.Map == null || !p.IsColonist || p.Dead || p.Downed) return;
            dueTick[p] = Find.TickManager.TicksGame + SettleDelay;
        }

        private static int lastSweep = -99999;

        /// <summary>兜底扫描：事件入口会漏（工资/拾取/mod 直接塞背包），每游戏小时检查一遍所有殖民者背包。</summary>
        private static void Sweep(Map map, int now)
        {
            if (now - lastSweep < 2500) return;
            lastSweep = now;
            List<Pawn> crew = map.mapPawns.FreeColonists;
            for (int i = 0; i < crew.Count; i++)
            {
                Pawn p = crew[i];
                if (p == null || p.Dead || p.inventory == null) continue;
                if (FindPublicSilver(p) != null) MarkDirty(p);
            }
        }

        public static void Tick(Map map)
        {
            if (map == null) return;
            int now = Find.TickManager.TicksGame;
            Sweep(map, now);
            if (now - lastDiag >= 2500)
            {
                if (diagFound + diagOk + diagNoCell + diagCantQueue > 0)
                    Log.Message("[DSH-ECONOMY] [PUB] 近一小时：发现 " + diagFound + "，派活成功 " + diagOk
                                + "，没仓位 " + diagNoCell + "，不能排队 " + diagCantQueue + "，待处理 " + dueTick.Count);
                lastDiag = now;
                diagFound = diagOk = diagNoCell = diagCantQueue = 0;
            }
            if (dueTick.Count == 0) return;

            List<Pawn> ready = null;
            foreach (KeyValuePair<Pawn, int> kv in dueTick)
            {
                Pawn p = kv.Key;
                if (p == null || p.Dead || p.Destroyed || p.MapHeld != map)
                {
                    (ready ?? (ready = new List<Pawn>())).Add(p);
                    continue;
                }
                if (kv.Value > now) continue;
                (ready ?? (ready = new List<Pawn>())).Add(p);
            }
            if (ready == null) return;

            int done = 0;
            for (int i = 0; i < ready.Count; i++)
            {
                Pawn p = ready[i];
                int due;
                if (!dueTick.TryGetValue(p, out due)) continue;
                if (due > now) continue;

                // 处理失败（正在忙、暂时没仓位）就再等一会儿，不无限重试
                if (done >= MaxPerPass || !Handle(p, map))
                {
                    if (!p.Destroyed && !p.Dead) dueTick[p] = now + 600;
                }
                else
                {
                    dueTick.Remove(p);
                }
                done++;
            }
        }

        private static bool Handle(Pawn p, Map map)
        {
            if (p == null || p.Dead || p.Downed || p.inventory == null || p.MapHeld != map) return true;
            // 正在"往背包里拿东西"的这一刻先放过：任务还没结束，
            // 补结算（EndCurrentJob 里）马上就要给这件东西打私有标签。
            // 以前这里会抢先把刚拿到的食物当公共物品收回/丢下，
            // 结果就是"捡了、扣了分、东西又回到公有空间"。
            Job cur = p.jobs != null ? p.jobs.curJob : null;
            if (cur != null && cur.def == JobDefOf.TakeInventory) return true;
            Thing silver = FindPublicSilver(p);
            if (silver == null) return true; // 没有了，登记作废
            diagFound++;
            LogFound(p, silver);

            if (!CanQueueHaul(p)) { diagCantQueue++; return false; }
            // 正在干活也照排：EnqueueFirst 只是插到当前工作之后，不会打断手里的活
            if (TrySendToStorage(p, silver, map)) { diagOk++; return true; }
            NoSlotBreakdown(map, p, silver);
            // 没有任何公共仓位能收它：直接丢脚下（不用等空闲，否则会一直挂在包里）
            diagNoCell++;
            return DropAtFeet(p, silver);
        }

        private static readonly Dictionary<Pawn, int> lastFoundLog = new Dictionary<Pawn, int>();

        private static void LogFound(Pawn p, Thing t)
        {
            int now = Find.TickManager.TicksGame;
            int day = now / GenDate.TicksPerDay;
            int last;
            if (lastFoundLog.TryGetValue(p, out last) && last == day) return;
            lastFoundLog[p] = day;
            Log.Message("[DSH-ECONOMY] [PUB] " + p.LabelShort + " 身上有公共物品 "
                        + t.LabelShort + " ×" + t.stackCount + "，准备入库或丢下");
        }

        private static bool IsBusy(Pawn p)
        {
            Job cur = p.jobs != null ? p.jobs.curJob : null;
            if (cur == null) return false;
            return cur.def != JobDefOf.Wait
                   && cur.def != JobDefOf.Wait_Wander
                   && cur.def != JobDefOf.GotoWander
                   && cur.def != JobDefOf.Goto;
        }

        /// <summary>
        /// 能不能把搬运排进队列：睡觉、吃东西、战斗、精神状态、被征召时不行，
        /// 其它（包括正在干活）都可以——排到当前工作之后执行，不会打断手上的活。
        /// </summary>
        private static bool CanQueueHaul(Pawn p)
        {
            if (p == null || p.Dead || p.Downed || p.Drafted) return false;
            if (p.InMentalState) return false;
            if (p.jobs == null || p.jobs.jobQueue == null) return false;
            // 排队不会打断当前工作（EnqueueFirst 只是插队），所以睡觉/吃饭时也可以先排上
            return true;
        }

        private static Thing FindPublicSilver(Pawn p)
        {
            Thing carried = p.carryTracker != null ? p.carryTracker.CarriedThing : null;
            if (carried != null && !carried.Destroyed && EconomyPrivate.OwnerOf(carried) == null
                && (carried.def == ThingDefOf.Silver || !carried.def.IsIngestible))
            {
                JobDef cj = p.CurJobDef;
                bool alreadyHauling = cj == JobDefOf.HaulToCell || cj == JobDefOf.HaulToContainer
                                      || cj == JobDefOf.HaulToTransporter || cj == JobDefOf.TakeInventory;
                if (!alreadyHauling) return carried;
            }

            List<Thing> items = p.inventory.innerContainer.InnerListForReading;
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (t == null || t.Destroyed) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue; // 私有的不动
                // 白银一律处理；食物在"不是正在吃、也不饿"时才处理（准备吃的别抢）
                if (t.def != ThingDefOf.Silver && t.def.IsIngestible)
                {
                    if (p.CurJobDef == JobDefOf.Ingest) continue;
                    if (p.needs != null && p.needs.food != null && p.needs.food.CurLevelPercentage < 0.6f) continue;
                }
                if (t.def.IsWeapon || t.def.IsApparel)
                {
                    // 穿在身上/拿在手里的不算"包里"，这里只处理背包里的
                }
                return t;
            }
            return null;
        }

        private static bool TrySendToStorage(Pawn p, Thing silver, Map map)
        {
            if (!CanQueueHaul(p)) return false;

            // 背包/手里拿着的东西不能被 HaulToCell 直接搬（不是地图上的实体），先放地上
            if (silver.ParentHolder is Pawn_InventoryTracker)
            {
                if (!p.inventory.innerContainer.TryDrop(silver, p.PositionHeld, p.MapHeld,
                        ThingPlaceMode.Near, out Thing dropped)) return false;
                silver = dropped;
            }
            else if (p.carryTracker != null && p.carryTracker.CarriedThing == silver)
            {
                if (!p.carryTracker.TryDropCarriedThing(p.PositionHeld, ThingPlaceMode.Near, out Thing dropped2)) return false;
                silver = dropped2;
            }
            if (silver == null || !silver.Spawned) return false;

            // 一叠超过单格上限时先拆出一份再找仓位，否则整叠永远"放不下"
            Thing move = silver;
            bool split = false;
            int limit = Mathf.Max(1, silver.def.stackLimit);
            if (silver.stackCount > limit)
            {
                move = silver.SplitOff(limit);
                split = true;
            }

            if (!StoreUtility.TryFindBestBetterStoreCellFor(move, p, map, StoragePriority.Unstored,
                    p.Faction, out IntVec3 cell))
            {
                if (split && !move.Destroyed) silver.TryAbsorbStack(move, true);
                return false;
            }

            // 选中的格子必须现在就能收这件东西，否则排了也会在执行时报
            // "未配置空余的、可到达的储存点"
            if (!StoreUtility.IsGoodStoreCell(cell, map, move, p, p.Faction))
            {
                if (split && !move.Destroyed) silver.TryAbsorbStack(move, true);
                return false;
            }

            Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, move, cell);
            job.count = move.stackCount;
            job.haulMode = HaulMode.ToCellStorage;
            p.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
            return true;
        }

        private static bool DropAtFeet(Pawn p, Thing thing)
        {
            if (p.MapHeld == null || !p.Spawned || thing == null) return false;
            IntVec3 pos = p.PositionHeld;
            Map map = p.MapHeld;

            // 手上拿着的走 carryTracker，背包里的走 inventory —— 用错容器会静默失败
            if (p.carryTracker != null && p.carryTracker.CarriedThing == thing)
            {
                Thing droppedCarried;
                if (p.carryTracker.TryDropCarriedThing(pos, ThingPlaceMode.Near, out droppedCarried))
                {
                    Log.Message("[DSH-ECONOMY] 公共物品： " + p.LabelShort + " 手上的 " + thing.LabelShort
                                + " ×" + thing.stackCount + " 没有公共仓位，已丢在脚下");
                }
                return true;
            }

            Thing dropped;
            if (p.inventory.innerContainer.TryDrop(thing, pos, map, ThingPlaceMode.Near, out dropped))
            {
                Log.Message("[DSH-ECONOMY] 公共物品： " + p.LabelShort + " 包里的 " + thing.LabelShort
                            + " ×" + thing.stackCount + " 没有公共仓位，已丢在脚下");
            }
            return true;
        }

        /// <summary>找不到仓位时，把每个存放组为什么拒绝打出来（每小时最多一次）。</summary>
        private static void NoSlotBreakdown(Map map, Pawn p, Thing item)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[PUB] 无仓位明细 ");
                sb.Append(p.LabelShort).Append(" 持有 ").Append(item.LabelShort).Append("：");
                foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                {
                    if (g == null) continue;
                    Pawn owner = EconomyPrivateStorageBehavior.OwnerOfGroup(g);
                    bool allows = true;
sb.Append(" [").Append((g.parent as Thing)?.LabelShort ?? "?").Append(" 主=").Append(owner == null ? "公共" : owner.LabelShort)
                      .Append(" 收=").Append(allows ? "是" : "否").Append(" 格=").Append(g.CellsList.Count).Append("]");
                }
                Log.Message(sb.ToString());
            }
            catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 无仓位明细失败：" + e.Message); }
        }
    }
}
