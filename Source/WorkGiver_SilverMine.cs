using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 银矿场专用的派活器，挂在“采矿”工作类型下。
    ///
    /// 原版所有工作台账单都由“制作”（WorkGiver_DoBill）派发，配方写的是采矿技能也没用：
    /// 只勾了采矿的矿工永远轮不到，所以银矿场看起来“怎么都不出料”。
    /// 这里先借原版 DoBill 判定，借不到就自己拼一个格式一致的任务，让采矿工人也能干这份账单。
    /// </summary>
    public class WorkGiver_SilverMine : WorkGiver_Scanner
    {
        private static WorkGiver_DoBill vanilla;

        internal static WorkGiver_DoBill Vanilla()
        {
            if (vanilla != null) return vanilla;
            foreach (WorkGiverDef d in DefDatabase<WorkGiverDef>.AllDefs)
            {
                if (d == null || d.Worker == null) continue;
                if (d.defName == "DoBill") { vanilla = d.Worker as WorkGiver_DoBill; break; }
                if (vanilla == null && d.workType != null && d.workType.defName == "Crafting")
                    vanilla = d.Worker as WorkGiver_DoBill;
            }
            return vanilla;
        }

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map map = pawn != null ? pawn.Map : null;
            if (map == null) yield break;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b == null || b.def == null) continue;
                if (b.def.defName != "DshEco_SilverMine") continue;
                yield return b;
            }
        }

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            return JobOnThing(pawn, t, forced) != null;
        }

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn == null || t == null) return null;
            if (t.def == null || t.def.defName != "DshEco_SilverMine") return null;
            if (t.IsForbidden(pawn)) return null;

            WorkGiver_DoBill doBill = Vanilla();
            Job job = null;
            if (doBill != null)
            {
                try
                {
                    SilverMineOnlyMiners.Bypass = true;
                    job = doBill.JobOnThing(pawn, t, forced);
                }
                catch { job = null; }
                finally { SilverMineOnlyMiners.Bypass = false; }
            }

            if (job == null) job = BuildJob(pawn, t);
            return job;
        }

        /// <summary>
        /// 原版的 DoBill 判定在 1.7 里对这张账单始终返回 null（各项条件都是 True，却派不出）。
        /// 硬拼出来的 DoBill 任务又会同一 tick 反复失败重建（"started 10 jobs in one tick"），
        /// 人卡在原地不睡不吃、进度永远 0。所以改成我们自己的任务：JobDriver_SilverWork。
        /// </summary>
        internal static Job BuildJob(Pawn p, Thing bench)
        {
            try
            {
                if (p == null || bench == null) return null;
                IBillGiver giver = bench as IBillGiver;
                if (giver == null || giver.BillStack == null) return null;

                Bill bill = null;
                for (int i = 0; i < giver.BillStack.Count; i++)
                {
                    Bill b = giver.BillStack[i];
                    if (b != null && b.recipe != null && b.ShouldDoNow() && b.PawnAllowedToStartAnew(p)) { bill = b; break; }
                }
                if (bill == null) return null;
                if (bill.recipe.ingredients == null || bill.recipe.ingredients.Count == 0) return null;

                // 原料得够（不够不派，免得人走过去干等）
                IngredientCount ing = bill.recipe.ingredients[0];
                int need = UnityEngine.Mathf.RoundToInt(ing.count);
                if (need <= 0) return null;
                int got = 0;
                foreach (Thing t in bench.Map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                {
                    if (got >= need) break;
                    if (t == null || t.Destroyed || !t.Spawned) continue;
                    if (!ing.filter.Allows(t.def)) continue;
                    if (t.IsForbidden(p)) continue;
                    got += t.stackCount;
                }
                if (got < need) return null;

                // 工位被占着就先不派
                if (!p.CanReserveAndReach(bench, PathEndMode.InteractionCell, Danger.Deadly, 1, -1)) return null;

                JobDef def = DefDatabase<JobDef>.GetNamedSilentFail("DshEco_SilverWork");
                if (def == null) return null;

                Job job = JobMaker.MakeJob(def, bench);
                job.bill = bill;
                job.count = 1;
                return job;
            }
            catch
            {
                return null;
            }
        }
    }

    /// <summary>
    /// 银矿场只让“采矿”工人干：把原版 DoBill（制作）对银矿场的派活挡掉。
    /// 我们自己的 WorkGiver_SilverMine 调用时把 Bypass 置真，绕过这道拦截。
    /// </summary>
    public static class SilverMineOnlyMiners
    {
        [System.ThreadStatic] public static bool Bypass;

        public static void Apply(HarmonyLib.Harmony harmony)
        {
            System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method(
                typeof(WorkGiver_DoBill), "JobOnThing",
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            if (target == null) return;
            harmony.Patch(target,
                prefix: new HarmonyLib.HarmonyMethod(
                    typeof(SilverMineOnlyMiners).GetMethod(nameof(Prefix),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)));
        }

        public static bool Prefix(Thing thing, ref Job __result)
        {
            if (Bypass) return true;
            if (thing == null || thing.def == null) return true;
            if (thing.def.defName != "DshEco_SilverMine") return true;
            __result = null;
            return false;
        }
    }
}
