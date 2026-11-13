using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>一类产出累计的投入 / 产出 / 工时（工单、采矿、建造共用）。</summary>
    public class BillRecord
    {
        public string label;
        public RecipeDef recipe;
        public int runs;
        public float inputValue;
        public float outputValue;
        public float laborDays;

        /// <summary>最后一次记账的 tick：用于按天自动清理旧记录。</summary>
        public int lastTick;

        public float WageValue
        {
            get
            {
                WagePolicy w = CurrentWage();
                float p = w != null ? w.laborDayValue : 20f;
                float s = w != null ? Mathf.Clamp01(w.wageShare) : 0.6f;
                return laborDays * p * s;
            }
        }

        public float Surplus
        {
            get { return outputValue - inputValue - WageValue; }
        }

        public float ProfitRate
        {
            get
            {
                float capital = inputValue + WageValue;
                return capital > 0.001f ? Surplus / capital : 0f;
            }
        }

        private static WagePolicy CurrentWage()
        {
            Map map = Find.CurrentMap;
            if (map == null) return null;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            return comp != null ? comp.wages : null;
        }
    }

    /// <summary>
    /// 生产记账：每完成一次生产，记录投入、产出（含品质）、工时。
    /// 覆盖三类来源：
    ///   1. 工作台配方（任何走 GenRecipe.MakeRecipeProducts 的，包括 mod）
    ///   2. 采矿（会话结束时矿已消失，按矿石产出记）
    ///   3. 建造完成（Frame.CompleteConstruction）
    /// </summary>
    public class EconomyBillLedger : MapComponent
    {
        // 半成品按"等分"：两段链时粗银取最终产品单价的一半
        public const float SemiFinishedShare = 0.5f;

        public List<BillRecord> records = new List<BillRecord>();
        private List<string> keys = new List<string>();
        private List<int> runs = new List<int>();
        private List<float> inputs = new List<float>();
        private List<float> outputs = new List<float>();
        private List<float> labors = new List<float>();
        private List<int> stamps = new List<int>();

        public EconomyBillLedger(Map map) : base(map)
        {
        }

        private BillRecord GetOrCreate(string label, RecipeDef recipe)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].label == label)
                {
                    records[i].lastTick = now;
                    return records[i];
                }
            }
            var rec = new BillRecord { label = label, recipe = recipe, lastTick = now };
            records.Add(rec);
            return rec;
        }

        /// <summary>按天清理：超过 days 天没有新记账的条目直接丢掉（0 或负数 = 不清理）。</summary>
        public int Prune(float days)
        {
            if (days <= 0.01f || records.Count == 0) return 0;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            int limit = (int)(days * GenDate.TicksPerDay);
            int removed = 0;
            for (int i = records.Count - 1; i >= 0; i--)
            {
                if (now - records[i].lastTick > limit)
                {
                    records.RemoveAt(i);
                    removed++;
                }
            }
            return removed;
        }

        /// <summary>工作台：一次配方完成（产物按品质折价）。</summary>
        public void RecordRecipe(Pawn worker, RecipeDef recipe, List<Thing> ingredients, IEnumerable<Thing> products)
        {
            if (recipe == null) return;

            float inValue = 0f;
            if (ingredients != null)
            {
                for (int i = 0; i < ingredients.Count; i++)
                {
                    Thing t = ingredients[i];
                    if (t == null || t.def == null) continue;
                    inValue += UnitValue(t.def) * Mathf.Max(1, t.stackCount);
                }
            }

            float outValue = 0f;
            if (products != null)
            {
                foreach (Thing t in products)
                {
                    if (t == null || t.def == null) continue;
                    float q = 1f;
                    CompQuality cq = t.TryGetComp<CompQuality>();
                    if (cq != null) q = EconomyPrice.QualityFactor(t);
                    outValue += UnitValue(t.def) * Mathf.Max(1, t.stackCount) * q;
                }
            }

            BillRecord rec = GetOrCreate(recipe.LabelCap, recipe);
            rec.runs++;
            rec.inputValue += inValue;
            rec.outputValue += outValue;
            rec.laborDays += recipe.workAmount / GenDate.TicksPerDay;
        }

        /// <summary>采矿 / 建造这类非工单劳动。</summary>
        public void RecordActivity(string label, float inputValue, float outputValue, float laborDays)
        {
            if (string.IsNullOrEmpty(label)) return;
            BillRecord rec = GetOrCreate(label, null);
            rec.runs++;
            rec.inputValue += inputValue;
            rec.outputValue += outputValue;
            rec.laborDays += laborDays;
        }

        public void RecordActivity(string label, float outputValue, float laborDays)
        {
            RecordActivity(label, 0f, outputValue, laborDays);
        }

        /// <summary>物品单价：半成品按最终产品的等分份额折算。</summary>
        public float UnitValue(ThingDef def)
        {
            if (def == null) return 0f;
            if (def.defName == "DshEco_RawSilver")
            {
                return EconomyPrice.UnitPrice(ThingDefOf.Silver, map) * SemiFinishedShare;
            }
            return EconomyPrice.UnitPrice(def, map);
        }

        public void ClearAll()
        {
            records.Clear();
        }

        public override void ExposeData()
        {
            base.ExposeData();
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                keys.Clear(); runs.Clear(); inputs.Clear(); outputs.Clear(); labors.Clear(); stamps.Clear();
                for (int i = 0; i < records.Count; i++)
                {
                    keys.Add(records[i].label);
                    runs.Add(records[i].runs);
                    inputs.Add(records[i].inputValue);
                    outputs.Add(records[i].outputValue);
                    labors.Add(records[i].laborDays);
                    stamps.Add(records[i].lastTick);
                }
            }
            Scribe_Collections.Look(ref keys, "dsh_billKeys2", LookMode.Value);
            Scribe_Collections.Look(ref runs, "dsh_billRuns2", LookMode.Value);
            Scribe_Collections.Look(ref inputs, "dsh_billInputs2", LookMode.Value);
            Scribe_Collections.Look(ref outputs, "dsh_billOutputs2", LookMode.Value);
            Scribe_Collections.Look(ref labors, "dsh_billLabors2", LookMode.Value);
            Scribe_Collections.Look(ref stamps, "dsh_billStamps2", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (keys == null) keys = new List<string>();
                if (runs == null) runs = new List<int>();
                if (inputs == null) inputs = new List<float>();
                if (outputs == null) outputs = new List<float>();
                if (labors == null) labors = new List<float>();
                if (stamps == null) stamps = new List<int>();
                records = new List<BillRecord>();
                for (int i = 0; i < keys.Count; i++)
                {
                    records.Add(new BillRecord
                    {
                        label = keys[i],
                        runs = i < runs.Count ? runs[i] : 0,
                        inputValue = i < inputs.Count ? inputs[i] : 0f,
                        outputValue = i < outputs.Count ? outputs[i] : 0f,
                        laborDays = i < labors.Count ? labors[i] : 0f,
                        lastTick = i < stamps.Count ? stamps[i] : 0
                    });
                }
            }
        }

        public string Report()
        {
            if (records.Count == 0) return "（还没有完成的工单）";
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < records.Count; i++)
            {
                BillRecord r = records[i];
                sb.AppendLine(r.label + " ×" + r.runs
                    + "　投入 " + r.inputValue.ToString("0.0")
                    + "　产出 " + r.outputValue.ToString("0.0")
                    + "　工资 " + r.WageValue.ToString("0.0")
                    + "　剩余 " + r.Surplus.ToString("0.0")
                    + "　利润率 " + (r.ProfitRate * 100f).ToString("0.0") + "%");
            }
            return sb.ToString();
        }
    }

    /// <summary>产出口：配方产物（含 mod）、采矿、建造。</summary>
    public static class EconomyProductionAccounting
    {
        public static bool Patch(Harmony harmony)
        {
            try
            {
                bool ok = true;

                var mk = AccessTools.Method(typeof(GenRecipe), "MakeRecipeProducts");
                if (mk != null)
                {
                    harmony.Patch(mk, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyProductionAccounting), "MakeProductsPostfix")));
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 GenRecipe.MakeRecipeProducts");
                    ok = false;
                }

                var build = AccessTools.Method(typeof(Frame), "CompleteConstruction");
                if (build != null)
                {
                    harmony.Patch(build, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyProductionAccounting), "BuildPostfix")));
                }
                return ok;
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 生产记账挂载失败：" + e.Message);
                return false;
            }
        }

        /// <summary>包一层迭代器：等游戏真正枚举产物时再记账，避免提前触发生成逻辑。</summary>
        public static void MakeProductsPostfix(ref IEnumerable<Thing> __result, RecipeDef recipeDef, Pawn worker, List<Thing> ingredients)
        {
            if (__result == null || worker == null || worker.Map == null) return;
            __result = Wrap(__result, worker, recipeDef, ingredients);
        }

        private static IEnumerable<Thing> Wrap(IEnumerable<Thing> src, Pawn worker, RecipeDef recipeDef, List<Thing> ingredients)
        {
            var produced = new List<Thing>();
            foreach (Thing t in src)
            {
                produced.Add(t);
                yield return t;
            }

            Map map = worker != null ? worker.Map : null;
            if (map == null) yield break;
            EconomyBillLedger ledger = map.GetComponent<EconomyBillLedger>();
            if (ledger != null) ledger.RecordRecipe(worker, recipeDef, ingredients, produced);
        }

        public static void BuildPostfix(Frame __instance, Pawn worker)
        {
            if (__instance == null || worker == null) return;
            Map map = worker.Map;
            if (map == null) return;
            EconomyBillLedger ledger = map.GetComponent<EconomyBillLedger>();
            if (ledger == null) return;

            ThingDef built = __instance.BuildDef;
            if (built == null) return;

            float materialValue = 0f;
            List<ThingDefCountClass> cost = __instance.TotalMaterialCost();
            for (int i = 0; i < cost.Count; i++)
            {
                materialValue += ledger.UnitValue(cost[i].thingDef) * cost[i].count;
            }

            float laborDays = __instance.WorkToBuild / GenDate.TicksPerDay;
            ledger.RecordActivity("建造·" + built.LabelCap, materialValue, built.GetStatValueAbstract(StatDefOf.MarketValue), laborDays);
        }
    }
}
