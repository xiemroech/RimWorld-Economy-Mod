using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 非工作时期：优先把自己名下的物品（背包里的）搬进自己的储物建筑。
    ///
    /// 上一版把原版搬运搞崩的教训：
    ///  - 不 patch 原版搬运方法，只在"小人空闲"时自己派一个明确目标的任务；
    ///  - 连续失败就当天不再尝试，避免每 250 tick 重试造成死循环；
    ///  - 只有拿到新的私有物品的人才会被登记，平时这个 tick 直接返回。
    /// </summary>
    public static class EconomyPrivateStorageBehavior
    {
        // 同一个人的最短重试间隔（1 游戏小时）
        private const int RetryInterval = 300;

        // 每天最多尝试次数（非工作时段执行，可以多试几次）
        // 不再限制每天次数：失败只推后 RetryInterval 再试。

        private static readonly HashSet<Pawn> pending = new HashSet<Pawn>();

        // 地上的私有物品（落地/消失时增量维护，避免全图扫描）
        private static readonly HashSet<Thing> grounded = new HashSet<Thing>();

        private static readonly Dictionary<Pawn, int> nextTryTick = new Dictionary<Pawn, int>();
        private static readonly Dictionary<Pawn, int> attempts = new Dictionary<Pawn, int>();
        private static readonly Dictionary<Pawn, int> dayOf = new Dictionary<Pawn, int>();


        /// <summary>读档/换图时清掉所有按 Pawn 对象缓存的运行时状态。
        /// 静态字典会跨读档存活，而 Pawn 都是新对象，不清就会带着上一档的"今天已经试过 6 次"。</summary>
        public static void ResetRuntime()
        {
            roomCacheTick.Clear();
            roomCacheVal.Clear();
            pending.Clear();
            grounded.Clear();
            nextTryTick.Clear();
            attempts.Clear();
            dayOf.Clear();
            groundScanned = false;
            lastSummaryTick = -99999;
            lastPlacementSweep = -99999;
            dumpedGroups = false;
        }

        public static void MarkDirty(Pawn p)
        {
            if (p != null && p.IsColonist) pending.Add(p);
        }

        public static void SpawnPostfix(Thing __instance)
        {
            Thing t = __instance;
            if (t == null || t.def == null) return;
            if (t.def.category != ThingCategory.Item) return;
            Pawn owner = EconomyPrivate.OwnerOf(t);
            if (owner == null || !owner.IsColonist) return;
            grounded.Add(t);
            MarkDirty(owner);
        }

        public static void DespawnPrefix(Thing __instance)
        {
            Thing t = __instance;
            if (t != null) grounded.Remove(t);
        }

        private static bool groundScanned;

        /// <summary>读档后扫一次地图上的私有物，重建 grounded 登记表（只做一次）。</summary>
        public static void ScanGroundOnce(Map map)
        {
            if (groundScanned || map == null || map.listerThings == null) return;
            groundScanned = true;
            int n = 0;
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.def == null || t.def.category != ThingCategory.Item) continue;
                if (EconomyRegistry.IsCurrency(t.def)) continue;
                Pawn owner = EconomyPrivate.OwnerOf(t);
                if (owner == null || !owner.IsColonist) continue;
                grounded.Add(t);
                MarkDirty(owner);
                n++;
            }
            int ownedZones = 0, ownedBuildings = 0;
            if (map.zoneManager != null)
            {
                List<Zone> zs = map.zoneManager.AllZones;
                for (int i = 0; i < zs.Count; i++)
                {
                    Zone_Stockpile zone = zs[i] as Zone_Stockpile;
                    if (zone != null && EconomyRealEstate.OwnerOf(zone) != null) ownedZones++;
                }
            }
            if (map.listerBuildings != null)
            {
                foreach (Building b in map.listerBuildings.allBuildingsColonist)
                {
                    if (b is Building_Storage && EconomyRealEstate.OwnerOfBuilding(b) != null) ownedBuildings++;
                }
            }
            int groups = 0, ownedGroups = 0, emptyGroups = 0;
            if (map.haulDestinationManager != null)
            {
                foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                {
                    if (g == null) continue;
                    groups++;
                    if (OwnerOfGroup(g) != null) ownedGroups++;
                    bool free = false;
                    if (g.CellsList != null)
                        for (int i = 0; i < g.CellsList.Count; i++)
                            if (g.CellsList[i].InBounds(map) && g.CellsList[i].GetThingList(map).Count == 0) { free = true; break; }
                    if (free) emptyGroups++;
                }
            }
            Log.Message("[HAUL] 载入扫描：地上私有物 " + n + " 件；有主存储区=" + ownedZones
                + " 有主存储建筑=" + ownedBuildings + " 存放组=" + groups
                + " 有主组=" + ownedGroups + " 有空位组=" + emptyGroups);
        }

        private static int lastSweepTick = -99999;

        /// <summary>定期把"背包里有自己私有物"的人重新登记，避免读档后 pending 为空。</summary>
        private static void Sweep(Map map, int now)
        {
            if (now - lastSweepTick < 2500) return;
            lastSweepTick = now;

            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p.inventory == null) continue;
                foreach (Thing t in p.inventory.innerContainer)
                {
                    Pawn owner = EconomyPrivate.OwnerOf(t);
                    if (owner == p) { MarkDirty(p); break; }
                }
            }

            // 地上的私有物也要认领：以前只扫背包，掉在地上的武器/装备永远没人搬
            List<Thing> ground = new List<Thing>(grounded);
            for (int i = 0; i < ground.Count; i++)
            {
                Thing t = ground[i];
                if (t == null || t.Destroyed)
                {
                    grounded.Remove(t);
                    continue;
                }
                if (!t.Spawned) continue;
                Pawn owner = EconomyPrivate.OwnerOf(t);
                if (owner == null || !owner.IsColonist) continue;
                if (owner.Map != map || owner.Dead || owner.Downed) continue;
                if (AlreadyStored(t, owner)) continue;
                MarkDirty(owner);
            }
        }

        public static void Tick(Map map)
        {
            if (map == null) return;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            ScanGroundOnce(map);
            Sweep(map, now);
            if (now - lastInvScan >= 2500) { lastInvScan = now; ScanInventories(map); }
            if (pending.Count == 0)
            {
                if (now % 2500 == 0)
                    Log.Message("[HAUL] 待搬=0 地上=" + grounded.Count);
                return;
            }
            int day = now / GenDate.TicksPerDay;

            int busy = 0, noCell = 0, issued = 0, waiting = 0;
            int now0 = now;
            bool diagHour = now0 - lastDiagTick >= 2500;
            if (diagHour) lastDiagTick = now0;
            List<Pawn> snapshot = new List<Pawn>(pending);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Pawn p = snapshot[i];
                if (p == null || p.Dead || p.Destroyed)
                {
                    pending.Remove(p);
                    continue;
                }
                if (p.Map != map) continue;

                Thing item = FindPrivateItem(p);
                if (item == null)
                {
                    pending.Remove(p);
                    continue;
                }

                int d;
                if (!dayOf.TryGetValue(p, out d) || d != day)
                {
                    dayOf[p] = day;
                    nextTryTick[p] = 0;
                }

                int next;
                nextTryTick.TryGetValue(p, out next);
                if (now < next) { waiting++; continue; }

                if (p.InMentalState || p.Downed) continue;
                if (p.jobs == null) continue;

                // 正在吃东西、或者已经饿了：先让人吃饭，别在这时候给他派搬运活，
                // 否则会出现"拿起自己的食物 → 被叫去放回箱子 → 再拿起"的来回卡死。
                if (p.CurJobDef == JobDefOf.Ingest) continue;
                if (p.needs != null && p.needs.food != null && p.needs.food.CurLevelPercentage < 0.5f) continue;

                // 空闲时随时可以搬；非工作时段还能打断闲逛/娱乐
                bool idle = p.jobs.curJob == null;
                string curDef = p.jobs.curJob != null ? p.jobs.curJob.def.defName : null;
                bool pureWander = curDef == "GotoWander" || curDef == "Wait_Wander"
                                  || curDef == "Wait" || curDef == "Goto";
                bool leisure = p.jobs.curJob != null
                               && IsLeisureJob(p.jobs.curJob.def)
                               && (!IsWorkTime(p) || pureWander);

                // A 方案：允许打断"低优先级、可中断"的工作去收自己的东西
                bool interruptible = p.jobs.curJob != null
                                     && !p.Downed && !p.InMentalState
                                     && IsLowPriorityWork(p.jobs.curJob);
                if (!idle && !leisure && !interruptible)
                {
                    busy++;
                    continue;
                }

                // 背包里的东西不能直接当 HaulToCell 的目标：先丢到脚下，再搬
                bool carried = item.ParentHolder is Pawn_InventoryTracker;
                if (carried)
                {
                    if (p.inventory == null) continue;
                    Thing dropped;
                    if (!p.inventory.innerContainer.TryDrop(item, p.PositionHeld, p.MapHeld,
                            ThingPlaceMode.Near, out dropped) || dropped == null)
                    {

                        nextTryTick[p] = now + RetryInterval;
                        continue;
                    }
                    item = dropped;
                }

                IntVec3 cell;
                if (!FindOwnCell(p, item, out cell))
                {
                    // 没有自己的仓库/没有空位就当天作罢；买仓库统一由消费系统负责，避免两边重复买
                    Log.Message("[HAUL] " + p.LabelShort + " 有私有物（" + item.LabelShort
                        + "）但自己名下没有能放的空格");
                    LogOwnDiagnostics(p, item, p.MapHeld);
                    if (diagHour)
                        Log.Message("[HAULWHY] " + p.LabelShort + " 搬不动（" + item.LabelShort
                            + " def=" + item.def.defName + "）：自己名下没有能收它的格子");

                    // 装备/衣服没有仓位时，先试着直接穿戴上（本来就不该进箱子）
                    if (item.ParentHolder is Pawn_InventoryTracker && TryEquipOrWear(p, item))
                    {
                        Log.Message("[HAUL] " + p.LabelShort + " 直接把（" + item.LabelShort + "）装备上了（没有仓位）");
                        continue;
                    }

                    // 没地方放就别一直塞在背包里：丢在脚下，让它回到地面（和公共货无法入库时一样）
                    if (item.ParentHolder is Pawn_InventoryTracker && p.inventory != null && !p.Downed)
                    {
                        Thing droppedNow;
                        if (p.inventory.innerContainer.TryDrop(item, p.PositionHeld, p.MapHeld,
                                ThingPlaceMode.Near, out droppedNow) && droppedNow != null)
                        {
                            Log.Message("[HAUL] " + p.LabelShort + " 的（" + droppedNow.LabelShort
                                + "）没有可放位置，已丢在脚下 " + droppedNow.PositionHeld);
                        }
                    }
                    noCell++;

                    continue;
                }

                issued++;
                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, item, cell);
                job.count = Mathf.Min(item.stackCount, p.carryTracker.AvailableStackSpace(item.def));
                if (job.count <= 0)
                {
                    if (diagHour)
                        Log.Message("[HAULWHY] " + p.LabelShort + " 想搬（" + item.LabelShort
                            + "）但手上没有空间（carryTracker 可用 0）");
                    continue;
                }
                job.haulMode = HaulMode.ToCellStorage;

                if (p.jobs.curJob != null && IsLowPriorityWork(p.jobs.curJob))
                p.jobs.TryTakeOrderedJob(job, JobTag.Misc);
            else
                p.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);   // 排队，不打断进食/睡觉

                nextTryTick[p] = now + RetryInterval;
            }

            if (now - lastSummaryTick >= 2500)
            {
                lastSummaryTick = now;
                Log.Message("[HAUL] 待搬=" + pending.Count + " 地上=" + grounded.Count
                    + " 忙=" + busy + " 无空格=" + noCell + " 派活=" + issued);
            }
        }


        private static int lastInvScan = -99999;
        private static int lastDiagTick = -99999;

        /// <summary>兜底：背包里带私有标签的东西如果没被 MarkDirty 过，每 2500 tick 补登记一次。</summary>
        public static void ScanInventories(Map map)
        {
            if (map == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.inventory == null || p.Dead) continue;
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (t == null || t.def == null) continue;
                    if (t.def.category != ThingCategory.Item) continue;
                    if (EconomyRegistry.IsCurrency(t.def)) continue;
                    if (EconomyPrivate.OwnerOf(t) != p) continue;
                    // 已装备/已穿戴在物主自己身上 = 已在归位：不再搬，也不要"放下"
                    if (t.ParentHolder is Pawn_EquipmentTracker eq && eq.pawn == p) { grounded.Remove(t); continue; }
                    if (t.ParentHolder is Pawn_ApparelTracker ap && ap.pawn == p) { grounded.Remove(t); continue; }
                    // 被别人装备着的私人物品：先让对方放下，下一轮再搬
                    if (t.ParentHolder is Pawn_EquipmentTracker eq2 && eq2.pawn != p)
                    {
                        ThingWithComps dropped;
                        ThingWithComps twc = t as ThingWithComps;
                        if (twc != null && eq2.TryDropEquipment(twc, out dropped, eq2.pawn.PositionHeld, true) && dropped != null)
                            GenPlace.TryPlaceThing(dropped, eq2.pawn.PositionHeld, eq2.pawn.MapHeld, ThingPlaceMode.Near);
                        continue;
                    }
                    if (AlreadyStored(t, p)) continue;
                    MarkDirty(p);
                    break;
                }
            }
        }

        /// <summary>当前作息是不是"工作"。</summary>
        private static bool IsWorkTime(Pawn p)
        {
            if (p.timetable == null) return false;
            TimeAssignmentDef assign = p.timetable.CurrentAssignment;
            return assign != null && assign == TimeAssignmentDefOf.Work;
        }

        /// <summary>闲逛/娱乐/等待这类可以打断的任务。</summary>
        private static bool IsLeisureJob(JobDef def)
        {
            if (def == null) return false;
            if (def.joyKind != null) return true;

            string n = def.defName;
            if (n == "Goto" || n == "GotoWander" || n == "Wait" || n == "Wait_Wander"
                || n == "Wait_Combat" || n == "Meditate" || n == "Relax" || n == "SocialRelax"
                || n == "GotoSafeArea" || n == "Wander") return true;
            return false;
        }

        /// <summary>在自己名下找一个能放这件东西的空格。</summary>
        /// <summary>可以被打断去收私有物的"低优先级"工作：搬运、清洁、建造、采矿、种植、砍伐。</summary>
        private static bool IsLowPriorityWork(Job job)
        {
            if (job == null || job.def == null) return false;
            string n = job.def.defName;
            if (n == "Haul" || n == "HaulToCell" || n == "HaulToContainer") return true;
            if (n == "Clean" || n == "ClearSnow" || n == "Harvest" || n == "CutPlant"
                || n == "Mine" || n == "Build" || n == "Repair" || n == "Deconstruct") return true;
            return false;
        }

        /// <summary>没仓位时，能用就直接穿/拿在手上：武器进装备栏、衣服进着装栏。</summary>
        private static bool TryEquipOrWear(Pawn p, Thing item)
        {
            try
            {
                if (item == null || item.def == null) return false;

                if (item.def.IsWeapon && p.equipment != null && p.equipment.Primary == null)
                {
                    ThingWithComps twc = item as ThingWithComps;
                    if (twc == null) return false;
                    string reason;
                    if (!EquipmentUtility.CanEquip(twc, p, out reason)) return false;
                    p.inventory.innerContainer.Remove(item);
                    p.equipment.AddEquipment(twc);
                    return true;
                }

                Apparel ap = item as Apparel;
                if (ap != null && p.apparel != null && !p.apparel.WornApparel.Contains(ap)
                    && ApparelUtility.HasPartsToWear(p, ap.def)
                    && ap.def.apparel.PawnCanWear(p)
                    && !p.apparel.WornApparel.Any(a => a.def.apparel.LastLayer == ap.def.apparel.LastLayer))
                {
                    p.inventory.innerContainer.Remove(ap);
                    p.apparel.Wear(ap, false);
                    return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 自己名下的“内部容器”（mod 箱子/冰箱等）是否还能收这件物品。
        /// 这类容器不是靠 SlotGroup 的空格数，而是靠 inner ThingOwner 的剩余组数/堆叠容量。
        /// </summary>
        public static bool FindOwnContainer(Pawn p, Thing item, out Thing container)
        {
            container = null;
            Map map = p != null ? p.MapHeld : null;
            if (map == null || item == null || map.haulDestinationManager == null) return false;

            foreach (IHaulDestination d in map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder)
            {
                if (d == null || !d.HaulDestinationEnabled) continue;
                Thing thing = d as Thing;
                if (thing == null || thing.Destroyed || !thing.Spawned) continue;

                ThingOwner inner = thing.TryGetInnerInteractableThingOwner();
                if (inner == null || inner.GetCountCanAccept(item) <= 0) continue;

                StorageSettings ss = d.GetStoreSettings();
                if (ss == null) continue;
                SlotGroup sg = thing.GetSlotGroup();
                Pawn owner = sg != null ? OwnerOfGroup(sg) : StorageSettingsOwnerIndex.OwnerOf(ss);
                if (owner != p) continue;

                if (!d.Accepts(item)) continue;
                if (!p.CanReach(thing, PathEndMode.ClosestTouch, Danger.Some)) continue;
                if (!p.CanReserveNew(thing)) continue;

                container = thing;
                return true;
            }
            return false;
        }

        public static bool FindOwnCell(Pawn p, Thing item, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            Map map = p != null ? p.MapHeld : null;
            if (map == null || map.haulDestinationManager == null) return false;

            // 直接遍历所有存放组：堆放区、储物建筑、其它 mod 的存储都算，
            // 不再只认 Zone_Stockpile / Building_Storage。
            foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (g == null || g.CellsList == null) continue;

                Pawn owner = OwnerOfGroup(g);
                if (owner != p) continue;   // 只找自己名下的

                StorageSettings ss = g.Settings;
                if (ss != null && !ss.AllowedToAccept(item)) continue;

                // 一个格子可以叠很多种物品（原版/储物 mod 都是这样），所以"空格"不等于"空单元格"。
                // 先找能并进现有堆的格子，再退而求其次找没有同类满堆的格子。
                IntVec3 fallback = IntVec3.Invalid;
                int fallbackCount = int.MaxValue;

                for (int i = 0; i < g.CellsList.Count; i++)
                {
                    IntVec3 c = g.CellsList[i];
                    if (!c.InBounds(map)) continue;

                    List<Thing> here = c.GetThingList(map);
                    bool blocked = false;

                    for (int k = 0; k < here.Count; k++)
                    {
                        Thing other = here[k];
                        if (other == null || other.def != item.def) continue;

                        Pawn otherOwner = EconomyPrivate.OwnerOf(other);
                        if (otherOwner != null && otherOwner != p)
                        {
                            blocked = true;   // 别人的私人物，不能混进去
                            continue;
                        }

                        // 同款、同材质、同品质、堆得下 → 直接并进去
                        if (other.CanStackWith(item)
                            && other.stackCount + item.stackCount <= other.def.stackLimit)
                        {
                            cell = c;
                            return true;
                        }
                    }

                    if (blocked) continue;

                    // 交给原版/储物 mod 自己的判定：堆叠上限、过滤器、容量都算在内
                    if (!StoreUtility.IsGoodStoreCell(c, map, item, p, p.Faction)) continue;

                    if (here.Count < fallbackCount)
                    {
                        fallbackCount = here.Count;
                        fallback = c;
                    }
                }

                if (fallback.IsValid)
                {
                    cell = fallback;
                    return true;
                }
            }

            return false;
        }

        /// <summary>把落进公共/别人存储里的私有物重新排进弹出队列。</summary>

        private static bool dumpedGroups;

        /// <summary>一次性把所有存放组（含归属、空格数）打到日志，用来核对"仓库到底有没有空"。</summary>
        public static void DumpAllGroups(Map map)
        {
            if (dumpedGroups || map == null || map.haulDestinationManager == null) return;
            dumpedGroups = true;

            int i = 0;
            foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (g == null) continue;
                i++;
                string kind = "?";
                string name = "?";
                Zone zone = g.parent as Zone;
                Building b = g.parent as Building;
                if (zone != null) { kind = "区"; name = zone.label; }
                else if (b != null) { kind = b.def.defName; name = b.LabelShort; }

                int empty = 0, total = g.CellsList != null ? g.CellsList.Count : 0;
                if (g.CellsList != null)
                {
                    for (int k = 0; k < g.CellsList.Count; k++)
                    {
                        IntVec3 c = g.CellsList[k];
                        if (c.InBounds(map) && c.GetThingList(map).Count == 0) empty++;
                    }
                }

                Pawn owner = OwnerOfGroup(g);
                Log.Message("[GROUPS] #" + i + " " + kind + " " + name
                    + " 格=" + total + " 空=" + empty
                    + " 主=" + (owner != null ? owner.LabelShort : "公共"));
            }
            Log.Message("[GROUPS] 共 " + i + " 个存放组");
        }


        /// <summary>自己名下还有没有任何一格能放下这件东西（交给原版判定，尊重储物 mod 的容量）。</summary>
        public static bool HasRoomFor(Pawn p, Thing item, Map map)
        {
            if (p == null || item == null || map == null || map.haulDestinationManager == null) return false;

            // 先看 mod 内部容器：它们容量在 ThingOwner 里，不看 SlotGroup 的空格。
            Thing container;
            if (FindOwnContainer(p, item, out container)) return true;

            foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (g == null || g.CellsList == null) continue;
                if (OwnerOfGroup(g) != p) continue;
                if (g.Settings != null && !g.Settings.AllowedToAccept(item)) continue;

                for (int i = 0; i < g.CellsList.Count; i++)
                {
                    IntVec3 c = g.CellsList[i];
                    if (!c.InBounds(map)) continue;
                    if (StoreUtility.IsGoodStoreCell(c, map, item, p, p.Faction)) return true;
                }
            }
            return false;
        }

        /// <summary>自己名下还有没有空间放"任何一件"私有物（没有私有物时按银币试探）。</summary>
        private static readonly Dictionary<Pawn, int> roomCacheTick = new Dictionary<Pawn, int>();
        private static readonly Dictionary<Pawn, bool> roomCacheVal = new Dictionary<Pawn, bool>();

        public static bool HasRoomForAny(Pawn p, Map map)
        {
            if (p == null || map == null) return false;

            // 每次问都全图扫一遍太贵（O(pawns × things × groups)），按人缓存 250 tick。
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            int cachedAt;
            bool cached;
            if (roomCacheTick.TryGetValue(p, out cachedAt) && now - cachedAt >= 0 && now - cachedAt < 250
                && roomCacheVal.TryGetValue(p, out cached))
                return cached;

            bool result = HasRoomForAnyUncached(p, map);
            roomCacheTick[p] = now;
            roomCacheVal[p] = result;
            return result;
        }

        private static bool HasRoomForAnyUncached(Pawn p, Map map)
        {
            if (p == null || map == null) return false;

            bool any = false;
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (EconomyPrivate.OwnerOf(t) != p) continue;
                any = true;
                if (HasRoomFor(p, t, map)) return true;
            }

            // 没有私有物 = 不需要容器。之前这里拿一枚临时银币去试探，
            // 只要仓库的过滤器不收白银就判"放不下"，于是有空间的人也会去买箱子。
            if (!any) return true;

            return false;
        }

        private static int lastPlacementSweep = -99999;
        private static int lastSummaryTick = -99999;

        public static void ValidatePrivatePlacement(Map map)
        {
            if (map == null || map.listerThings == null) return;
            int nowTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            DumpAllGroups(map);
            if (nowTick - lastPlacementSweep < 2500) return;
            lastPlacementSweep = nowTick;

            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (t.def == null || t.def.category != ThingCategory.Item) continue;

                Pawn owner = EconomyPrivate.OwnerOf(t);
                if (owner == null) continue;

                SlotGroup g = t.GetSlotGroup();
                if (g == null) continue;              // 没在存储里，地上放着不管

                Pawn cellOwner = EconomyRealEstate.OwnerAtCell(map, t.Position);
                if (cellOwner == owner) continue;      // 自己的存储，正常

                Log.Message("[HAUL] 异常放置：" + t.LabelShort + "（物主 " + owner.LabelShort
                    + "）在" + (cellOwner == null ? "公共" : cellOwner.LabelShort) + "存储里，准备弹出");
                StorageEjectQueue.Enqueue(t);
            }
        }

        /// <summary>某个存放组归谁：堆放区查 zone 归属，建筑查 building 归属。</summary>
        public static Pawn OwnerOfGroup(SlotGroup g)
        {
            if (g == null) return null;
            object parent = g.parent;
            Zone zone = parent as Zone;
            if (zone != null) return EconomyRealEstate.OwnerOf(zone);
            Building b = parent as Building;
            if (b != null) return EconomyRealEstate.OwnerOfBuilding(b);
            return null;
        }


        /// <summary>搬运失败时打印该小人名下存放组的真实状况，用于定位"没有能放的空格"。</summary>
        private static void LogOwnDiagnostics(Pawn p, Thing item, Map map)
        {
            int owned = 0, allowed = 0, free = 0;
            StringBuilder sb = new StringBuilder();
            foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (g == null) continue;
                Pawn owner = OwnerOfGroup(g);
                if (owner != p) continue;
                owned++;

                string name = null;
                Zone zone = g.parent as Zone;
                if (zone != null) name = zone.label;
                else
                {
                    Building b = g.parent as Building;
                    if (b != null) name = b.LabelShort;
                }

                bool ok = g.Settings == null || g.Settings.AllowedToAccept(item);
                int empty = 0;
                if (g.CellsList != null)
                {
                    for (int i = 0; i < g.CellsList.Count; i++)
                    {
                        IntVec3 c = g.CellsList[i];
                        if (c.InBounds(map) && StoreUtility.IsGoodStoreCell(c, map, item, p, p.Faction)) empty++;
                    }
                }
                if (ok) allowed++;
                free += empty;
                if (sb.Length < 400)
                    sb.Append(" [").Append(name ?? "?").Append(" 收=").Append(ok ? "是" : "否")
                      .Append(" 空=").Append(empty).Append("/")
                      .Append(g.CellsList != null ? g.CellsList.Count : 0).Append("]");
            }

            Log.Message("[HAUL] 明细：" + p.LabelShort + " 物品=" + item.LabelShort
                + " 判断额=" + EconomyPrice.UnitPrice(item, map).ToString("F1")
                + " 名下组=" + owned + " 能收=" + allowed + " 总空格=" + free + sb);
        }


        /// <summary>某人私有仓库的空余率（没有仓库返回 -1）。供界面显示。</summary>
        public static float FreeRatio(Pawn p, Map map, out int used, out int capacity)
        {
            used = 0;
            capacity = 0;
            if (p == null || map == null || map.haulDestinationManager == null) return -1f;

            foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (g == null || g.CellsList == null) continue;
                if (OwnerOfGroup(g) != p) continue;

                // mod 容器：容量在 inner ThingOwner 里（12 组 ≠ 1 格），必须按件数算
                ThingOwner inner = ContainerUtil.InnerOf(g.parent as Building_Storage);
                if (inner == null && g.parent is Thing)
                {
                    try { inner = (g.parent as Thing).TryGetInnerInteractableThingOwner(); } catch { }
                }
                if (inner != null)
                {
                    int cnt = ContainerUtil.CountIn(inner);
                    ThingDef sampleDef = ThingDefOf.Silver;
                    try
                    {
                        foreach (Thing it in inner) { if (it != null) { sampleDef = it.def; break; } }
                    }
                    catch { }
                    used += cnt;
                    capacity += cnt + Mathf.Max(1, ContainerUtil.CanAcceptCount(inner, sampleDef));
                    continue;
                }

                // 每格能放几堆：储物建筑自己的 maxItemsInCell（木货箱G 是 16，4 格 = 64 容量），
                // 普通货架/堆放区是 1。
                int perCell = 1;
                Building b = g.parent as Building;
                if (b != null && b.def != null && b.def.building != null)
                    perCell = Mathf.Max(1, b.def.building.maxItemsInCell);

                // 声明值不一定准（有的 mod 不写 maxItemsInCell），所以同时统计实际每格最多几堆，
                // 取两者较大值，避免读数永远卡在 0%。
                int observedMax = 0;
                for (int i = 0; i < g.CellsList.Count; i++)
                {
                    IntVec3 c = g.CellsList[i];
                    if (!c.InBounds(map)) continue;
                    List<Thing> here = c.GetThingList(map);
                    int stacks = 0;
                    for (int k = 0; k < here.Count; k++)
                    {
                        if (here[k] != null && here[k].def.category == ThingCategory.Item) stacks++;
                    }
                    used += stacks;
                    if (stacks > observedMax) observedMax = stacks;
                }

                capacity += g.CellsList.Count * Mathf.Max(perCell, observedMax);
            }

            if (capacity <= 0) return -1f;
            return Mathf.Clamp01((capacity - used) / (float)capacity);
        }

        /// <summary>先找背包，再找"掉在地上的自己的东西"。</summary>

        /// <summary>这件东西是不是已经放在物主自己的存放组里（那就别再"搬"一次，否则会来回折腾）。</summary>
        private static bool AlreadyStored(Thing t, Pawn p)
        {
            if (t == null || !t.Spawned) return false;
            SlotGroup g = t.GetSlotGroup();
            if (g == null) return false;
            return OwnerOfGroup(g) == p;
        }

        private static Thing FindPrivateItem(Pawn p)
        {
            if (p == null) return null;

            if (p.inventory != null)
            {
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (t == null || t.def == null) continue;
                    if (EconomyRegistry.IsCurrency(t.def)) continue;  // 白银走零钱制度，不在这里搬
                    if (!EconomyPrivate.IsPrivate(t)) continue;
                    if (EconomyPrivate.OwnerOf(t) != p) continue;
                    if (AlreadyStored(t, p)) continue;   // 已经在自己箱子里，不需要搬
                    return t;
                }
            }

            // 地上的：只在小集合里找，不扫地图
            List<Thing> snapshot = new List<Thing>(grounded);
            for (int i = 0; i < snapshot.Count; i++)
            {
                Thing t = snapshot[i];
                if (t == null || t.Destroyed || !t.Spawned)
                {
                    grounded.Remove(t);
                    continue;
                }
                if (t.Map != p.Map) continue;
                if (EconomyRegistry.IsCurrency(t.def)) continue;
                if (EconomyPrivate.OwnerOf(t) != p) continue;
                if (AlreadyStored(t, p)) continue;
                return t;
            }
            return null;
        }
    }
}
