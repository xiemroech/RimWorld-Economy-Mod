using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 银矿场 / 银加工台专用的干活任务。
    ///
    /// 为什么不用原版 DoBill：1.7 里原版对这两个建筑始终返回空，硬拼出来的 DoBill 任务
    /// 会在同一 tick 里反复失败重建（日志 "started 10 jobs in one tick"），小人卡在原地
    /// 不睡不吃、进度也永远是 0。
    ///
    /// 这个驱动自己走流程：走到工位 → 累积工时 → 扣原料 → 出产品。
    /// 进度按“工位 + 配方”记在内存里，被打断（去睡觉/吃饭）后回来接着做，不从头开始。
    /// </summary>
    public class JobDriver_SilverWork : JobDriver
    {
        private const TargetIndex BenchInd = TargetIndex.A;

        private Building Bench => job.GetTarget(BenchInd).Thing as Building;

        private Bill Bill => job.bill;

        /// <summary>进度：工位loadID + 配方 → 已完成的 tick 数（被打断也不丢，读档也不丢）。</summary>
        private static Dictionary<string, float> progress = new Dictionary<string, float>();

        /// <summary>把进度写进存档（由 EconomyLaborSave 调用）。</summary>
        public static void ExposeData()
        {
            Scribe_Collections.Look(ref progress, "dsh_silverWorkProgress", LookMode.Value, LookMode.Value);
            if (progress == null) progress = new Dictionary<string, float>();
        }

        private string Key
        {
            get
            {
                Building b = Bench;
                Bill bill = Bill;
                if (b == null || bill == null || bill.recipe == null) return null;
                return b.GetUniqueLoadID() + "|" + bill.recipe.defName;
            }
        }

        private float TotalWork()
        {
            Bill bill = Bill;
            if (bill == null || bill.recipe == null) return 60f;
            float w = bill.recipe.workAmount;
            if (w <= 0f)
            {
                // 配方没写工时就看产物的 WorkToMake（原版数值在产物上）
                float per = 60f;
                if (bill.recipe.products != null && bill.recipe.products.Count > 0)
                {
                    ThingDefCountClass prod = bill.recipe.products[0];
                    if (prod != null && prod.thingDef != null)
                    {
                        float stat = prod.thingDef.GetStatValueAbstract(StatDefOf.WorkToMake);
                        if (stat > 0f) per = stat * UnityEngine.Mathf.Max(1, prod.count);
                    }
                }
                w = per;
            }
            return UnityEngine.Mathf.Max(30f, w);
        }

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            if (!pawn.Reserve(Bench, job, 1, -1, null, errorOnFailed)) return false;
            return true;
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            this.FailOnDespawnedNullOrForbidden(BenchInd);

            yield return Toils_Goto.GotoThing(BenchInd, PathEndMode.InteractionCell)
                .FailOnDespawnedNullOrForbidden(BenchInd);

            string key = Key;
            float total = TotalWork();
            if (key != null && !progress.ContainsKey(key)) progress[key] = 0f;

            Toil work = ToilMaker.MakeToil("DshEco_SilverWork");
            work.defaultCompleteMode = ToilCompleteMode.Never;
            work.FailOnCannotTouch(BenchInd, PathEndMode.InteractionCell);
            work.handlingFacing = true;
            work.WithProgressBar(BenchInd, delegate
            {
                float done = (key != null && progress.ContainsKey(key)) ? progress[key] : 0f;
                return UnityEngine.Mathf.Clamp01(done / total);
            }, false, -0.5f);

            SkillDef skill = null;
            Bill bill = Bill;
            if (bill != null && bill.recipe != null) skill = bill.recipe.workSkill;
            if (skill != null) work.activeSkill = () => skill;

            work.tickAction = delegate
            {
                Bill b = Bill;
                Building bench = Bench;
                if (b == null || bench == null) { EndJobWith(JobCondition.Incompletable); return; }
                if (key == null) { EndJobWith(JobCondition.Incompletable); return; }

                float done = progress.TryGetValue(key, out float d) ? d : 0f;
                done += 1f;
                if (done < total) { progress[key] = done; return; }

                // 到点了：扣原料、出产品
                progress.Remove(key);
                if (!FinishWork(pawn, bench, b)) { EndJobWith(JobCondition.Incompletable); return; }
                EndJobWith(JobCondition.Succeeded);
            };

            yield return work;
        }

        /// <summary>扣原料 + 出成品。原料不够就返回 false（不产出、不扣）。</summary>
        private static bool FinishWork(Pawn pawn, Building bench, Bill bill)
        {
            Map map = bench.MapHeld;
            RecipeDef recipe = bill.recipe;
            if (map == null || recipe == null) return false;

            List<Thing> used = new List<Thing>();
            if (recipe.ingredients != null)
            {
                for (int i = 0; i < recipe.ingredients.Count; i++)
                {
                    IngredientCount ing = recipe.ingredients[i];
                    if (ing == null) continue;
                    int need = UnityEngine.Mathf.RoundToInt(ing.count);
                    if (need <= 0) continue;
                    if (!TakeFromMap(map, pawn, ing, need, used)) return false;
                }
            }

            if (recipe.products != null)
            {
                for (int i = 0; i < recipe.products.Count; i++)
                {
                    ThingDefCountClass prod = recipe.products[i];
                    if (prod == null || prod.thingDef == null) continue;
                    Thing made = ThingMaker.MakeThing(prod.thingDef, null);
                    made.stackCount = UnityEngine.Mathf.Max(1, prod.count);
                    GenPlace.TryPlaceThing(made, bench.InteractionCell, map, ThingPlaceMode.Near);
                }
            }

            // 账单计数：只有“做 N 次”模式才需要减
            try
            {
                if (bill is Bill_Production bp && bp.repeatMode == BillRepeatModeDefOf.RepeatCount
                    && bp.repeatCount > 0)
                {
                    bp.repeatCount--;
                }
            }
            catch { }

            DevLog.Msg("[SILVER] " + pawn.LabelShort + " 做完 " + recipe.defName + "（" + bench.def.defName + "）");
            return true;
        }

        private static bool TakeFromMap(Map map, Pawn pawn, IngredientCount ing, int need, List<Thing> used)
        {
            int still = need;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (still <= 0) break;
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (!ing.filter.Allows(t.def)) continue;
                if (t.IsForbidden(pawn)) continue;
                int take = UnityEngine.Mathf.Min(still, t.stackCount);
                if (take <= 0) continue;
                if (take >= t.stackCount)
                {
                    used.Add(t);
                    t.Destroy(DestroyMode.Vanish);
                }
                else
                {
                    Thing part = t.SplitOff(take);
                    used.Add(part);
                    part.Destroy(DestroyMode.Vanish);
                }
                still -= take;
            }
            return still <= 0;
        }
    }
}
