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
    /// 这里把原版 DoBill 的判定原样借过来用，让采矿工人也能干这份账单。
    /// </summary>
    public class WorkGiver_SilverMine : WorkGiver_Scanner
    {
        private static WorkGiver_DoBill vanilla;

        private static WorkGiver_DoBill Vanilla()
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

        private static int scanProbe;

        public override IEnumerable<Thing> PotentialWorkThingsGlobal(Pawn pawn)
        {
            Map map = pawn != null ? pawn.Map : null;
            if (map == null) yield break;
            int n = 0;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b == null || b.def == null) continue;
                if (b.def.defName != "DshEco_SilverMine") continue;
                n++;
                yield return b;
            }
            if (scanThingsProbe < 6)
            {
                scanThingsProbe++;
                Verse.Log.Message("[MINE] 扫描到矿场数=" + n + " 人=" + (pawn != null ? pawn.LabelShort : "无"));
            }
        }

        private static int scanThingsProbe;

        public override bool HasJobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            Job job = JobOnThing(pawn, t, forced);
            if (t != null && t.def != null && t.def.defName == "DshEco_SilverMine" && mineProbe < 40)
            {
                mineProbe++;
                string bills = "无";
                try
                {
                    IBillGiver giver = t as IBillGiver;
                    bills = giver == null ? "不是账单建筑" : giver.BillStack.Count + " 张";
                }
                catch { }
                Verse.Log.Message("[MINE] 派活器被问到：人=" + (pawn != null ? pawn.LabelShort : "无")
                    + " 账单=" + bills
                    + " 原版DoBill=" + (Vanilla() != null)
                    + " 结果=" + (job != null));
            }
            return job != null;
        }

        private static int mineProbe;

        public override Job JobOnThing(Pawn pawn, Thing t, bool forced = false)
        {
            if (pawn == null || t == null) return null;
            if (t.def == null || t.def.defName != "DshEco_SilverMine") return null;
            if (t.IsForbidden(pawn)) return null;

            WorkGiver_DoBill doBill = Vanilla();
            if (doBill == null) return null;
            Job job = null;
            try
            {
                SilverMineOnlyMiners.Bypass = true;
                job = doBill.JobOnThing(pawn, t, forced);
            }
            catch { job = null; }
            finally { SilverMineOnlyMiners.Bypass = false; }

            if (job == null) job = BuildJob(pawn, t);
            if (job == null && mineDetail < 10) { mineDetail++; Explain(pawn, t); }
            if (job != null && mineOK < 10)
            {
                mineOK++;
                Verse.Log.Message("[MINE] 派活了：" + (pawn != null ? pawn.LabelShort : "无") + " → 银矿场");
            }
            return job;
        }

        private static int mineOK;

        private static int mineDetail;

        /// <summary>
        /// 原版的 DoBill 判定在 1.7 里对这张账单始终返回 null（各项条件都是 True，却派不出）。
        /// 不再依赖它：自己找原料、自己拼一个标准的 DoBill 任务，格式与原版一致。
        /// </summary>
        private static Job BuildJob(Pawn p, Thing bench)
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

                IngredientCount ing = bill.recipe.ingredients[0];
                int need = (int)ing.count;
                if (need <= 0) return null;

                Map map = bench.Map;
                List<ThingCount> picked = new List<ThingCount>();
                int got = 0;
                foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
                {
                    if (got >= need) break;
                    if (t == null || t.Destroyed || !t.Spawned) continue;
                    if (!ing.filter.Allows(t.def)) continue;
                    if (t.IsForbidden(p)) continue;
                    if (!p.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                    int take = UnityEngine.Mathf.Min(need - got, t.stackCount);
                    if (take <= 0) continue;
                    picked.Add(new ThingCount(t, take));
                    got += take;
                }
                if (got < need) return null;

                Job job = JobMaker.MakeJob(JobDefOf.DoBill, bench);
                job.bill = bill;
                job.haulMode = HaulMode.ToCellStorage;
                job.targetQueueB = new List<LocalTargetInfo>();
                job.countQueue = new List<int>();
                for (int i = 0; i < picked.Count; i++)
                {
                    job.targetQueueB.Add(new LocalTargetInfo(picked[i].Thing));
                    job.countQueue.Add(picked[i].Count);
                }
                job.count = 1;
                return job;
            }
            catch (System.Exception e)
            {
                Verse.Log.Message("[MINE] 自己拼任务失败：" + e.Message);
                return null;
            }
        }

        /// <summary>派不出活时，把"差在哪"一次全打出来，省得再来回猜。</summary>
        private static void Explain(Pawn p, Thing t)
        {
            try
            {
                Map map = t.Map;
                IBillGiver giver = t as IBillGiver;
                Bill bill = giver != null && giver.BillStack != null && giver.BillStack.Count > 0 ? giver.BillStack[0] : null;
                IntVec3 cell = t.InteractionCell;
                string why = "账单=无";
                if (bill != null)
                {
                    float needWood = 0f;
                    try { needWood = bill.recipe != null && bill.recipe.ingredients != null && bill.recipe.ingredients.Count > 0 ? bill.recipe.ingredients[0].count : 0f; } catch { }
                    int haveWood = 0;
                    try { haveWood = map.resourceCounter.GetCount(ThingDefOf.WoodLog); } catch { }
                    why = "该做了=" + bill.ShouldDoNow()
                        + " 暂停=" + bill.suspended
                        + " 允许此人=" + bill.PawnAllowedToStartAnew(p)
                        + " 需木材=" + needWood + " 仓里木材=" + haveWood
                        + " 研究=" + (bill.recipe.researchPrerequisite == null || bill.recipe.researchPrerequisite.IsFinished)
                        + " 配方可用=" + bill.recipe.AvailableNow
                        + " 指定派活类型=" + (bill.recipe.requiredGiverWorkType != null ? bill.recipe.requiredGiverWorkType.defName : "无")
                        + " 操作力=" + p.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation).ToString("0.00")
                        + " 技能满足=" + (bill.recipe.workSkill == null || bill.recipe.PawnSatisfiesSkillRequirements(p))
                        + " 找料成功=" + FindIngredients(Vanilla(), bill, p, t);
                }
                Verse.Log.Message("[MINE] 为什么派不出：" + (p != null ? p.LabelShort : "无")
                    + " 换手格=" + cell + " 可站=" + cell.Standable(map)
                    + " 可达=" + p.CanReach(cell, PathEndMode.OnCell, Danger.Deadly)
                    + " 可预约=" + p.CanReserve(t, 1, -1, null, false)
                    + " || " + why);
            }
            catch (System.Exception e) { Verse.Log.Message("[MINE] 解释失败：" + e.Message); }
        }

        /// <summary>原版"能不能找到原料"的那一步，单独调一次看结果。</summary>
        private static string FindIngredients(WorkGiver_DoBill doBill, Bill bill, Pawn p, Thing bench)
        {
            try
            {
                System.Reflection.MethodInfo mi = HarmonyLib.AccessTools.Method(
                    typeof(WorkGiver_DoBill), "TryFindBestBillIngredients",
                    new[] { typeof(Bill), typeof(Pawn), typeof(Thing), typeof(System.Collections.Generic.List<ThingCount>) });
                if (mi == null) return "没这个方法";
                var list = new System.Collections.Generic.List<ThingCount>();
                object[] args = new object[] { bill, p, bench, list };
                object r = mi.Invoke(doBill, args);
                return (r is bool b && b) + "(数量" + list.Count + ")";
            }
            catch (System.Exception e) { return "异常:" + e.GetType().Name; }
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
            WorkGiverDef mine = DefDatabase<WorkGiverDef>.GetNamedSilentFail("DshEco_MineSilverBill");
            Verse.Log.Message("[DSH-ECONOMY] 银矿派活器已加载=" + (mine != null)
                + " 工作类型=" + (mine != null && mine.workType != null ? mine.workType.defName : "无"));
            System.Reflection.MethodInfo target = HarmonyLib.AccessTools.Method(
                typeof(WorkGiver_DoBill), "JobOnThing",
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            if (target == null)
            {
                Verse.Log.Warning("[DSH-ECONOMY] 银矿只给矿工：没找到 WorkGiver_DoBill.JobOnThing");
                return;
            }
            harmony.Patch(target,
                prefix: new HarmonyLib.HarmonyMethod(
                    typeof(SilverMineOnlyMiners).GetMethod(nameof(Prefix),
                        System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)));
            Verse.Log.Message("[DSH-ECONOMY] 银矿只给矿工：已挂 DoBill 拦截");
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
