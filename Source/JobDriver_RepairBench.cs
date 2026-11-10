using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 按原版「修复故障建筑」的流程走：去搬材料 → 扛到机器旁 → 进度条 1000 tick → 成功率判定 → 修好。
    /// 唯一区别是材料换成这台机器自己的维修材料（零部件 / 10% 建造材料）。
    /// </summary>
    public class JobDriver_RepairBench : JobDriver
    {
        private const TargetIndex BuildingInd = TargetIndex.A;
        private const TargetIndex MaterialInd = TargetIndex.B;
        private const int TicksDuration = 1000;

        private Building Building => (Building)job.GetTarget(BuildingInd).Thing;
        private Thing Material => job.GetTarget(MaterialInd).Thing;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Building, job, 1, -1, null, errorOnFailed)) return false;
            return pawn.Reserve(Material, job, 1, -1, null, errorOnFailed);
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(BuildingInd);

            yield return Toils_Goto.GotoThing(MaterialInd, PathEndMode.Touch)
                .FailOnDespawnedNullOrForbidden(MaterialInd)
                .FailOnSomeonePhysicallyInteracting(MaterialInd);

            yield return Toils_Haul.StartCarryThing(MaterialInd);

            yield return Toils_Goto.GotoThing(BuildingInd, PathEndMode.Touch).FailOnDespawnedOrNull(BuildingInd);

            Toil wait = Toils_General.Wait(TicksDuration);
            wait.FailOnDespawnedOrNull(BuildingInd);
            wait.FailOnCannotTouch(BuildingInd, PathEndMode.Touch);
            wait.WithEffect(Building.def.repairEffect, BuildingInd);
            wait.WithProgressBarToilDelay(BuildingInd);
            wait.activeSkill = () => SkillDefOf.Construction;
            yield return wait;

            Toil finish = ToilMaker.MakeToil("DshEco_RepairFinish");
            finish.initAction = delegate
            {
                Pawn actor = finish.actor;
                // 手里搬着的材料就是消耗掉的量
                Thing carried = actor.carryTracker?.CarriedThing;
                string costLog = carried != null ? (carried.def.LabelCap + "×" + carried.stackCount) : "无";
                if (carried != null) actor.carryTracker.DestroyCarriedThing();

                // 还有其它材料（少见，例如原料+材质不同）就从地图扣
                List<ThingDefCountClass> rest = EconomyDepreciation.RepairCost(Building);
                if (rest.Count > 0)
                {
                    List<ThingDefCountClass> after = new List<ThingDefCountClass>();
                    foreach (ThingDefCountClass c in rest)
                    {
                        int still = c.count;
                        foreach (Thing x in actor.Map.listerThings.ThingsOfDef(c.thingDef))
                        {
                            if (still <= 0) break;
                            if (x.IsForbidden(Faction.OfPlayer)) continue;
                            if (EconomyPrivate.OwnerOf(x) != null) continue;
                            int take = UnityEngine.Mathf.Min(still, x.stackCount);
                            x.stackCount -= take;
                            if (x.stackCount <= 0) x.Destroy(DestroyMode.Vanish);
                            still -= take;
                        }
                        if (still > 0) after.Add(new ThingDefCountClass(c.thingDef, still));
                    }
                    if (after.Count > 0)
                    {
                        Messages.Message(actor.LabelShort + " 修不了 " + Building.LabelShort + "：缺少 "
                            + string.Join("、", after.Select(c => c.thingDef.LabelCap + "×" + c.count).ToArray()),
                            Building, MessageTypeDefOf.NegativeEvent, false);
                        actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                        return;
                    }
                }

                if (Rand.Value > actor.GetStatValue(StatDefOf.FixBrokenDownBuildingSuccessChance))
                {
                    MoteMaker.ThrowText((actor.DrawPos + Building.DrawPos) / 2f, actor.Map,
                        "TextMote_FixBrokenDownBuildingFail".Translate(), 3.65f);
                    DevLog.Msg("[DEPRECIATION] 维修失败（成功率判定）：" + Building.LabelShort + "，消耗 " + costLog);
                }
                else
                {
                    Building.GetComp<CompBreakdownable>().Notify_Repaired();
                    actor.records?.Increment(RecordDefOf.ThingsRepaired);
                    DevLog.Msg("[DEPRECIATION] 修好 " + Building.LabelShort + "（" + Building.def.defName
                        + "）消耗 " + costLog);
                }
                actor.jobs.EndCurrentJob(JobCondition.Succeeded);
            };
            yield return finish;
        }
    }

    /// <summary>扫描故障的生产建筑，搬它需要的材料去修。</summary>
    public class WorkGiver_RepairBench : WorkGiver_Scanner
    {
        public override PathEndMode PathEndMode => PathEndMode.Touch;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map map = pawn?.MapHeld;
            if (map == null) yield break;
            foreach (Building b in EconomyDepreciation.ProductionBuildings(map))
            {
                if (EconomyDepreciation.IsBroken(b)) yield return b;
            }
        }

        public override bool ShouldSkip(Pawn pawn, bool forced = false)
        {
            if (pawn?.MapHeld == null) return true;
            return !EconomyDepreciation.ProductionBuildings(pawn.MapHeld).Any(EconomyDepreciation.IsBroken);
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (!(t is Building b) || !EconomyDepreciation.IsProduction(b)) return false;
            if (!EconomyDepreciation.IsBroken(b)) return false;
            if (pawn.WorkTypeIsDisabled(WorkTypeDefOf.Construction)) return false;
            if (!pawn.CanReserve(t, 1, -1, null, forced)) return false;
            if (!pawn.CanReach(t, PathEndMode.Touch, Danger.Deadly)) return false;
            return FindMaterial(pawn, b) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("DshEco_RepairBench");
            if (def == null) return null;
            Building b = t as Building;
            Thing mat = FindMaterial(pawn, b);
            if (mat == null) return null;

            int need = 0;
            foreach (ThingDefCountClass c in EconomyDepreciation.RepairCost(b))
            {
                if (c.thingDef == mat.def) { need = c.count; break; }
            }
            Job job = JobMaker.MakeJob(def, b, mat);
            job.count = UnityEngine.Mathf.Clamp(need, 1, mat.stackCount);
            return job;
        }

        /// <summary>找维修材料：优先主材料（数量最多的那种），数量够一次搬完。</summary>
        public static Thing FindMaterial(Pawn pawn, Building b)
        {
            if (b == null) return null;
            List<ThingDefCountClass> cost = EconomyDepreciation.RepairCost(b);
            if (cost.Count == 0) return null;

            foreach (ThingDefCountClass c in cost.OrderByDescending(x => x.count))
            {
                Thing found = GenClosest.ClosestThingReachable(
                    pawn.Position, pawn.MapHeld,
                    ThingRequest.ForDef(c.thingDef),
                    PathEndMode.Touch,
                    TraverseParms.For(pawn, Danger.Deadly),
                    9999f,
                    x => !x.IsForbidden(pawn)
                         && x.stackCount >= c.count
                         && EconomyPrivate.OwnerOf(x) == null
                         && pawn.CanReserve(x, 1, -1, null, false)
                         && pawn.CanReach(x, PathEndMode.Touch, Danger.Deadly));
                if (found != null) return found;
            }
            return null;
        }
    }
}

