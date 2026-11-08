using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 唯一的定价管道。其他地方一律调这里，不要自己算价格，
    /// 否则后期接入贸易、工资、公共化时会出现双重计价。
    ///
    /// 最终单价 = 原版市场价（成本价）
    ///          × 分类价格倍率 × 物品价格倍率
    ///          × 公共化系数（0% = 利润倍率，50% = 1.0，100% = 0）
    ///          × 本地银价指数
    /// </summary>
    public static class EconomyPrice
    {
        /// <summary>本地银价指数：库存白银越多，白银购买力越低，物价倍率越高。</summary>
        public static float SilverIndex(Map map)
        {
            if (map == null) return 1f;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            return comp != null ? comp.SilverIndex : 1f;
        }

        /// <summary>该分类当前生效的公共化程度 0-100。</summary>
        public static float Publicization(EconomyCategoryDef cat, Map map)
        {
            if (cat == null) return 0f;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            return Mathf.Clamp(comp != null ? comp.GetPublicization(cat) : cat.defaultPublicization, 0f, 100f);
        }

        /// <summary>公共化 -> 相对"成本价"的价格系数。
        /// 0%   = 完全商品化，按成本价 × 利润倍率出售（卖家赚钱）
        /// 50%  = 成本价，也就是原版市场价（"原本价格"）
        /// 100% = 免费
        /// </summary>
        public static float PublicizationFactor(float publicization)
        {
            float pub = Mathf.Clamp(publicization, 0f, 100f);
            if (pub >= 100f) return 0f;
            if (pub <= 50f)
            {
                float t = pub / 50f;
                return Mathf.Lerp(ProfitMultiplier, 1f, t);
            }
            float t2 = (pub - 50f) / 50f;
            return Mathf.Lerp(1f, 0f, t2);
        }

        /// <summary>0% 公共化时的售价 / 成本价。</summary>
        public static float ProfitMultiplier
        {
            get
            {
                DshEconomySettings s = DshEconomyMod.Settings;
                return s != null ? Mathf.Max(1f, s.profitMultiplier) : 1.5f;
            }
        }

        /// <summary>组合系数（不含银价指数），用于界面展示。</summary>
        public static float CombinedFactor(ThingDef def, Map map)
        {
            EconomyCategoryDef cat = EconomyRegistry.CategoryOf(def);
            float catFactor = cat != null ? cat.priceFactor : 1f;
            float itemFactor = 1f;
            float pub = 0f;

            EconomyItemExtension ext = def.GetModExtension<EconomyItemExtension>();
            if (ext != null)
            {
                itemFactor = ext.priceFactor;
                if (ext.publicizationOverride >= 0f) pub = Mathf.Clamp(ext.publicizationOverride, 0f, 100f);
                else pub = Publicization(cat, map);
            }
            else
            {
                pub = Publicization(cat, map);
            }

            return Mathf.Max(0f, catFactor) * Mathf.Max(0f, itemFactor) * PublicizationFactor(pub);
        }

        // 供求偏离的最大幅度：价格只在价值上下 15% 内摆动（马克思：价格围绕价值波动）
        public const float ScarcityRange = 0.15f;

        /// <summary>
        /// 参考库存：约 2000 银的货折成多少件。
        /// 返回 -1 = 原版没给这件东西标价，算不出来（面板显示"—"，稀缺度不参与）。
        /// 上限按堆叠上限的 2 倍封顶，避免 0.1 银的东西算出 20000 件那种假数字。
        /// </summary>
        public static float ReferenceStock(ThingDef def)
        {
            if (def == null) return -1f;
            float vanilla = EconomyValue.VanillaPrice(def);
            if (vanilla <= 0.01f) return -1f;
            return Mathf.Min(2000f / vanilla, Mathf.Max(2f, def.stackLimit * 2f));
        }

        /// <summary>稀缺弹性指数 k：越大，缺货时涨得越猛。</summary>
        public const float ScarcityElasticity = 0.5f;
        /// <summary>价格倍率硬夹：再缺也不超过 3 倍，再积压也不低于 0.3 倍。</summary>
        public const float ElasticMin = 0.3f;
        public const float ElasticMax = 3f;

        /// <summary>
        /// 稀缺系数：库存相对"参考库存"的缺口，用幂函数放大，再乘需求（成交量）因子。
        /// 不改变价值本身，只让价格围绕价值波动。
        /// </summary>
        public static float ScarcityFactor(ThingDef def, Map map)
        {
            if (def == null || map == null) return 1f;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return 1f;

            float value = Mathf.Max(0.1f, EconomyValue.Of(def, map));
            // 参考库存 = 2000 银 ÷ 原版市价。
            // 用原版市价而不是我们算出来的价值：估值一旦偏离，参考库存会跟着偏，稀缺度又被放大一遍。
            float reference = ReferenceStock(def);
            // 原版没标价：不参与稀缺波动，免得 0.1 的兜底价算出"参考 20000 件"这种假数字。
            if (reference < 0f) return 1f;
            int stock = comp.StockOf(def);

            // 正数 = 积压（跌价），负数 = 缺货（涨价）
            float ratio = (stock - reference) / Mathf.Max(reference, 1f);

            // 幂函数：ratio<0 是缺货，(1+ratio)^(-k) > 1 → 涨价；ratio>0 是积压 → 跌价。
            // 线性版本（1 - 0.15*ratio）在"缺 10 倍"和"缺 1 倍"给同样的价，等于没有供求信号。
            float e = Mathf.Clamp(ratio, -0.95f, 50f);
            float price = Mathf.Pow(1f + e, -ScarcityElasticity);
            price = Mathf.Clamp(price, ElasticMin, ElasticMax);

            // 需求这一侧：本期成交量 / 上期成交量。抢的人多 → 再上浮；没人要 → 压下来。
            price *= EconomyTradeVolume.DemandFactor(def);
            price = Mathf.Clamp(price, ElasticMin, ElasticMax);

            // 恐慌性囤积：通胀压力越高，人人抢货，价格再加一层
            float panic = 1f + Mathf.Clamp(EconomyInflation.Pressure(map), 0f, 3f) * 0.05f;
            return price * panic;
        }

        /// <summary>该商品的社会化比例，考虑单件覆盖。</summary>
        public static float PublicizationFor(ThingDef def, Map map)
        {
            if (def == null) return 0f;
            EconomyItemExtension ext = def.GetModExtension<EconomyItemExtension>();
            if (ext != null && ext.publicizationOverride >= 0f)
            {
                return Mathf.Clamp(ext.publicizationOverride, 0f, 100f);
            }
            return Publicization(EconomyRegistry.CategoryOf(def), map);
        }

        /// <summary>该商品单独设置的税率百分比（商品税栏位），没设置返回 0。</summary>
        public static float TaxRate(ThingDef def, Map map)
        {
            if (def == null || map == null) return 0f;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            return comp != null ? comp.TaxRateFor(def) : 0f;
        }

        /// <summary>单个单位的实际价格。</summary>
        public static float UnitPrice(ThingDef def, Map map)
        {
            if (def == null) return 0f;
            float marketValue = def.GetStatValueAbstract(StatDefOf.MarketValue);
            if (EconomyRegistry.IsCurrency(def)) return marketValue;
            // 营养膏不参与价格曲线：固定按营养膏机单价计
            if (EconomyConsumption.IsPasteDef(def))
            {
                MapEconomyComponent pc = map != null ? map.GetComponent<MapEconomyComponent>() : null;
                return pc != null ? Mathf.Max(1f, pc.pastePrice) : marketValue;
            }
            float price = Mathf.Max(0f, EconomyValue.Of(def, map) * CombinedFactor(def, map) * SilverIndex(map) * ScarcityFactor(def, map));
            float tax = TaxRate(def, map);
            if (tax != 0f) price = Mathf.Max(0f, price * (1f + tax / 100f));
            return price;
        }

        /// <summary>单件物品的价格：在定义价基础上叠加品质。</summary>
        public static float UnitPrice(Thing thing, Map map)
        {
            if (thing == null) return 0f;
            float price = UnitPrice(thing.def, map) * QualityFactor(thing) * PreferabilityFactor(thing);

            // 荤素：原版 CompIngredients 记下了这一碗实际用了哪些原料，
            // 全肉的比全素的贵 30%。用比例而不是绝对原料价，避免把饭价推上天。
            float meat = EconomyValue.MeatShare(thing);
            if (meat > 0f) price *= 1f + 0.3f * meat;

            // 真实配料：这一碗到底用虫肉还是牛肉，用 CompIngredients 记的实际原料重算成本。
            // 不能用"比值夹在 0.5~2 倍"那种温和修正 —— 那样"一碗肉做的饭"会比生肉还便宜，
            // 明显不合逻辑。这里直接用真实原料价替换平均原料价，劳动部分保持不变。
            float avgCost = EconomyValue.MaterialCost(thing.def, map);
            float realCost = EconomyValue.RealIngredientCost(thing);
            if (realCost > 0.01f)
            {
                float value = EconomyValue.Of(thing.def, map);
                if (value > 0.01f)
                {
                    float labor = Mathf.Max(0f, value - avgCost);
                    float newValue = realCost + labor;
                    price *= newValue / value;
                }
            }

            return price;
        }

        /// <summary>食物等级倍率。餐食没有品质组件，但原版用 preferability 区分生食/简单/精致/丰盛。</summary>
        public static float PreferabilityFactor(Thing thing)
        {
            if (thing == null || thing.def == null || thing.def.ingestible == null) return 1f;
            switch (thing.def.ingestible.preferability)
            {
                case FoodPreferability.DesperateOnly: return 0.3f;
                case FoodPreferability.DesperateOnlyForHumanlikes: return 0.3f;
                case FoodPreferability.RawBad: return 0.5f;
                case FoodPreferability.RawTasty: return 0.8f;
                case FoodPreferability.MealTerrible: return 0.6f;
                case FoodPreferability.MealAwful: return 0.7f;
                case FoodPreferability.MealSimple: return 1f;
                case FoodPreferability.MealFine: return 1.5f;
                case FoodPreferability.MealLavish: return 2.2f;
                default: return 1f;
            }
        }

        /// <summary>品质倍率。没有品质组件的物品返回 1。</summary>
        public static float QualityFactor(Thing thing)
        {
            if (thing == null) return 1f;
            CompQuality q = thing.TryGetComp<CompQuality>();
            if (q == null) return 1f;
            switch (q.Quality)
            {
                case QualityCategory.Awful: return 0.6f;
                case QualityCategory.Poor: return 0.8f;
                case QualityCategory.Good: return 1.3f;
                case QualityCategory.Excellent: return 1.7f;
                case QualityCategory.Masterwork: return 2.2f;
                case QualityCategory.Legendary: return 3f;
                default: return 1f;
            }
        }

        /// <summary>把定价过程拆成一行文本，方便调试和界面展示。</summary>
        public static string Explain(ThingDef def, Map map)
        {
            if (def == null) return "-";
            EconomyCategoryDef cat = EconomyRegistry.CategoryOf(def);
            float marketValue = def.GetStatValueAbstract(StatDefOf.MarketValue);
            return def.defName
                + " 市场价=" + marketValue.ToString("0.##")
                + " 分类=" + (cat != null ? cat.defName : "未归类")
                + " 系数=" + CombinedFactor(def, map).ToString("0.000")
                + " 银价指数=" + SilverIndex(map).ToString("0.000")
                + " 稀缺系数=" + ScarcityFactor(def, map).ToString("0.000")
                + " 最终=" + UnitPrice(def, map).ToString("0.##");
        }
    }
}
