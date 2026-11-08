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
            meatUnitCost = null;
            meatUnitLabor = null;
            feedPerNutrition = -1f;
            plantScale = -1f;
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

        /// <summary>这个物品的原料成本 c（单件口径，已按产量摊薄）。</summary>
        public static float MaterialCost(ThingDef def, Map map)
        {
            // 产地统计优先：挖出来/种出来的没有原料成本，宰出来的成本就是饲养。
            // 样本不足时 Dominant 返回 false，退回下面的静态判断。
            EconomyProductionStats.Source prod;
            if (EconomyProductionStats.Dominant(def, out prod))
            {
                if (prod == EconomyProductionStats.Source.Mine) return 0f;
                if (prod == EconomyProductionStats.Source.Harvest) return 0f;
                if (prod == EconomyProductionStats.Source.Butcher) return MeatUnitCost(def);
                if (prod == EconomyProductionStats.Source.Gather) return MeatUnitCost(def) * 0.5f;
            }

            // 顺序很重要：有制造配方的一律走统一公式。
            // 特殊算法只给"没有配方"的东西兜底，绝不抢在配方前面。
            // 唯一例外是矿物：它的来源是"挖"，不是"造"。
            // mod 常给矿配一条"废渣炼钢"之类的转化配方，那只是二次加工，不该定义矿价。
            if (IsMineral(def)) return 0f;
            if (PlantLaborPerUnit(def, map) > 0f) return 0f;

            // 肉只按"肉"算，哪怕存在"把器官剁成肉"这种配方（人肉就是被它算成 120 的）。
            if (IsButcheredMeat(def)) return MeatUnitCost(def);

            RecipeDef r = MakerOf(def);
            if (r == null)
            {
                if (def != null && IsBuiltThing(def)) return BuildingMaterial(def, map);

                float meat = MeatUnitCost(def);
                if (meat > 0f) return meat;

                if (PlantLaborPerUnit(def) > 0f) return 0f;

                if (def != null && IsBodyPart(def)) return OrganicPrice(def);
                return 0f;
            }
            if (r.ingredients == null) return 0f;
            float c = 0f;
            for (int i = 0; i < r.ingredients.Count; i++)
            {
                IngredientCount ing = r.ingredients[i];
                ThingDef mat = IngredientDef(ing);
                if (mat == null) continue;
                c += Of(mat, map) * Mathf.Max(1f, ing.GetBaseCount());
            }
            // 一炉出 N 件，成本要摊到每一件上。
            // 以前这里没除，于是"一次 400 发子弹"的原料全算到一发头上，面板显示大几百倍。
            return c / Mathf.Max(1, ProductCount(r, def));
        }

        /// <summary>这个物品包含的新创造价值 v+s（劳动价值）。</summary>
        /// <summary>
        /// 这件物品的工时（tick）。
        /// RecipeDef.workAmount 有 44% 的配方是 -1（原版烹饪也在内），所以退到 WorkToMake 统计值。
        /// </summary>
        private static float WorkAmountOf(RecipeDef r, ThingDef def)
        {
            if (r != null && r.workAmount > 0f) return r.workAmount;
            if (def == null) return 0f;
            return Mathf.Max(0f, def.GetStatValueAbstract(StatDefOf.WorkToMake));
        }

        public static float LaborValue(ThingDef def, Map map)
        {
            // 产地统计优先：实际怎么来的就按哪条口径算
            EconomyProductionStats.Source pl;
            if (EconomyProductionStats.Dominant(def, out pl))
            {
                if (pl == EconomyProductionStats.Source.Mine) return Fallback(def);
                if (pl == EconomyProductionStats.Source.Butcher) return MeatUnitLabor(def);
                if (pl == EconomyProductionStats.Source.Gather) return MeatUnitLabor(def) * 0.5f;
                if (pl == EconomyProductionStats.Source.Harvest)
                {
                    float t = PlantLaborPerUnit(def);
                    if (t > 0f) return Mathf.Max(t / (float)GenDate.TicksPerDay * LaborDayValue(), Fallback(def));
                    return Fallback(def);
                }
            }

            // 矿物：原料成本为 0，全部算作挖矿劳动（见 LaborValue）
            if (def != null && IsMineral(def)) return 0f;

            // 能种出来的东西：价值全在"播种+收割"里，放在配方之前。
            // 《布》《稻米》这类原版是种出来的，mod 常又给一条织布/碾米配方，
            // 只看配方会把它们算成"造出来的"。
            float plantT = PlantValue(def, map);
            if (plantT > 0f) return plantT;

            // 肉只按"肉"算，哪怕存在"把器官剁成肉"这种配方。
            if (IsButcheredMeat(def)) return MeatUnitLabor(def);

            RecipeDef r = MakerOf(def);
            if (r == null)
            {
                if (def != null && IsBuiltThing(def)) return BuildingLabor(def);

                float meatLabor = MeatUnitLabor(def);
                if (meatLabor > 0f) return meatLabor;   // 产肉周期 = 工时

                // 农作物：播种 + 收割工时 ÷ 产量（与 Compute 完全同一口径）
                float plantLabor = PlantValue(def, map);
                if (plantLabor > 0f) return plantLabor;

                // 矿物：劳动列 = 实测挖矿工时换的钱（工时账本由工资系统提供）
                if (IsMineral(def)) return MineralValue(def, map);

                if (def != null && IsBodyPart(def)) return 0f;
                return 0f;
            }
            float work = WorkAmountOf(r, def);
            if (work <= 0f)
            {
                // 配方没填工时、产物也没有 WorkToMake 时，不能把人工算成 0（织布明明要干活）。
                // 用"原版市价 - 原料成本"作为人工的残余值：原版市价本来就包含人工。
                float residual = Fallback(def) - MaterialCost(def, map);
                return Mathf.Max(0f, residual);
            }
            float days = work / (float)GenDate.TicksPerDay;
            int count = ProductCount(r, def);
            float lv = Mathf.Max(0f, days * LaborDayValue() / Mathf.Max(1, count));
            // 与 Compute 里的上限保持一致：劳动价值不超过市价 5 倍
            return Mathf.Min(lv, Fallback(def) * 5f);
        }

        private static float Compute(ThingDef def, Map map, int depth)
        {
            if (def == null) return 0f;

            // 银本位：1 个白银就是 1 银，不参与任何成本推导。
            // 否则一旦有"熔炼白银"之类的配方，货币本身的价值就会被算歪。
            if (def == ThingDefOf.Silver) return 1f;

            // 产地统计优先：这个殖民地实际怎么得到它的，就按哪条口径定价。
            // 样本不足时 Dominant 返回 false，退回下面的静态判断。
            EconomyProductionStats.Source pv;
            if (EconomyProductionStats.Dominant(def, out pv))
            {
                if (pv == EconomyProductionStats.Source.Mine) return Fallback(def);
                if (pv == EconomyProductionStats.Source.Butcher) return MeatUnitCost(def) + MeatUnitLabor(def);
                if (pv == EconomyProductionStats.Source.Gather) return (MeatUnitCost(def) + MeatUnitLabor(def)) * 0.5f;
                if (pv == EconomyProductionStats.Source.Harvest)
                {
                    float t = PlantLaborPerUnit(def);
                    if (t > 0f) return Mathf.Max(t / (float)GenDate.TicksPerDay * LaborDayValue(), Fallback(def));
                    return Fallback(def);
                }
            }

            if (depth > 4 || computing.Contains(def)) return Fallback(def);

            // 矿物先于配方：mod 的"废渣炼钢"只是二次加工，不是钢的来源。
            if (IsMineral(def)) return MineralValue(def, map);

            // 能种出来的也先于配方：布、稻米原版都是种出来的
            float plantValue = PlantValue(def, map);
            if (plantValue > 0f) return plantValue;

            // 肉也先于配方：mod 常给"把器官剁成肉"这类二次加工配方，
            // 它不该决定一块肉值多少（否则人肉 120、鱼肉 0）。
            if (IsButcheredMeat(def))
            {
                float meatCost = MeatUnitCost(def) + MeatUnitLabor(def);
                if (meatCost > 0.01f) return meatCost;
            }

            RecipeDef r = MakerOf(def);

            if (r == null)
            {
                // 没有制造配方的东西，才轮到特殊算法兜底：
                // 建筑 → 建造材料+工时；肉 → 饲养+照料；器官/仿生体 → 固定基准价
                if (IsBuiltThing(def)) return BuildingValue(def, map);

                float meat = MeatUnitCost(def);
                if (meat > 0f) return meat + MeatUnitLabor(def);

                // 农作物/木材这类"种出来、砍下来"的东西：没有原料，成本全是劳动
                float planted = PlantValue(def, map);
                if (planted > 0f) return planted;

                if (IsBodyPart(def)) return OrganicPrice(def);

                return Fallback(def);
            }

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

            float v = 0f;
            float work = WorkAmountOf(r, def);
            if (work > 0f)
            {
                float days = work / (float)GenDate.TicksPerDay;
                v = Mathf.Max(0f, days * LaborDayValue());
            }

            // 劳动价值不能离谱地超过这件东西本身的市价。
            // 木材曾因为"生长周期 8 天"被算成 1200 银/个，然后所有用到木材的配方一起暴涨
            // （教材 500 木材 → 60 万银）。这里按市价的 5 倍封顶。
            float laborCap = Fallback(def) * 5f;
            if (v > laborCap) v = laborCap;

            int count = ProductCount(r, def);
            float result = (c + v) / Mathf.Max(1, count);

            // 保险带已按用户要求关闭（通胀完全自由）。
            // 需要时可以把它打开，但要注意：开着会削弱通胀传导。
            // float infl = Mathf.Max(1f, LaborDayValue() / 150f);
            // float fb = Mathf.Max(0.1f, Fallback(def)) * infl;
            // if (result > fb * 25f) result = fb * 25f;
            // else if (result < fb * 0.1f) result = fb * 0.1f;

            // 兜底：配方在，但原料和工时都算不出来（mod 常见：原料走 filter、工时没填）。
            // 这时不要返回 0 —— 0 会被当成"这东西一文不值"，面板显示 0.06 就是这么来的。
            if (result <= 0.01f)
            {
                if (IsBuiltThing(def)) return BuildingValue(def, map);
                return Fallback(def);
            }
            return result;
        }

        /// <summary>
        /// 原版给这件东西标的价格。0 表示"mod 根本没标"。
        /// BaseMarketValue 是 Def 加载时就算好的基础价，比临时查属性可靠；
        /// 有些 mod 只写了 statBases 没写别的，两条都查一遍。
        /// </summary>
        private static float MarketValueOf(ThingDef def)
        {
            if (def == null) return 0f;
            float v = def.BaseMarketValue;
            if (v <= 0.01f) v = def.GetStatValueAbstract(StatDefOf.MarketValue);
            return v > 0.01f ? v : 0f;
        }

        /// <summary>
        /// 原版给这件东西标的价格。0 表示"mod 根本没标"。
        /// 面板的参考库存、稀缺系数都走这里，保证三处口径一致。
        /// </summary>
        public static float VanillaPrice(ThingDef def) { return MarketValueOf(def); }

        /// <summary>
        /// mod 里的"中间定义"（比如"钢铁（生成）"）：没有配方、没有原料表、原版标价还近乎为零。
        /// 这类东西不是商品，是别人 mod 内部流转用的占位物，不该出现在市场面板里。
        /// 判断全走已有分支，不写死任何 defName，所以换 mod 也不会误伤。
        /// </summary>
        public static bool IsPlaceholder(ThingDef def)
        {
            if (def == null) return false;
            if (MakerOf(def) != null) return false;
            if (IsBuiltThing(def)) return false;
            if (IsButcheredMeat(def)) return false;
            if (PlantLaborPerUnit(def) > 0f) return false;
            if (IsMineral(def)) return false;
            if (IsBodyPart(def)) return false;
            if (def.CostList != null && def.CostList.Count > 0) return false;
            return MarketValueOf(def) <= 0.5f;
        }

        private static float Fallback(ThingDef def)
        {
            float v = MarketValueOf(def);
            return v > 0.01f ? v : 0.1f;
        }

        // ---------------- 器官 / 仿生体 ----------------
        // 它们不是"生产"出来的：器官来自战俘，仿生体是买来的成品。
        // 所以不按劳动日价值算（否则调工资会把器官价格带着一起飞），
        // 取"固定基准价"和"市价"里更高的那个。

        public const float OrganBasePrice = 1200f;

        /// <summary>
        /// 这碗饭的"荤素占比"：0 = 全素，1 = 全肉。
        /// 原料来自原版 CompIngredients —— 做饭时游戏会把实际用到的原料 def 记在餐食上，
        /// 这比"按配方猜"准，因为它记的是**这一碗**真的放了什么。
        /// 只用于定价加权，不改变能不能吃（吃不吃由 WillEat 决定）。
        /// </summary>
        /// <summary>
        /// 这**一碗**的实际原料成本：用 CompIngredients 里记的真实配料，按"每营养价格"平均，
        /// 再乘这碗的营养量。配方成本是平均值，这一碗到底用了虫肉还是牛肉，只有它自己知道。
        /// 取不到配料时返回 0（调用方退回配方成本）。
        /// </summary>
        public static float RealIngredientCost(Thing thing)
        {
            if (thing == null || thing.def == null) return 0f;
            RimWorld.CompIngredients comp = thing.TryGetComp<RimWorld.CompIngredients>();
            if (comp == null || comp.ingredients == null || comp.ingredients.Count == 0) return 0f;

            float sum = 0f;
            int n = 0;
            for (int i = 0; i < comp.ingredients.Count; i++)
            {
                ThingDef d = comp.ingredients[i];
                if (d == null || d.ingestible == null) continue;
                float nut = d.GetStatValueAbstract(StatDefOf.Nutrition);
                if (nut <= 0.01f) continue;
                sum += Fallback(d) / nut;
                n++;
            }
            if (n == 0) return 0f;

            float selfNut = thing.def.GetStatValueAbstract(StatDefOf.Nutrition);
            if (selfNut <= 0.01f) return 0f;
            return sum / n * selfNut;
        }

        public static float MeatShare(Thing thing)
        {
            try
            {
                if (thing == null || thing.def == null) return 0f;
                CompIngredients comp = thing.TryGetComp<CompIngredients>();
                if (comp == null || comp.ingredients == null || comp.ingredients.Count == 0) return 0f;

                float sum = 0f;
                int n = 0;
                for (int i = 0; i < comp.ingredients.Count; i++)
                {
                    ThingDef d = comp.ingredients[i];
                    if (d == null || d.ingestible == null) continue;
                    sum += MeatWeight(d);
                    n++;
                }
                return n > 0 ? sum / n : 0f;
            }
            catch { return 0f; }
        }
        /// <summary>
        /// 这一件的"原料签名"：把用了哪些原料排序拼成字符串。
        /// 签名相同才算同一种东西，可以合并；不同就必须分开。
        /// </summary>
        public static string IngredientKey(Thing thing)
        {
            if (thing == null) return null;
            try
            {
                CompIngredients comp = thing.TryGetComp<CompIngredients>();
                 if (comp == null || comp.ingredients == null || comp.ingredients.Count == 0) return null;
                List<string> names = new List<string>();
                for (int i = 0; i < comp.ingredients.Count; i++)
                {
                    if (comp.ingredients[i] != null) names.Add(comp.ingredients[i].defName);
                }
                if (names.Count == 0) return null;
                names.Sort(System.StringComparer.Ordinal);
                return string.Join("|", names.ToArray());
            }
            catch { return null; }
        }
        private static float MeatWeight(ThingDef d)
        {
            if (d == null || d.ingestible == null) return 0.5f;
            if ((d.ingestible.foodType & FoodTypeFlags.Meat) != 0) return 1f;
            if ((d.ingestible.foodType & (FoodTypeFlags.VegetableOrFruit | FoodTypeFlags.Plant)) != 0) return 0f;
            return 0.5f;
        }

        private static float OrganicPrice(ThingDef def)
        {
            return Mathf.Max(OrganBasePrice, Fallback(def));
        }

        // ---------------- 肉类 / 屠宰产物 ----------------
        // 肉没有"制造配方"，它的成本来自"养一头动物要花多少"。口径：
        //   每块肉的成本 = 产出这种肉的最便宜动物的市价 ÷ 它的产肉量
        // 这是"周期成本"的近似值——动物不是凭空出现的，从小养到大就是它的原料。

        private static Dictionary<ThingDef, float> meatUnitCost;
        private static Dictionary<ThingDef, float> meatUnitLabor;
        private static float feedPerNutrition = -1f;

        /// <summary>
        /// 产肉周期里"人真正花在照料上的时间"占多少。
        /// 1.0 = 一个殖民者全职守着这头动物养 18 天 → 一只鸡 2700 银，显然不对。
        /// 0.05 ≈ 每天 1.2 小时（喂食、清洁、治疗）——这才接近实际。
        /// </summary>
        public const float RearingLaborShare = 0.05f;

        /// <summary>是不是鱼（只能靠名字认，原版没有 Fish 这种 foodType）。</summary>
        public static bool IsFish(ThingDef def)
        {
            if (def == null || def.defName == null) return false;
            if (def.defName.IndexOf("Fish", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return def.label != null
                && def.label.IndexOf("鱼", System.StringComparison.Ordinal) >= 0;
        }

        /// <summary>是不是屠宰产物（原版的肉标志，不看 defName）。</summary>
        public static bool IsButcheredMeat(ThingDef def)
        {
            if (def == null || def.ingestible == null) return false;
            if ((def.ingestible.foodType & FoodTypeFlags.Meat) != 0) return true;

            // 不少 mod 的肉只写了 category = Foods，没打 Meat 标志，
            // 于是被当成普通"无配方物品"，肉价算不出来、显示 0/0。
            // 这里放宽：能吃 + 不是素食/水果/植物 + 不是药 + 不是做出来的熟食 → 当肉看。
            // 放宽不会误伤熟食（熟食有配方）、也不会误伤蔬菜（有植物标志）。
            if (def.IsDrug) return false;
            if (def.ingestible.preferability == FoodPreferability.NeverForNutrition) return false;
            if ((def.ingestible.foodType & (FoodTypeFlags.VegetableOrFruit | FoodTypeFlags.Plant)) != 0) return false;
            if (MakerOf(def) != null) return false;
            return true;
        }

        private static void BuildMeatIndex()
        {
            if (meatUnitCost != null) return;
            meatUnitCost = new Dictionary<ThingDef, float>();
            meatUnitLabor = new Dictionary<ThingDef, float>();
            try
            {
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef d = all[i];
                    try
                    {
                        if (d == null || d.race == null) continue;
                        ThingDef meat = d.race.meatDef;
                        if (meat == null) continue;

                        // 1.7 的产肉量是属性值（StatDefOf.MeatAmount），不是 RaceProperties.meatAmount
                        // 全部钳到合理区间：mod 里这几个字段经常填得离谱，
                        // 见过"一块肉 1.4 亿银"，就是养成天数或产肉量被填成了天文数字。
                        float amount = Mathf.Clamp(d.GetStatValueAbstract(StatDefOf.MeatAmount), 0.1f, 5000f);
                        if (amount <= 0f) continue;

                        // 养成天数：最后一个成长阶段开始的那个年龄（年 → 天），最长按 180 天算
                        float days = 0f;
                        List<LifeStageAge> stages = d.race.lifeStageAges;
                        if (stages != null && stages.Count > 0)
                            days = Mathf.Clamp(stages[stages.Count - 1].minAge * 60f, 0f, 180f);

                        // 每天吃掉的营养
                        float hunger = Mathf.Clamp(d.race.baseHungerRate, 0f, 20f);

                        // c：只按"实际吃掉的饲料"算。
                        // 不再和动物市价取大/取小 —— 那就是"拍脑袋"，用户明确不要。
                        // 饲料价来自作物数据（生长时间 / 收割产出 / 营养值），见 FeedPricePerNutrition()。
                        float c = hunger * days * FeedPricePerNutrition() / amount;

                        // v：产肉周期 = 工时（照料天数 × 每天照料占比）
                        float v = days * RearingLaborShare * LaborDayValue() / amount;

                        float total = c + v;

                        // 上一版这里有一道"单价高出原版市价 10 倍就直接丢弃"的闸门。
                        // 结果是把所有动物都丢了：饲养成本本来就远高于原版肉价（0.8~2.3），
                        // 索引被清空 → 每块肉都算成 0。现在只丢"算不出来"的，不丢"算得贵"的。
                        if (total <= 0f) continue;
                        float oldC;
                        float oldV;
                        if (meatUnitCost.TryGetValue(meat, out oldC)
                            && meatUnitLabor.TryGetValue(meat, out oldV)
                            && (oldC + oldV) <= total) continue;

                        meatUnitCost[meat] = c;
                        meatUnitLabor[meat] = v;
                    }
                    catch { }
                }
            }
            catch { }
            DevLog.Msg("[DSH-ECONOMY] 肉类来源索引：" + meatUnitCost.Count + " 种");
        }

        /// <summary>每块肉的"养育成本"（饲料）。查不到时退回原版市价（不是 0）。</summary>
        private static float MeatUnitCost(ThingDef def)
        {
            if (!IsButcheredMeat(def)) return 0f;
            BuildMeatIndex();
            float per;
            if (meatUnitCost != null && meatUnitCost.TryGetValue(def, out per)) return Mathf.Max(0f, per);
            // 索引里没有这种肉（没有动物产它、或者动物数据缺失）：
            // 退回原版市价，至少不会算成 0 让整套价格塌掉。
            // 鱼没有饲养环节（饲料=0，全是钓鱼工时），所以饲料只取 0 成。
            if (IsFish(def)) return 0f;
            return Mathf.Max(0.1f, Fallback(def) * 0.8f);
        }

        /// <summary>每块肉的"照料工时"价值。查不到时按市价的 20% 折算（不是 0）。</summary>
        private static float MeatUnitLabor(ThingDef def)
        {
            if (!IsButcheredMeat(def)) return 0f;
            BuildMeatIndex();
            float per;
            if (meatUnitLabor != null && meatUnitLabor.TryGetValue(def, out per) && per > 0f)
                return Mathf.Max(0f, per);
            // 索引没命中：鱼按"钓鱼工时账"算，其它动物把市价拆成 8 成饲料 + 2 成照料。
            float perCatch = EconomyProductionStats.FishingTicksPerCatch();
            if (perCatch > 0f && IsFish(def))
                return Mathf.Max(0.05f, perCatch / GenDate.TicksPerDay * LaborDayValue());
            return Mathf.Max(0.05f, Fallback(def) * 0.2f);
        }

        /// <summary>每点营养的饲料价：取最便宜的素食（市价 ÷ 营养值）。</summary>
        /// <summary>调试：把"哪种作物收出什么东西"打出来（前 40 条），查匹配失败用。</summary>
        public static void DumpPlants()
        {
            try
            {
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                string s = "";
                int n = 0;
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef d = all[i];
                    if (d == null || d.plant == null || d.plant.harvestedThingDef == null) continue;
                    s += d.defName + "→" + d.plant.harvestedThingDef.defName + "  ";
                    n++;
                    if (n >= 40) break;
                }
                Log.Message("[DSH-ECONOMY] 作物表（前 " + n + " 条）：" + (n == 0 ? "空" : s));
            }
            catch { }
        }

        // ---------------- 给调试按钮用的公开判断 ----------------
        public static bool HasRecipeSource(ThingDef def)
        {
            try { return def != null && MakerOf(def) != null; } catch { return false; }
        }

        public static bool HasPlantSource(ThingDef def)
        {
            try { return def != null && PlantLaborPerUnit(def) > 0f; } catch { return false; }
        }

        public static bool IsMeatSource(ThingDef def)
        {
            try { return def != null && MeatUnitCost(def) > 0f; } catch { return false; }
        }

        private static float FeedPricePerNutrition()
        {
            if (feedPerNutrition >= 0f) return feedPerNutrition;
            feedPerNutrition = 0.1f;
            try
            {
                // 基准是"干草"，不是"全表最便宜的素食"。
                // 最便宜的那个往往是野草/牧草，价格接近 0，会把肉价压到不合常理（40 件肉才 5 银）。
                // 原版饲养用的就是干草，拿它当饲料价最有代表性。
                if (ThingDefOf.Hay != null)
                {
                    float hnut = ThingDefOf.Hay.GetStatValueAbstract(StatDefOf.Nutrition);
                    if (hnut > 0.01f)
                    {
                        float hv = Fallback(ThingDefOf.Hay) / hnut;
                        if (hv > 0.01f)
                        {
                            feedPerNutrition = Mathf.Max(0.1f, hv);
                            return feedPerNutrition;
                        }
                    }
                }

                // 没有干草（被别的 mod 删了？）才退回扫素食，下限同样是 0.1
                float best = float.MaxValue;
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef d = all[i];
                    try
                    {
                        if (d == null || d.ingestible == null) continue;
                        if ((d.ingestible.foodType & (FoodTypeFlags.VegetableOrFruit | FoodTypeFlags.Plant)) == 0) continue;

                        float nut = d.GetStatValueAbstract(StatDefOf.Nutrition);
                        if (nut <= 0.01f) continue;

                        float per = Fallback(d) / nut;
                        if (per < best) best = per;
                    }
                    catch { }
                }
                if (best < float.MaxValue) feedPerNutrition = Mathf.Max(0.1f, best);
            }
            catch { }
            return feedPerNutrition;
        }

        // ---------------- 建筑：建造材料 + 建造工时 ----------------
        // 建筑不是"造出来的物品"，没有 RecipeDef。它的成本来自 CostList（固定的钢铁/零件等）
        // 加上"可选材料"（木头/钢铁/布料，由建造时选定），劳动来自 WorkToBuild。

        private static float BuildingMaterial(ThingDef def, Map map)
        {
            float c = 0f;

            if (def.CostList != null)
            {
                for (int i = 0; i < def.CostList.Count; i++)
                {
                    ThingDefCountClass t = def.CostList[i];
                    if (t == null || t.thingDef == null) continue;
                    c += Of(t.thingDef, map) * Mathf.Max(1, t.count);
                }
            }

            // 可选材料（MadeFromStuff）在 CostList 里通常不写数量，单独补一笔。
            // 没有具体材料信息时按钢铁的市价估，属于下界。
            if (def.MadeFromStuff && def.costStuffCount > 0)
            {
                ThingDef steel = ThingDefOf.Steel;
                float unit = steel != null
                    ? Mathf.Max(0.1f, steel.GetStatValueAbstract(StatDefOf.MarketValue))
                    : 1.9f;
                c += def.costStuffCount * unit;
            }

            return c;
        }

        private static float BuildingLabor(ThingDef def)
        {
            float work = Mathf.Max(0f, def.GetStatValueAbstract(StatDefOf.WorkToBuild));
            if (work <= 0f) return 0f;
            return work / (float)GenDate.TicksPerDay * LaborDayValue();
        }

        /// <summary>诊断用：说明 IsBodyPart 是被哪一条命中的。</summary>
        private static string BodyPartReason(ThingDef def)
        {
            if (def == null) return "空";
            if (def.thingCategories != null)
            {
                for (int i = 0; i < def.thingCategories.Count; i++)
                {
                    ThingCategoryDef tc = def.thingCategories[i];
                    if (tc == null || tc.defName == null) continue;
                    string n = tc.defName;
                    if (n.StartsWith("BodyParts") || n == "Genes" || n == "Serums") return "cat:" + n;
                }
            }
            string dn = def.defName ?? string.Empty;
            if (dn.IndexOf("Bionic", System.StringComparison.OrdinalIgnoreCase) >= 0) return "name:Bionic";
            if (dn.IndexOf("Archotech", System.StringComparison.OrdinalIgnoreCase) >= 0) return "name:Archotech";
            if (dn.IndexOf("Prosthetic", System.StringComparison.OrdinalIgnoreCase) >= 0) return "name:Prosthetic";
            if (dn.IndexOf("Organ", System.StringComparison.OrdinalIgnoreCase) >= 0) return "name:Organ";
            if (dn.IndexOf("Serum", System.StringComparison.OrdinalIgnoreCase) >= 0) return "name:Serum";
            return "无";
        }

        // ── 矿物：原版没把"采矿工时"写进 def（探针已证实 building/statBases 里都没有），
        //    所以矿物只能按市价估值。但它的价值全是"挖出来的劳动"，没有原料，
        //    因此对外表现成 c=0、v+s=市价（方案 B）。
        private static readonly HashSet<ThingDef> mineralProducts = new HashSet<ThingDef>();
        private static bool mineralBuilt;

        /// <summary>建立"哪些物品是从矿里挖出来的"索引。用反射读 mineableThing，避免猜字段名报编译错。</summary>
        private static void BuildMineralIndex()
        {
            if (mineralBuilt) return;
            mineralBuilt = true;
            mineralProducts.Clear();
            try
            {
                System.Reflection.FieldInfo f = typeof(BuildingProperties).GetField("mineableThing");
                if (f == null) return;
                List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < defs.Count; i++)
                {
                    ThingDef d = defs[i];
                    if (d == null || d.building == null || !d.building.isResourceRock) continue;
                    ThingDef prod = f.GetValue(d.building) as ThingDef;
                    if (prod != null) mineralProducts.Add(prod);
                }
            }
            catch { }
        }

        /// <summary>这个物品有没有"正经制造配方"（一次只产 1 件，不是批量回收/深加工）。</summary>
        private static bool HasPrimaryRecipe(ThingDef def)
        {
            RecipeDef r = MakerOf(def);
            if (r == null) return false;
            // 一次出好几件的配方多是"回收/批量熔炼"（例如废渣→45 钢），不能当主来源。
            return ProductCount(r, def) <= 1;
        }

        public static bool IsMineral(ThingDef def)
        {
            if (def == null) return false;
            BuildMineralIndex();
            // 只要某种矿产出它，它就是矿 —— 不看有没有配方。
            // （零部件确实能被矿产出，这是地图资源的一部分，不该被"它也能造"否掉。）
            return mineralProducts.Contains(def);
        }

        /// <summary>
        /// 农作物的"每单位劳动工时（tick）"。
        /// 找一种以 product 为收获物的植物，取 (播种工时 + 收割工时) ÷ 每株产量。
        /// 找不到返回 -1，表示"这不是作物"，走别的分支。
        /// 全程读 def 字段，mod 加的作物自动适配；没有作物也不会报错。
        /// </summary>
        /// <summary>
        /// 生长周期里"要照看"的那部分折算系数。
        /// 注意：这个值不能大 —— 生长时间长不等于劳动多，等待本身不产生价值。
        /// 现在设为 0：完全按"每轮播种+收割的工时 ÷ 每轮产量"算。
        /// </summary>
        public const float GrowRiskShare = 0f;

        /// <summary>
        /// 农业劳动的价值系数：自动标定，让"每单位劳动最省的那种作物"对齐它自己的原版市价。
        /// 这样水稻（每单位约 74 tick）与玉米（每单位约 21 tick）之间 3.6 倍的劳动差距
        /// 会如实换算成钱，而整体粮价又不会低到 0.05 银。
        /// 用 150 作标定基准（不跟工资波动），避免"发一次工资粮价跳一次"。
        /// </summary>
        private static float PlantScale()
        {
            if (plantScale >= 0f) return plantScale;
            plantScale = 1f;
            try
            {
                float bestComputed = float.MaxValue;
                float bestVanilla = 0f;
                List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < defs.Count; i++)
                {
                    ThingDef d = defs[i];
                    if (d == null || d.plant == null || d.plant.harvestedThingDef == null) continue;
                    ThingDef product = d.plant.harvestedThingDef;
                    float unit = PlantLaborPerUnit(product);
                    if (unit <= 0f) continue;
                    float computed = unit / (float)GenDate.TicksPerDay * 150f;
                    if (computed <= 0.001f || computed >= bestComputed) continue;
                    float vanilla = MarketValueOf(product);
                    if (vanilla <= 0.01f) continue;
                    bestComputed = computed;
                    bestVanilla = vanilla;
                }
                if (bestComputed < float.MaxValue && bestVanilla > 0f)
                    plantScale = Mathf.Clamp(bestVanilla / bestComputed, 1f, 100f);
            }
            catch { }
            if (plantScale <= 0f) plantScale = 1f;
            return plantScale;
        }

        /// <summary>作物/木材这类"种出来、砍下来"的东西的价值：每单位劳动价值 × 自动标定系数。</summary>
        private static float PlantValue(ThingDef product, Map map)
        {
            float unit = PlantLaborPerUnit(product, map);
            if (unit <= 0f) return 0f;
            return unit / (float)GenDate.TicksPerDay * LaborDayValue() * PlantScale();
        }

        private static float plantScale = -1f;

        // ---- 实时作物来源：按地图上"实际种着/长着"的那种植物取工时 ----
        // 静态扫描会挑"每单位最省"的，结果原木挑到了 BurnedTree（烧焦的树），
        // 那种东西玩家根本不会种。改成优先看地图上真实存在的植物。
        private static readonly Dictionary<ThingDef, ThingDef> plantPickCache = new Dictionary<ThingDef, ThingDef>();
        private static Map plantPickMap;
        private static int plantPickTick = -99999;

        private static ThingDef BestPlantOnMap(ThingDef product, Map map)
        {
            if (product == null || map == null) return null;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (map != plantPickMap || now - plantPickTick > 250)
            {
                plantPickCache.Clear();
                plantPickMap = map;
                plantPickTick = now;
                Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
                foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant))
                {
                    ThingDef d = t != null ? t.def : null;
                    if (d == null || d.plant == null || d.plant.harvestedThingDef == null) continue;
                    if (IsRemnantPlant(d)) continue;
                    int c;
                    counts.TryGetValue(d, out c);
                    counts[d] = c + 1;
                }
                Dictionary<ThingDef, int> best = new Dictionary<ThingDef, int>();
                foreach (KeyValuePair<ThingDef, int> kv in counts)
                {
                    ThingDef prod = kv.Key.plant.harvestedThingDef;
                    int cur;
                    if (!best.TryGetValue(prod, out cur) || kv.Value > cur)
                        best[prod] = kv.Value;
                }
                foreach (KeyValuePair<ThingDef, int> kv in best)
                {
                    // 再按数量挑一次：只保留"数量最多"的那种作为该产物的来源
                    ThingDef winner = null;
                    int winnerCount = -1;
                    foreach (KeyValuePair<ThingDef, int> c2 in counts)
                    {
                        if (c2.Key.plant == null || c2.Key.plant.harvestedThingDef != kv.Key) continue;
                        if (c2.Value > winnerCount) { winnerCount = c2.Value; winner = c2.Key; }
                    }
                    if (winner != null) plantPickCache[kv.Key] = winner;
                }
            }
            ThingDef pick;
            return plantPickCache.TryGetValue(product, out pick) ? pick : null;
        }

        private static float UnitTicksOfPlant(ThingDef plantDef)
        {
            if (plantDef == null || plantDef.plant == null) return -1f;
            float yield = Mathf.Max(1f, plantDef.plant.harvestYield);
            float work = Mathf.Max(0f, plantDef.plant.sowWork) + Mathf.Max(0f, plantDef.plant.harvestWork);
            if (work <= 0f) return -1f;
            return work / yield;
        }

        /// <summary>
        /// 优先用地图上真实的植物（实时），没有才退回"静态最省"。
        /// </summary>
        private static float PlantLaborPerUnit(ThingDef product, Map map)
        {
            ThingDef onMap = BestPlantOnMap(product, map);
            if (onMap != null)
            {
                float t = UnitTicksOfPlant(onMap);
                if (t > 0f) return t;
            }
            return PlantLaborPerUnit(product);
        }

        private static bool IsRemnantPlant(ThingDef d)
        {
            if (d == null) return true;
            string n = d.defName;
            if (string.IsNullOrEmpty(n)) return false;
            // 残骸类不是"来源"：烧焦的树、被砍倒的树、树桩、被砸烂的残株、枯死的植物。
            // 它们能产出木材/干草，但玩家不会为了产出而"种"它们，拿来当成本依据会失真。
            return n.IndexOf("Burned", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Chopped", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Stump", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Smashed", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Dessicated", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("Dead", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static float PlantLaborPerUnit(ThingDef product)
        {
            if (product == null) return -1f;
            try
            {
                List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
                float best = -1f;
                for (int i = 0; i < defs.Count; i++)
                {
                    ThingDef d = defs[i];
                    if (d == null || d.plant == null) continue;
                    if (d.plant.harvestedThingDef != product) continue;
                    if (IsRemnantPlant(d)) continue;   // 烧焦/砍倒/树桩这类残骸不当来源
                    float yield = Mathf.Max(1f, d.plant.harvestYield);
                    float work = Mathf.Max(0f, d.plant.sowWork) + Mathf.Max(0f, d.plant.harvestWork);
                    if (work <= 0f) continue;
                    // 生长期不是白等：越长越要照看（除草/防病/防动物），折算成一部分劳动。
                    // 但等待本身不消耗资源，所以只按 GrowRiskShare 折价，不整段计入。
                    float growDays = d.plant.growDays > 0f ? d.plant.growDays : 0f;
                    float riskTicks = growDays * GenDate.TicksPerDay * GrowRiskShare;
                    float unit = (work + riskTicks) / yield;
                    // 同一产物可能有多种作物（水稻/水培/魔菇），取"每单位劳动最省"的那种作为来源。
                    if (best < 0f || unit < best) best = unit;
                }
                return best;
            }
            catch { }
            return -1f;
        }

        /// <summary>
        /// 严格判定"这是不是器官/仿生体"。
        /// 不能用 EconomyConsumption.IsImplantLike —— 它走的是交易分类注册表，
        /// 那个分类范围很宽，结果把木材（原木/桃木/阴沉木）也判成了器官，
        /// 于是它们全按"器官基准价 1200"计价（就是面板上 1200/690 的来源）。
        /// 这里只看原版字段和部位分类，宁可漏判也不误判。
        /// </summary>
        private static bool IsBodyPart(ThingDef def)
        {
            if (def == null) return false;
            // 注意：不能用 def.isTechHediff —— 实测 WoodLog 的 isTechHediff 也是 true，
            // 用了它木材会被当成器官按 1200 计价。只认部位分类和名字关键词。

            if (def.thingCategories != null)
            {
                for (int i = 0; i < def.thingCategories.Count; i++)
                {
                    ThingCategoryDef tc = def.thingCategories[i];
                    if (tc == null || tc.defName == null) continue;
                    string n = tc.defName;
                    if (n.StartsWith("BodyParts") || n == "Genes" || n == "Serums") return true;
                }
            }

            string dn = def.defName ?? string.Empty;
            return dn.IndexOf("Bionic", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Archotech", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Prosthetic", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Organ", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Serum", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 该不该按"建造物"算。
        /// 除了建筑类，很多 mod 的机枪/炮塔是 ThingCategory.Item 但写了 CostList ——
        /// 它们既没有制造配方也没有市价，于是被算成 0.1 银（面板显示 0.06）。
        /// </summary>
        /// <summary>
        /// 是不是炮塔。不写死字段名：1.7 里可能是 turretGunDef 字段，也可能是 IsTurret 属性，
        /// 两条都探一遍，探不到就返回 false（退回"按普通物品算"，不会崩）。
        /// </summary>
        private static bool IsTurretLike(ThingDef def)
        {
            if (def == null || def.building == null) return false;
            try
            {
                System.Reflection.FieldInfo f = typeof(BuildingProperties).GetField("turretGunDef");
                if (f != null && f.GetValue(def.building) != null) return true;
                System.Reflection.PropertyInfo p = typeof(BuildingProperties).GetProperty("IsTurret");
                if (p != null && p.GetValue(def.building) is bool b && b) return true;
            }
            catch { }
            return false;
        }

        private static bool IsBuiltThing(ThingDef def)
        {
            if (def == null) return false;
            // 注意：这里必须写真判断，不能调自己（批量替换曾把这里改成自调用 → 无限递归）
            if (def.category == ThingCategory.Building) return true;
            // 炮塔：ThingCategory.Item，但带 building 数据，材料写在 CostList 里。
            // 1.7 的 BuildingProperties 没有 isTurret 这个字段，用反射探 turretGunDef / IsTurret。
            if (IsTurretLike(def)) return true;
            if (def.CostList == null || def.CostList.Count == 0) return false;
            // 有制造配方的（药片、子弹、零部件）不是"建造物"：CostList 只是原料表，
            // 一律交给配方分支算，避免判断窗口里把它们显示成"建造=True"。
            return MakerOf(def) == null;
        }

        private static float BuildingValue(ThingDef def, Map map)
        {
            // 保底不低于市价，避免某些 mod 建筑 CostList 为空时算成 0
            return Mathf.Max(Fallback(def), BuildingMaterial(def, map) + BuildingLabor(def));
        }

        private static float LaborDayValue()
        {
            Map map = Find.CurrentMap;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp != null && comp.wages != null) return Mathf.Max(1f, comp.wages.laborDayValue);
            return 150f;
        }

        /// <summary>
        /// 一次制造真实产出多少件。
        /// 规则：能精确匹配到这件物品的产物就取它的数量；
        /// 匹配不到（mod 的产物 def 与显示 def 不是同一个）就看配方到底产出了什么；
        /// 实在判断不了才退回 1。
        /// </summary>
        private static int ProductCount(RecipeDef r, ThingDef def)
        {
            if (r == null || r.products == null || r.products.Count == 0) return 1;

            int total = 0;
            for (int i = 0; i < r.products.Count; i++)
            {
                ThingDefCountClass p = r.products[i];
                if (p == null) continue;
                total += Mathf.Max(1, p.count);
                if (def != null && p.thingDef == def) return Mathf.Max(1, p.count);
            }

            // 只有一个产物：产物 def 对不上号，但它就是这个配方造出来的东西
            // （mod 常见写法：产物 = 弹药包 / 中间物，面板显示的是最终物品）
            if (r.products.Count == 1) return Mathf.Max(1, r.products[0].count);

            // 多产物：按总产出摊薄，至少不会把整批成本压到一件上
            return Mathf.Max(1, total);
        }

        private static ThingDef IngredientDef(IngredientCount ing)
        {
            if (ing == null) return null;
            if (ing.IsFixedIngredient) return ing.FixedIngredient;
            if (ing.filter == null) return null;

            // 这里以前用 AnyAllowedDef —— 它返回"过滤器里任意一件"，可能是天价物品，
            // 于是整条产线的原料成本爆炸（教材曾算出 60 万银一件）。
            // 改成"允许项里市价最低的那件"：偏保守，但不会被随便一个高端件带飞。
            ThingDef best = null;
            float bestValue = float.MaxValue;
            foreach (ThingDef d in ing.filter.AllowedThingDefs)
            {
                if (d == null) continue;
                float v = Fallback(d);
                if (v < bestValue) { bestValue = v; best = d; }
            }
            return best;
        }

        /// <summary>
        /// 这件东西的原料成本是真算出来的，还是"查不到、退回市价"。
        /// 面板用它区分 0 和"—"：0 是算出来就是零，— 是根本没法算。
        /// </summary>
        public static bool HasKnownCost(ThingDef def)
        {
            if (def == null) return false;
            if (MakerOf(def) != null) return true;                    // 有制造配方
            if (IsBuiltThing(def)) return true;   // 建造材料
            if (IsButcheredMeat(def)) return MeatUnitCost(def) > 0f;    // 找得到来源动物
            if (PlantLaborPerUnit(def) > 0f) return true;               // 农作物/木材：有采收工时
            if (IsMineral(def)) return true;                            // 矿物：原料就是 0
            if (IsBodyPart(def)) return true;     // 固定基准价
            return false;
        }

        /// <summary>劳动时间是否已知（同上，用于面板显示）。</summary>
        public static bool HasKnownLabor(ThingDef def)
        {
            if (def == null) return false;
            RecipeDef r = MakerOf(def);
            if (r != null) return WorkAmountOf(r, def) > 0f;
            if (IsBuiltThing(def))
                return def.GetStatValueAbstract(StatDefOf.WorkToBuild) > 0f;
            if (IsButcheredMeat(def)) return MeatUnitLabor(def) > 0f;
            if (PlantLaborPerUnit(def) > 0f) return true;
            if (IsMineral(def)) return true;
            return false;
        }

        /// <summary>
        /// 临时探针：把"看起来该算成肉/作物、结果却算不出来"的食物逐条打印。
        /// 只列可疑项，避免刷屏。诊断完就删。
        /// </summary>
        public static void DumpFoodBranch()
        {
            try
            {
                Map map = Find.CurrentMap;
                if (map == null) map = Find.AnyPlayerHomeMap;
                Log.Message("[FOOD] 开始诊断，当前地图=" + (map == null ? "无" : map.uniqueID.ToString()));
                if (map == null) return;

                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                int shown = 0;
                int scanned = 0;
                for (int i = 0; i < all.Count && shown < 30; i++)
                {
                    ThingDef d = all[i];
                    if (d == null || d.ingestible == null || d.defName == null) continue;
                    scanned++;
                    try
                    {
                        bool meatFlag = (d.ingestible.foodType & FoodTypeFlags.Meat) != 0;
                        bool isMeat = IsButcheredMeat(d);
                        float meatC = MeatUnitCost(d);
                        float meatV = MeatUnitLabor(d);
                        float plant = PlantLaborPerUnit(d, map);
                        RecipeDef r = MakerOf(d);
                        float c = MaterialCost(d, map);
                        float v = LaborValue(d, map);

                        // 可疑：有肉标志却没算出肉价；或者有作物工时、结果却全 0
                        bool suspect = (meatFlag && meatC + meatV <= 0f)
                            || (plant > 0f && c <= 0f && v <= 0f);
                        if (!suspect) continue;

                        Log.Message("[FOOD] " + d.defName
                            + " 类型=" + d.ingestible.foodType
                            + " 有配方=" + (r == null ? "无" : r.defName)
                            + " 判肉=" + isMeat
                            + " 肉c=" + meatC.ToString("0.##") + " 肉v=" + meatV.ToString("0.##")
                            + " 作物=" + plant.ToString("0.#")
                            + " → c=" + c.ToString("0.##") + " v=" + v.ToString("0.##"));
                        shown++;
                    }
                    catch (System.Exception e)
                    {
                        Log.Warning("[FOOD] " + d.defName + " 抛异常：" + e.GetType().Name + " " + e.Message);
                    }
                }
                Log.Message("[FOOD] 结束：扫描 " + scanned + " 种可吃物品，输出 " + shown + " 条可疑项");
            }
            catch (System.Exception e)
            {
                Log.Warning("[FOOD] 诊断本身失败：" + e.Message);
            }
        }

        /// <summary>给"判断"窗用：这件东西被哪条规则定价，以及过程中每个判断的真假。</summary>
        public static string Explain(ThingDef def, Map map)
        {
            if (def == null) return "";
            try
            {
                RecipeDef r = MakerOf(def);
                float plant = PlantLaborPerUnit(def, map);
                float meat = MeatUnitCost(def);
                float mv = Fallback(def);

                EconomyProductionStats.Source src;
                string hit;
                if (def == ThingDefOf.Silver) hit = "银本位 → 固定 1 银";
                else if (EconomyProductionStats.Dominant(def, out src)) hit = "实时统计 → " + src + "（已经采到 ≥3 次样本）";
                else if (IsMineral(def)) hit = "静态：矿物 → 按市价，原料记 0";
                else if (plant > 0f) hit = "静态：作物 → 播种+收割+生长系数";
                // 肉必须排在配方前面：肉价来自"养出来的成本"，
                // mod 里"把器官剁成肉"那类配方只是二次加工，不该决定肉价。
                else if (meat > 0f) hit = "静态：肉 → 饲料 + 照料工时（忽略配方）";
                else if (r != null) hit = "静态：配方 → (材料+工时) ÷ 产量";
                else if (IsBuiltThing(def)) hit = "静态：建造物 → CostList + WorkToBuild";
                else if (IsBodyPart(def)) hit = "静态：器官 → 基准价 1200 与市价取大";
                else hit = "兜底 → 市价";

                return def.LabelCap + "（" + def.defName + "）\n"
                    + "配方=" + (r == null ? "无" : r.defName)
                    + "   矿=" + IsMineral(def)
                    + "   建造=" + IsBuiltThing(def)
                    + "   器官=" + IsBodyPart(def) + "\n"
                    + "作物工时=" + plant.ToString("0.#")
                    + "   肉成本=" + meat.ToString("0.#")
                    + "   市价=" + mv.ToString("0.#") + "\n"
                    + (plant > 0f ? "作物明细：" + PlantDetail(def, map) + "\n" : "")
                    + (meat > 0f ? "肉明细：每件 饲料 " + MeatUnitCost(def).ToString("0.#")
                        + " + 照料 " + MeatUnitLabor(def).ToString("0.#")
                        + "（来源动物见肉表，取单价最低的那种）\n" : "")
                    + (IsMineral(def) ? "矿物明细：累计挖矿 " + (EconomyProductionStats.miningTicks / (float)GenDate.TicksPerDay).ToString("0.00")
                        + " 人日 / 已登记 " + EconomyProductionStats.TotalOf(EconomyProductionStats.Source.Mine)
                        + " 件 → 每件 " + MiningDaysPerUnit(map).ToString("0.000") + " 人日\n" : "")
                    + "判定：" + hit + "\n"
                    + "c=" + MaterialCost(def, map).ToString("0.#")
                    + "   v+s=" + LaborValue(def, map).ToString("0.#")
                    + "   价值=" + Of(def, map).ToString("0.#")
                    + "   现价=" + EconomyPrice.UnitPrice(def, map).ToString("0.##");
            }
            catch { return def.LabelCap; }
        }

        /// <summary>
        /// 把"这株作物到底怎么算的"原样摊开给玩家看：
        /// 播种工时、收割工时、每株产量、生长天数、每单位劳动。
        /// 所有数字都从 def 现读，没有写死任何一轮数——换 mod 也不变。
        /// </summary>
        public static string PlantDetail(ThingDef product, Map map)
        {
            if (product == null) return "无";
            try
            {
                ThingDef onMap = BestPlantOnMap(product, map);
                if (onMap != null)
                {
                    float t = UnitTicksOfPlant(onMap);
                    if (t > 0f) return DescribePlant(onMap, "地图上实际在种");
                }
                List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < defs.Count; i++)
                {
                    ThingDef d = defs[i];
                    if (d == null || d.plant == null || d.plant.harvestedThingDef != product) continue;
                    if (IsRemnantPlant(d)) continue;
                    if (UnitTicksOfPlant(d) <= 0f) continue;
                    return DescribePlant(d, "地图上没有这种作物，用静态表");
                }
            }
            catch { }
            return "无";
        }

        private static string DescribePlant(ThingDef d, string source)
        {
            float yield = Mathf.Max(1f, d.plant.harvestYield);
            float work = Mathf.Max(0f, d.plant.sowWork) + Mathf.Max(0f, d.plant.harvestWork);
            return d.defName
                + " 播种" + d.plant.sowWork.ToString("0")
                + " 收割" + d.plant.harvestWork.ToString("0")
                + " 产量" + yield.ToString("0")
                + " 生长" + d.plant.growDays.ToString("0.#") + "天"
                + " → 每单位 " + (work / yield).ToString("0.#") + " tick"
                + "（" + source + "）";
        }

        /// <summary>矿物价格的最低占比：实测工时算出来太低时，不会跌破原版市价的这个比例。</summary>
        public const float MineralFloorShare = 0.3f;

        /// <summary>
        /// 挖一块矿平均要多少人日：直接复用工资系统的工时账本（Mining 那一栏）
        /// 除以产地统计里实际挖出来的件数。没有数据就返回 0，退回市价。
        /// 这是"从已经记过的劳动里读"，不是另外发明一套计时。
        /// </summary>
        public static float MiningDaysPerUnit(Map map)
        {
            if (map == null) return 0f;
            try
            {
                float ticks = EconomyProductionStats.miningTicks;
                int mined = EconomyProductionStats.TotalOf(EconomyProductionStats.Source.Mine);
                if (ticks <= 0f || mined <= 0) return 0f;
                return ticks / mined / (float)GenDate.TicksPerDay;
            }
            catch { return 0f; }
        }

        /// <summary>矿物的价值：实测挖矿工时换钱，低于原版市价一定比例时用市价兜住。</summary>
        private static float MineralValue(ThingDef def, Map map)
        {
            float days = MiningDaysPerUnit(map);
            if (days <= 0f) return Fallback(def);
            float measured = days * LaborDayValue();
            return Mathf.Max(measured, Fallback(def) * MineralFloorShare);
        }

        /// <summary>
        /// 一件东西大致要花多少人日（用于"总社会劳动生产率"这种宏观提示）。
        /// 只算能查到的部分：配方工时 / 作物采收工时 / 屠宰照料；查不到返回 0。
        /// </summary>
        public static float LaborDaysPerUnit(ThingDef def)
        {
            if (def == null) return 0f;
            try
            {
                float ticks = PlantLaborPerUnit(def);
                if (ticks > 0f) return ticks / (float)GenDate.TicksPerDay;
                if (IsMineral(def)) return 0.05f;   // 挖矿没有可读工时，按一个经验值计
                RecipeDef r = MakerOf(def);
                if (r != null)
                {
                    float w = WorkAmountOf(r, def);
                    if (w > 0f) return w / (float)GenDate.TicksPerDay / Mathf.Max(1, ProductCount(r, def));
                }
                if (IsBuiltThing(def)) return 0.5f;
                return 0f;
            }
            catch { return 0f; }
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
            int skipNull = 0, skipWork = 0, skipMulti = 0, skipCat = 0;
            int workZero = 0, workNeg = 0;
            string workSample = "";
            string wtmSample = "";
            for (int i = 0; i < all.Count; i++)
            {
                RecipeDef r = all[i];
                if (r == null || r.products == null) { skipNull++; continue; }
                // 注意：这里不再因为 workAmount<=0 就整条丢掉。
                // 大量 mod 配方没填 workAmount，但它们照样写了原料 —— 原料成本 c 不需要工时就能算。
                // 以前在这里跳过，导致这些物品 c 和 v+s 双双是 0，只能回退市价。
                if (r.workAmount <= 0f)
                {
                    skipWork++;
                    if (r.workAmount < 0f) workNeg++; else workZero++;
                    if (workSample.Length < 200)
                    {
                        workSample += r.defName + "(" + r.workAmount.ToString("0.#") + ") ";
                    }
                }
                // 优先选"有工作台、产物单一"的配方，避免把拆解/屠宰当成制造
                for (int j = 0; j < r.products.Count; j++)
                {
                    ThingDef p = r.products[j].thingDef;
                    if (p == null) continue;
                    if (r.products.Count != 1) { skipMulti++; continue; }
                    if (p.IsCorpse) { skipCat++; continue; }
                    if (p.category != ThingCategory.Item) { skipCat++; continue; }
                    // 对照采样：workAmount 为负时，原版自带的 WorkToMake 统计值有没有数
                    if (r.workAmount <= 0f && wtmSample.Length < 260)
                    {
                        wtmSample += p.defName + "=" + p.GetStatValueAbstract(StatDefOf.WorkToMake).ToString("0.#") + " ";
                    }
                    RecipeDef old;
                    if (!makers.TryGetValue(p.defName, out old) || r.workAmount > old.workAmount)
                    {
                        makers[p.defName] = r;
                    }
                }
            }

            // 诊断：配方索引收了多少条、卡在哪一道过滤、无工时的是 0 还是负数。玩家环境不输出。
            DevLog.Msg("[DSH-ECONOMY] IDX recipes=" + all.Count + " registered=" + makers.Count
                + " skipNull=" + skipNull + " skipWork=" + skipWork
                + " skipMulti=" + skipMulti + " skipCat=" + skipCat
                + " workZero=" + workZero + " workNeg=" + workNeg);
            DevLog.Msg("[DSH-ECONOMY] IDX-NOWORK " + workSample);
            DevLog.Msg("[DSH-ECONOMY] IDX-WTM " + wtmSample);
        }

        /// <summary>
        /// 一次性打印"采集类"物品的真实字段。
        /// 目的：不猜字段名——用反射把这个 def 上所有数值字段都打出来，
        /// 这样能自动适配任意 mod 的植物/矿石，也不怕 1.7 改了字段名。
        /// </summary>
        public static void GatherTrace()
        {
            try
            {
                List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
                int rockShown = 0;
                int plantShown = 0;
                for (int i = 0; i < defs.Count; i++)
                {
                    ThingDef d = defs[i];
                    if (d == null) continue;

                    bool basic = d.defName == "WoodLog" || d.defName == "Steel";
                    bool rock = d.building != null && d.building.isResourceRock;
                    bool plant = d.plant != null;

                    if (!basic && !rock && !plant) continue;
                    // 矿石和作物各有各的额度 —— 以前共用 rockShown，结果前 3 个矿石把额度吃光，
                    // 后面所有作物的 plant 一次都没打印过（日志里全是"plant = 无"）。
                    if (!basic && rock && rockShown >= 3) continue;
                    if (!basic && !rock && plant && plantShown >= 4) continue;
                    if (!basic && rock) rockShown++;
                    if (!basic && !rock && plant) plantShown++;

                    DevLog.Msg("[GATHER-TRACE] " + d.defName
                        + " 市价=" + d.GetStatValueAbstract(StatDefOf.MarketValue).ToString("0.##")
                        + " WorkToMake=" + d.GetStatValueAbstract(StatDefOf.WorkToMake).ToString("0.##")
                        + " WorkToBuild=" + d.GetStatValueAbstract(StatDefOf.WorkToBuild).ToString("0.##"));
                    DevLog.Msg("[GATHER-TRACE]   plant = " + DumpNumbers(d.plant));
                    DevLog.Msg("[GATHER-TRACE]   building = " + DumpNumbers(d.building));
                    if (rock)
                    {
                        System.Reflection.FieldInfo mf = typeof(BuildingProperties).GetField("mineableThing");
                        ThingDef prod = mf != null ? mf.GetValue(d.building) as ThingDef : null;
                        DevLog.Msg("[GATHER-TRACE]   产出 = " + (prod == null ? "字段没读到" : prod.defName));
                    }
                    DevLog.Msg("[GATHER-TRACE]   stats = " + DumpStats(d));
                }
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] GATHER-TRACE 失败：" + e.Message);
            }
        }

        /// <summary>把 def.statBases 里所有属性打成一行（属性名=值），用于找采矿/砍伐工时。</summary>
        private static string DumpStats(ThingDef d)
        {
            if (d == null || d.statBases == null) return "无";
            string s = "";
            for (int i = 0; i < d.statBases.Count; i++)
            {
                StatModifier m = d.statBases[i];
                if (m == null || m.stat == null) continue;
                s += m.stat.defName + "=" + m.value.ToString("0.##") + " ";
            }
            return s.Length == 0 ? "（无）" : s;
        }

        /// <summary>把一个对象上所有"数值/布尔/字符串"公共字段打成一行，用于探查真实字段名。</summary>
        private static string DumpNumbers(object obj)
        {
            if (obj == null) return "无";
            string s = "";
            System.Reflection.FieldInfo[] fs = obj.GetType().GetFields(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            for (int i = 0; i < fs.Length; i++)
            {
                try
                {
                    object v = fs[i].GetValue(obj);
                    if (v is float || v is int || v is bool || v is string)
                    {
                        s += fs[i].Name + "=" + v + " ";
                    }
                }
                catch { }
            }
            return s.Length == 0 ? "（无字段）" : s;
        }
    }
}
