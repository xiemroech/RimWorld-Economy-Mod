using System.Collections.Generic;
using RimWorld;
using Verse;
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

        public static void ResetRuntime()
        {
            dueTick.Clear();
        }

        /// <summary>有白银进了某个殖民者的背包（或有白银变回公共）时调用。</summary>
        public static void MarkDirty(Pawn p)
        {
            if (p == null || p.Map == null || !p.IsColonist || p.Dead || p.Downed) return;
            dueTick[p] = Find.TickManager.TicksGame + SettleDelay;
        }

        public static void Tick(Map map)
        {
            if (map == null || dueTick.Count == 0) return;
            int now = Find.TickManager.TicksGame;

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
            Thing silver = FindPublicSilver(p);
            if (silver == null) return true; // 没有了，登记作废

            if (TrySendToStorage(p, silver, map)) return true;
            if (IsBusy(p)) return false;    // 忙：等下一次再看，不丢他脚边
            return DropAtFeet(p, silver);
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

        private static Thing FindPublicSilver(Pawn p)
        {
            List<Thing> items = p.inventory.innerContainer.InnerListForReading;
            for (int i = 0; i < items.Count; i++)
            {
                Thing t = items[i];
                if (t == null || t.Destroyed) continue;
                if (t.def != ThingDefOf.Silver) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue; // 私有的不动
                return t;
            }
            return null;
        }

        private static bool TrySendToStorage(Pawn p, Thing silver, Map map)
        {
            if (IsBusy(p)) return false;

            if (!StoreUtility.TryFindBestBetterStoreCellFor(silver, p, map, StoragePriority.Unstored,
                    p.Faction, out IntVec3 cell))
            {
                return false;
            }

            Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, silver, cell);
            job.count = silver.stackCount;
            job.haulMode = HaulMode.ToCellStorage;
            p.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
            return true;
        }

        private static bool DropAtFeet(Pawn p, Thing silver)
        {
            if (p.MapHeld == null || !p.Spawned) return false;
            Thing dropped;
            if (p.inventory.innerContainer.TryDrop(silver, p.PositionHeld, p.MapHeld,
                    ThingPlaceMode.Near, out dropped))
            {
                Log.Message("[DSH-ECONOMY] 公共白银： " + p.LabelShort + " 包里 " + silver.stackCount
                            + " 银没有仓位，已丢在脚下");
            }
            return true;
        }
    }
}
