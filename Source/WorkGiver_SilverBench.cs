using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 银加工台（精炼）的派活器，挂在“制作”工作类型下。
    ///
    /// 原因和银矿场一样：原版 DoBill 对这两个建筑在 1.7 里始终返回空，
    /// 右键没有干活选项、小人也不来。这里直接复用银矿那套“自己拼 DoBill 任务”的实现。
    /// </summary>
    public class WorkGiver_SilverBench : WorkGiver_Scanner
    {
        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map map = pawn != null ? pawn.Map : null;
            if (map == null) yield break;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b == null || b.def == null) continue;
                if (b.def.defName != "DshEco_SilverBench") continue;
                yield return b;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobOnThing(pawn, t, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn == null || t == null || t.def == null) return null;
            if (t.def.defName != "DshEco_SilverBench") return null;
            if (t.IsForbidden(pawn)) return null;

            WorkGiver_DoBill doBill = WorkGiver_SilverMine.Vanilla();
            Job job = null;
            if (doBill != null)
            {
                try { job = doBill.JobOnThing(pawn, t, forced); }
                catch { job = null; }
            }

            if (job == null) job = WorkGiver_SilverMine.BuildJob(pawn, t);
            return job;
        }
    }
}
