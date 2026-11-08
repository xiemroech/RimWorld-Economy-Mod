using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 植入体需求：这个人缺了哪个部位 → 该买哪一件植入体。
    ///
    /// 全部走原版机制，不假设任何 mod 的 defName：
    ///   RecipeDef.addsHediff                    —— 这个手术装的是什么
    ///   RecipeDef.appliedOnFixedBodyParts       —— 装在哪个部位
    ///   HediffDef.addedPartProps.partEfficiency —— 这件植入体的"工作效率"
    ///
    /// 查不到就返回空列表，永远不会抛异常；装 0 个 mod 和装 500 个 mod 行为一致。
    /// </summary>
    public static class EconomyImplantDemand
    {
        /// <summary>一个候选方案。</summary>
        public class Option
        {
            public BodyPartDef part;      // 缺的部位
            public RecipeDef recipe;      // 对应的手术
            public ThingDef item;         // 需要的那件植入体
            public float efficiency;      // 工作效率（原版 partEfficiency）
            public int price;             // 现价
        }

        /// <summary>预算上限 = 月收入 × 这个系数。超了就降一档。</summary>
        public const float BudgetMonths = 3f;

        private static bool built;
        private static readonly List<RecipeDef> implantRecipes = new List<RecipeDef>();

        public static void Reset()
        {
            built = false;
            implantRecipes.Clear();
        }

        private static void Build()
        {
            if (built) return;
            built = true;
            implantRecipes.Clear();
            try
            {
                List<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    RecipeDef r = all[i];
                    try
                    {
                        if (r == null || r.addsHediff == null || r.ingredients == null) continue;
                        if (FirstImplantIngredient(r) == null) continue;
                        implantRecipes.Add(r);
                    }
                    catch { }
                }
            }
            catch { }
            DevLog.Msg("[DSH-ECONOMY] 植入体配方索引：" + implantRecipes.Count + " 条");
        }

        /// <summary>配方里的那件植入体（器官/仿生体/血清/基因）。</summary>
        private static ThingDef FirstImplantIngredient(RecipeDef r)
        {
            if (r == null || r.ingredients == null) return null;
            for (int i = 0; i < r.ingredients.Count; i++)
            {
                IngredientCount ing = r.ingredients[i];
                if (ing == null) continue;
                ThingDef d = ing.IsFixedIngredient
                    ? ing.FixedIngredient
                    : (ing.filter != null ? ing.filter.AnyAllowedDef : null);
                if (d == null) continue;
                if (EconomyConsumption.IsImplantLike(d)) return d;
            }
            return null;
        }

        /// <summary>这个手术能不能装在指定部位上。没限定部位的手术（血清/基因）对任何部位都算候选。</summary>
        private static bool CoversPart(RecipeDef r, BodyPartDef part)
        {
            if (r == null) return false;
            if (r.appliedOnFixedBodyParts != null && r.appliedOnFixedBodyParts.Count > 0)
                return r.appliedOnFixedBodyParts.Contains(part);
            return true;
        }

        /// <summary>植入体的工作效率。原版没写就当 1.0（和原生器官一样）。</summary>
        private static float EfficiencyOf(RecipeDef r)
        {
            try
            {
                HediffDef h = r != null ? r.addsHediff : null;
                if (h != null && h.addedPartProps != null)
                    return Mathf.Max(0.01f, h.addedPartProps.partEfficiency);
            }
            catch { }
            return 1f;
        }

        /// <summary>这个人买得起多贵的植入体：月收入 × BudgetMonths。</summary>
        public static float Budget(Pawn p)
        {
            try
            {
                float wage = EconomyLedger.GetIncome(p);
                if (wage <= 0.01f) wage = EconomyLedger.GetLastIncome(p);
                return Mathf.Max(0f, wage) * BudgetMonths;
            }
            catch { }
            return 0f;
        }

        /// <summary>
        /// 这个人的全部植入需求（每个缺失部位选一件）。
        /// 规则：同一部位按效率从高到低试，第一个价格不超预算的就是答案。
        /// </summary>
        public static List<Option> Needs(Pawn p, Map map)
        {
            List<Option> result = new List<Option>();
            if (p == null || map == null || p.health == null) return result;

            Build();
            if (implantRecipes.Count == 0) return result;

            float budget = Budget(p);

            try
            {
                List<Hediff_MissingPart> missing = p.health.hediffSet.GetMissingPartsCommonAncestors();
                if (missing == null || missing.Count == 0) return result;

                for (int i = 0; i < missing.Count; i++)
                {
                    Hediff_MissingPart mp = missing[i];
                    if (mp == null || mp.Part == null) continue;
                    BodyPartDef partDef = mp.Part.def;
                    if (partDef == null) continue;

                    Option best = null;
                    float bestEff = -1f;

                    for (int j = 0; j < implantRecipes.Count; j++)
                    {
                        RecipeDef r = implantRecipes[j];
                        if (!CoversPart(r, partDef)) continue;

                        ThingDef item = FirstImplantIngredient(r);
                        if (item == null) continue;

                        float eff = EfficiencyOf(r);
                        if (eff <= bestEff) continue;      // 已经找到更好的

                        int price;
                        try { price = Mathf.RoundToInt(EconomyPrice.UnitPrice(item, map)); }
                        catch { continue; }

                        // 超预算：跳过这一档，继续找更便宜的（后面的效率更低）
                        if (budget > 0f && price > budget) continue;

                        bestEff = eff;
                        best = new Option
                        {
                            part = partDef,
                            recipe = r,
                            item = item,
                            efficiency = eff,
                            price = price
                        };
                    }

                    if (best != null) result.Add(best);
                }
            }
            catch { }

            return result;
        }

        /// <summary>这个人现在需不需要这件植入体（用于解除"植入体不能买"的一刀切）。</summary>
        public static bool Wanted(Pawn p, ThingDef def)
        {
            if (p == null || def == null) return false;
            try
            {
                List<Option> needs = Needs(p, p.MapHeld);
                for (int i = 0; i < needs.Count; i++)
                {
                    if (needs[i] != null && needs[i].item == def) return true;
                }
            }
            catch { }
            return false;
        }

        /// <summary>他最想买的那件植入体的价格（攒钱目标用）。没有需求返回 0。</summary>
        public static int WantedPrice(Pawn p)
        {
            if (p == null) return 0;
            Map map = p.MapHeld;
            if (map == null) return 0;
            List<Option> needs = Needs(p, map);
            int best = 0;
            for (int i = 0; i < needs.Count; i++)
            {
                if (needs[i] != null && needs[i].price > best) best = needs[i].price;
            }
            return best;
        }

        /// <summary>一句话摘要，给提示和日志用。</summary>
        public static string Summary(Pawn p)
        {
            if (p == null) return "";
            Map map = p.MapHeld;
            if (map == null) return "";
            List<Option> needs = Needs(p, map);
            if (needs.Count == 0) return p.LabelShort + "：无植入需求";
            string s = p.LabelShort + "：";
            for (int i = 0; i < needs.Count; i++)
            {
                Option o = needs[i];
                s += (i > 0 ? "；" : "")
                   + (o.part != null ? o.part.label : "部位")
                   + "→" + (o.item != null ? o.item.label : "?")
                   + "（效率 " + o.efficiency.ToString("0.00")
                   + "，价 " + o.price + "）";
            }
            return s;
        }
    }
}
