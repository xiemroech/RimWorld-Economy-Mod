using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 仓库补货：按建筑的补货设置，把货从殖民地其它地方运进来。
    /// 默认模式 = 把空格补满；细化模式 = 按每种物品的下限/上限补。
    /// </summary>
    public static class EconomyRestock
    {
        public const int Interval = 1000;          // 约 0.4 游戏小时检查一次
        public const int MaxJobsPerPass = 3;       // 每次最多派几个搬运任务，别抢光劳动力
        public const int DefaultFillStacks = 2;    // 默认模式下每个仓库每次最多补几堆

        private static int lastTick;

        public const float MealsTarget = 6f;          // 料斗里保证 6 顿饭的营养
        public const float NutritionPerMeal = 0.9f;

        public static void Tick(Map map)
        {
            // 料斗补货已按用户要求移除（没有应用到实际玩法，且会和 [PUB] 的收回逻辑互相拉扯）。
            if (map == null) return;
            int now = Find.TickManager.TicksGame;
            if (now - lastTick < Interval) return;
            lastTick = now;

            int jobs = 0;
            foreach (Building_Storage st in map.listerBuildings.AllBuildingsColonistOfClass<Building_Storage>())
            {
                if (jobs >= MaxJobsPerPass) break;
                if (st == null || st.Destroyed) continue;
                // 私有容器不参与公共补货：谁的东西谁自己搬（否则就是"公共货进私有箱"）
                if (EconomyRealEstate.OwnerOfBuilding(st) != null) continue;
                // 市场摊位也走补货：卖掉的货由搬运工从公共库存补回去

                RestockSettings comp = RestockStore.Lookup(st);
                if (comp == null) continue;

                if (comp.detailed && comp.rules.Count > 0)
                    jobs += RestockDetailed(map, st, comp, jobs);
                else
                    jobs += FillEmpty(map, st, jobs);
            }
        }

        // ---------- 细化模式 ----------

        private static int RestockDetailed(Map map, Building_Storage st, RestockSettings comp, int already)
        {
            int jobs = 0;
            foreach (RestockRule rule in comp.rules)
            {
                if (jobs + already >= MaxJobsPerPass) break;
                if (rule == null || rule.def == null) continue;
                int target = RestockStore.TargetItems(st, rule);
                if (target <= 0) continue;
                int have = CountIn(st, rule.def);
                // 低于目标的一半才补，避免"掉一件就搬一次"
                if (have >= Mathf.Max(1, target / 2)) continue;
                int need = target - have;
                if (need <= 0) continue;

                Thing source = FindSource(map, st, rule.def);
                if (source == null) continue;

                Pawn pawn = FindHauler(map, source, st);
                if (pawn == null) continue;

                // mod 容器：容量与目标都在 inner ThingOwner 里，必须用 HaulToContainerJob，不能指单格
                ThingOwner innerBox = ContainerUtil.InnerOf(st);
                if (innerBox != null)
                {
                    if (ContainerUtil.CanAcceptCount(innerBox, rule.def) <= 0) continue;
                    Job jb = HaulAIUtility.HaulToContainerJob(pawn, source, st);
                    if (jb == null) continue;
                    int canTake = Mathf.Max(1, ContainerUtil.CanAcceptCount(innerBox, rule.def));
                    jb.count = Mathf.Clamp(Mathf.Min(need, canTake), 1, source.stackCount);
                    if (pawn.jobs.TryTakeOrderedJob(jb, JobTag.Misc)) jobs++;
                    continue;
                }

                IntVec3 cell = FindCell(st, rule.def, pawn);
                if (!cell.IsValid) continue;

                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, source, cell);
                job.count = UnityEngine.Mathf.Clamp(need, 1, source.stackCount);
                job.haulMode = HaulMode.ToCellStorage;
                if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc)) jobs++;
            }
            return jobs;
        }

        // ---------- 默认模式：补满空格 ----------

        private static int FillEmpty(Map map, Building_Storage st, int already)
        {
            int jobs = 0;
            int tries = 0;

            // mod 容器：不看格子，直接补进去
            ThingOwner innerFill = ContainerUtil.InnerOf(st);
            if (innerFill != null)
            {
                foreach (Thing t in map.listerThings.AllThings)
                {
                    if (jobs + already >= MaxJobsPerPass || jobs >= DefaultFillStacks) break;
                    if (t == null || t.Destroyed || t.def == null || t.def.category != ThingCategory.Item) continue;
                    if (EconomyPrivate.OwnerOf(t) != null) continue;
                    if (InStorage(t, st)) continue;
                    if (t.IsForbidden(Faction.OfPlayer) || t.IsBurning()) continue;
                    if (EconomyRealEstate.OwnerOfBuilding(st) != null) break;
                    if (!ContainerUtil.CanAccept(innerFill, t)) continue;
                    Pawn hauler = FindHauler(map, t, st);
                    if (hauler == null) continue;
                    Job jb = HaulAIUtility.HaulToContainerJob(hauler, t, st);
                    if (jb == null) continue;
                    int canFill = Mathf.Max(1, ContainerUtil.CountCanAccept(innerFill, t));
                    jb.count = Mathf.Min(t.stackCount, canFill);
                    if (hauler.jobs.TryTakeOrderedJob(jb, JobTag.Misc)) jobs++;
                }
                return jobs;
            }

            foreach (IntVec3 cell in st.AllSlotCellsList())
            {
                if (jobs + already >= MaxJobsPerPass) break;
                if (jobs >= DefaultFillStacks) break;
                if (++tries > 40) break;
                if (cell.GetThingList(map).Count > 0) continue;

                Thing source = FindAnySourceFor(map, st, cell);
                if (source == null) continue;

                Pawn pawn = FindHauler(map, source, st);
                if (pawn == null) continue;
                if (!StoreUtility.IsGoodStoreCell(cell, map, source, pawn, pawn.Faction)) continue;

                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, source, cell);
                job.count = source.stackCount;
                job.haulMode = HaulMode.ToCellStorage;
                if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc)) jobs++;
            }
            return jobs;
        }

        // ---------- 工具 ----------

        public static int CountIn(Building_Storage st, ThingDef def)
        {
            int n = 0;
            ThingOwner innerCount = ContainerUtil.InnerOf(st);
            if (innerCount != null)
            {
                try
                {
                    foreach (Thing t0 in innerCount)
                        if (t0 != null && !t0.Destroyed && t0.def == def) n += t0.stackCount;
                }
                catch { }
                return n;
            }
            SlotGroup group = st.GetSlotGroup();
            if (group == null) return 0;
            foreach (Thing t in group.HeldThings)
            {
                if (t != null && !t.Destroyed && t.def == def) n += t.stackCount;
            }
            return n;
        }

        private static Thing FindSource(Map map, Building_Storage st, ThingDef def)
        {
            foreach (Thing t in map.listerThings.ThingsOfDef(def))
            {
                if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue;
                if (InStorage(t, st)) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                if (t.IsBurning()) continue;
                return t;
            }
            return null;
        }

        private static Thing FindAnySourceFor(Map map, Building_Storage st, IntVec3 cell)
        {
            foreach (Thing t in map.listerThings.AllThings)
            {
                if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                if (t.def.category != ThingCategory.Item) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue;
                if (InStorage(t, st)) continue;
                if (t.IsForbidden(Faction.OfPlayer) || t.IsBurning()) continue;
                if (!st.GetStoreSettings().AllowedToAccept(t)) continue;
                return t;
            }
            return null;
        }

        private static bool InStorage(Thing t, Building_Storage st)
        {
            SlotGroup g = t.GetSlotGroup();
            return g != null && g.parent == st;
        }


        // 料斗补货（RestockHoppers）已删除：
        // 它和 [PUB] 的"公共物品收回"逻辑会互相拉扯（补进去的公共食物又被判定为可回收），
        // 而且这套玩法用户并未实际启用。将来要恢复，从版本历史里找回即可。


        private static Thing FindFoodForHopper(Map map, Building hopper, float need)
        {
            Thing found = null;
            float best = float.MaxValue;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t == null || t.def == null || t.Destroyed) continue;
                if (!t.def.IsNutritionGivingIngestible) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue;   // 只搬公共食物
                if (t.stackCount <= 0) continue;
                float d = t.Position.DistanceToSquared(hopper.Position);
                if (d < best && map.reachability.CanReach(t.Position, hopper.Position, PathEndMode.Touch, TraverseMode.PassDoors, Danger.Some))
                {
                    best = d;
                    found = t;
                }
            }
            return found;
        }

        private static Pawn FindHaulerAny(Map map, Thing source)
        {
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p == null || p.Dead || p.Downed || p.InMentalState) continue;
                if (p.CurJobDef != null && p.CurJobDef != JobDefOf.Wait && p.CurJobDef != JobDefOf.Wait_Wander && p.CurJobDef != JobDefOf.GotoWander) continue;
                if (p.workSettings == null || !p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling)) continue;
                if (!p.CanReach(source, PathEndMode.ClosestTouch, Danger.Some)) continue;
                return p;
            }
            return null;
        }

        private static Pawn FindHauler(Map map, Thing source, Building_Storage st)
        {
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p == null || p.Downed || p.Dead) continue;
                if (p.InMentalState || p.InAggroMentalState) continue;
                if (p.workSettings == null || !p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling)) continue;
                if (!p.CanReach(source, PathEndMode.ClosestTouch, Danger.Some)) continue;
                if (!p.CanReach(st, PathEndMode.Touch, Danger.Some)) continue;
                if (p.CurJobDef == JobDefOf.Ingest) continue;
                if (p.CurJobDef != null && !IsFreeJob(p.CurJobDef)) continue;
                return p;
            }
            return null;
        }

        private static bool IsFreeJob(JobDef def)
        {
            return def == JobDefOf.Wait
                || def == JobDefOf.Wait_Wander
                || def == JobDefOf.GotoWander
                || def == JobDefOf.Goto
                || def.joyKind != null;
        }

        private static IntVec3 FindCell(Building_Storage st, ThingDef def, Pawn p)
        {
            Map map = st.Map;
            IntVec3 best = IntVec3.Invalid;
            int bestCount = int.MaxValue;
            foreach (IntVec3 c in st.AllSlotCellsList())
            {
                List<Thing> things = c.GetThingList(map);
                bool stackable = false;
                foreach (Thing t in things)
                {
                    if (t.def == def && t.CanStackWith(ThingMaker.MakeThing(def))) { stackable = true; break; }
                }
                if (!stackable && things.Count > 0) continue;
                if (!stackable && !StoreUtility.IsGoodStoreCell(c, map, ThingMaker.MakeThing(def), p, p.Faction)) continue;
                if (things.Count < bestCount)
                {
                    bestCount = things.Count;
                    best = c;
                }
            }
            return best;
        }
    }
}
