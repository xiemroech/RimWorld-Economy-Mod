using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 商品价值 = c + v + s（马克思的价值观）。
    ///
    ///   c = 不变资本：原料的价值（递归，原料本身也按自己的 c+v+s 算）
    ///   v + s = 新创造的价值：社会必要劳动时间 × 劳动日价值
    ///           （v/s 的拆分由工资比例决定，但那属于分配，不影响价值本身）
    ///
    /// 只有能找到配方的物品才算得出价值；原木、矿石这类"自然资源"没有配方，
    /// 退回原版市价作为它们的价值。
    /// </summary>
    public static class EconomyValue
    {
        private static readonly Dictionary<string, float> cache = new Dictionary<string, float>();
        private static readonly Dictionary<string, RecipeDef> makers = new Dictionary<string, RecipeDef>();
        private static readonly HashSet<ThingDef> computing = new HashSet<ThingDef>();
        private static bool indexBuilt;

        /// <summary>改设置（劳动日价值）后清缓存。</summary>
        public static void Reset()
        {
            cache.Clear();
            indexBuilt = false;
            makers.Clear();
        }

        public static float Of(ThingDef def, Map map)
        {
            if (def == null) return 0f;
            if (!indexBuilt) BuildIndex();

            float v;
            if (cache.TryGetValue(def.defName, out v)) return v;

            v = Compute(def, map, 0);
            cache[def.defName] = v;
            return v;
        }

        /// <summary>这个物品的原料成本 c。</summary>
        public static float MaterialCost(ThingDef def, Map map)
        {
            RecipeDef r = MakerOf(def);
            if (r == null || r.ingredients == null) return 0f;
            float c = 0f;
            for (int i = 0; i < r.ingredients.Count; i++)
            {
                IngredientCount ing = r.ingredients[i];
                ThingDef mat = IngredientDef(ing);
                if (mat == null) continue;
                c += Of(mat, map) * Mathf.Max(1f, ing.GetBaseCount());
            }
            return c;
        }

        /// <summary>这个物品包含的新创造价值 v+s（劳动价值）。</summary>
        public static float LaborValue(ThingDef def, Map map)
        {
            RecipeDef r = MakerOf(def);
            if (r == null) return 0f;
            float days = r.workAmount / (float)GenDate.TicksPerDay;
            int count = ProductCount(r, def);
            return days * LaborDayValue() / Mathf.Max(1, count);
        }

        private static float Compute(ThingDef def, Map map, int depth)
        {
            if (def == null) return 0f;
            if (depth > 4 || computing.Contains(def)) return Fallback(def);

            RecipeDef r = MakerOf(def);
            if (r == null) return Fallback(def);

            computing.Add(def);
            float c = 0f;
            if (r.ingredients != null)
            {
                for (int i = 0; i < r.ingredients.Count; i++)
                {
                    IngredientCount ing = r.ingredients[i];
                    ThingDef mat = IngredientDef(ing);
                    if (mat == null) continue;
                    c += Compute(mat, map, depth + 1) * Mathf.Max(1f, ing.GetBaseCount());
                }
            }
            computing.Remove(def);

            float days = r.workAmount / (float)GenDate.TicksPerDay;
            float v = days * LaborDayValue();
            int count = ProductCount(r, def);
            return (c + v) / Mathf.Max(1, count);
        }

        private static float Fallback(ThingDef def)
        {
            return Mathf.Max(0.1f, def.GetStatValueAbstract(StatDefOf.MarketValue));
        }

        private static float LaborDayValue()
        {
            Map map = Find.CurrentMap;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp != null && comp.wages != null) return Mathf.Max(1f, comp.wages.laborDayValue);
            return 150f;
        }

        private static int ProductCount(RecipeDef r, ThingDef def)
        {
            if (r.products == null) return 1;
            for (int i = 0; i < r.products.Count; i++)
            {
                if (r.products[i].thingDef == def) return Mathf.Max(1, r.products[i].count);
            }
            return 1;
        }

        private static ThingDef IngredientDef(IngredientCount ing)
        {
            if (ing == null) return null;
            if (ing.IsFixedIngredient) return ing.FixedIngredient;
            return ing.filter != null ? ing.filter.AnyAllowedDef : null;
        }

        public static RecipeDef MakerOf(ThingDef def)
        {
            if (def == null) return null;
            if (!indexBuilt) BuildIndex();
            RecipeDef r;
            return makers.TryGetValue(def.defName, out r) ? r : null;
        }

        private static void BuildIndex()
        {
            indexBuilt = true;
            makers.Clear();
            List<RecipeDef> all = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                RecipeDef r = all[i];
                if (r == null || r.products == null || r.workAmount <= 0f) continue;
                // 优先选"有工作台、产物单一"的配方，避免把拆解/屠宰当成制造
                for (int j = 0; j < r.products.Count; j++)
                {
                    ThingDef p = r.products[j].thingDef;
                    if (p == null) continue;
                    if (r.products.Count != 1) continue;
                    if (p.IsCorpse || p.category != ThingCategory.Item) continue;
                    RecipeDef old;
                    if (!makers.TryGetValue(p.defName, out old) || r.workAmount > old.workAmount)
                    {
                        makers[p.defName] = r;
                    }
                }
            }
        }
    }
}
