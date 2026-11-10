using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    public enum TaxMode
    {
        Regressive, // 累退税：收入越高税率越低
        Flat,       // 比例税：所有人同税率
        Progressive // 累进税：收入越高税率越高
    }

    public enum WealthTier
    {
        Low,
        Mid,
        High
    }

    /// <summary>
    /// 所得税政策。税率 = 基准税率 × 分档权重，基准为 0 时完全不收税。
    ///
    /// TODO（等文化/意识形态系统接进来）：公有制文化下应禁止选择"比例税及以下"，
    /// 只能累进。入口预留在 AllowsFlatOrLower。
    /// </summary>
    public class TaxPolicy : IExposable
    {
        public TaxMode mode = TaxMode.Flat;
        public float baseRate = 10f;     // 0-60 %
        public float lowWeight = 1f;
        public float midWeight = 1f;
        public float highWeight = 1f;

        // 人头税：与收入无关，每个殖民者每期固定缴纳
        public bool pollTaxEnabled;
        public float pollTaxAmount = 5f;

        /// <summary>目前所有模式都允许；等文化系统接入后按意识形态限制。</summary>
        public bool AllowsFlatOrLower
        {
            get { return true; }
        }

        public void SetModeDefaults()
        {
            switch (mode)
            {
                case TaxMode.Regressive:
                    lowWeight = 1.6f;
                    midWeight = 1f;
                    highWeight = 0.5f;
                    break;
                case TaxMode.Progressive:
                    lowWeight = 0.5f;
                    midWeight = 1f;
                    highWeight = 1.8f;
                    break;
                default:
                    lowWeight = 1f;
                    midWeight = 1f;
                    highWeight = 1f;
                    break;
            }
        }

        public float WeightFor(WealthTier tier)
        {
            switch (tier)
            {
                case WealthTier.Low: return lowWeight;
                case WealthTier.High: return highWeight;
                default: return midWeight;
            }
        }

        public void SetWeight(WealthTier tier, float value)
        {
            value = UnityEngine.Mathf.Clamp(value, 0f, 10f);
            switch (tier)
            {
                case WealthTier.Low: lowWeight = value; break;
                case WealthTier.High: highWeight = value; break;
                default: midWeight = value; break;
            }
        }

        /// <summary>某一档的实际税率百分比。</summary>
        public float RateFor(WealthTier tier)
        {
            if (baseRate <= 0f) return 0f;
            return UnityEngine.Mathf.Clamp(baseRate * WeightFor(tier), 0f, 100f);
        }

        public bool IsZero
        {
            get { return baseRate <= 0f; }
        }

        public string ModeLabel
        {
            get
            {
                switch (mode)
                {
                    case TaxMode.Regressive: return "累退税";
                    case TaxMode.Progressive: return "累进税";
                    default: return "比例税";
                }
            }
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref mode, "mode", TaxMode.Flat);
            Scribe_Values.Look(ref baseRate, "baseRate", 10f);
            Scribe_Values.Look(ref lowWeight, "lowWeight", 1f);
            Scribe_Values.Look(ref midWeight, "midWeight", 1f);
            Scribe_Values.Look(ref highWeight, "highWeight", 1f);
            Scribe_Values.Look(ref pollTaxEnabled, "pollTaxEnabled", false);
            Scribe_Values.Look(ref pollTaxAmount, "pollTaxAmount", 5f);
        }
    }

    /// <summary>按个人随身财富把殖民地人口三等分，用于计算分档税负。</summary>
    public struct WealthDistribution
    {
        public int low;
        public int mid;
        public int high;
        public float thresholdLow;
        public float thresholdHigh;
        public int total;
        public float averageRate;
    }

    public static class TaxPopulation
    {
        /// <summary>
        /// 按"本期收入"排名三等分，用于所得税分档。
        /// 累进/累退说的都是"收入越高税率越高/越低"，所以分档依据必须是收入而不是存量财富。
        /// </summary>
        public static System.Collections.Generic.Dictionary<Pawn, WealthTier> RankTiers(Map map)
        {
            var result = new System.Collections.Generic.Dictionary<Pawn, WealthTier>();
            if (map == null || map.mapPawns == null) return result;

            List<Pawn> pawns = map.mapPawns.FreeColonists;
            if (pawns == null || pawns.Count == 0) return result;

            var sorted = new List<Pawn>(pawns);
            sorted.SortByDescending(p => IncomeOf(p));

            // 按"同收入同档"分组：把收入相同的人捆在一起，
            // 再按整组的平均名次落到 高/中/低，避免同收入不同税率。
            int n = sorted.Count;
            int start = 0;
            while (start < n)
            {
                float value = IncomeOf(sorted[start]);
                int end = start;
                while (end < n && UnityEngine.Mathf.Approximately(IncomeOf(sorted[end]), value)) end++;

                float center = (start + end) / 2f / n;
                WealthTier tier = center < 1f / 3f ? WealthTier.High
                                : (center < 2f / 3f ? WealthTier.Mid : WealthTier.Low);

                for (int i = start; i < end; i++) result[sorted[i]] = tier;
                start = end;
            }
            return result;
        }

        private static float IncomeOf(Pawn p)
        {
            float inc = EconomyLedger.GetIncome(p);
            return inc > 0f ? inc : EconomyLedger.GetLastIncome(p);
        }

        /// <summary>按私有财富排名三等分，只用于界面展示财富分布。</summary>
        public static System.Collections.Generic.Dictionary<Pawn, WealthTier> RankTiersByWealth(Map map)
        {
            var result = new System.Collections.Generic.Dictionary<Pawn, WealthTier>();
            if (map == null || map.mapPawns == null) return result;

            List<Pawn> pawns = map.mapPawns.FreeColonists;
            if (pawns == null || pawns.Count == 0) return result;

            var sorted = new List<Pawn>(pawns);
            sorted.SortByDescending(p => PawnPrivateWealth(p));

            int n = sorted.Count;
            for (int i = 0; i < n; i++)
            {
                float rank = (float)i / n;
                WealthTier tier = rank < 1f / 3f ? WealthTier.High
                                : (rank < 2f / 3f ? WealthTier.Mid : WealthTier.Low);
                result[sorted[i]] = tier;
            }
            return result;
        }

        /// <summary>殖民地层面的财富与收入统计，用于经济界面展示差距。</summary>
        public struct ColonyStats
        {
            public int count;
            public float wealthTotal;      // 私有财富合计
            public float wealthAvg;
            public float wealthMax;
            public float wealthMin;
            public float publicTotal;      // 公共物品合计
            public float privateSilverTotal;
            public float incomeTotal;
            public float incomeMax;
            public float incomeMin;
            public float incomeGini;
            public float wealthGini;
            public int tierLow;
            public int tierMid;
            public int tierHigh;
        }

        public static ColonyStats Compute(Map map)
        {
            ColonyStats s = new ColonyStats();
            if (map == null || map.mapPawns == null) return s;

            List<Pawn> pawns = map.mapPawns.FreeColonists;
            if (pawns == null || pawns.Count == 0) return s;

            s.count = pawns.Count;
            var tiers = RankTiers(map);
            var silverMap = EconomyBank.PrivateSilverByOwner(map);
            var incomes = new List<float>(pawns.Count);
            var wealths = new List<float>(pawns.Count);

            s.wealthMin = float.MaxValue;
            for (int i = 0; i < pawns.Count; i++)
            {
                Pawn p = pawns[i];
                int silver;
                silverMap.TryGetValue(p, out silver);
                float w = PawnPrivateWealth(p, silver);
                wealths.Add(w);
                s.wealthTotal += w;
                if (w > s.wealthMax) s.wealthMax = w;
                if (w < s.wealthMin) s.wealthMin = w;
                s.publicTotal += PawnPublicWealth(p, silver);
                s.privateSilverTotal += silver;

                float inc = EconomyLedger.GetIncome(p);
                if (inc <= 0f) inc = EconomyLedger.GetLastIncome(p);
                s.incomeTotal += inc;
                incomes.Add(inc);
                if (inc > s.incomeMax) s.incomeMax = inc;
                if (inc < s.incomeMin) s.incomeMin = inc;

                WealthTier t;
                if (tiers.TryGetValue(p, out t))
                {
                    if (t == WealthTier.Low) s.tierLow++;
                    else if (t == WealthTier.High) s.tierHigh++;
                    else s.tierMid++;
                }
            }

            s.wealthAvg = s.count > 0 ? s.wealthTotal / s.count : 0f;
            s.incomeGini = Gini(incomes);
            s.wealthGini = Gini(wealths);
            return s;
        }

        private static Map cachedStatsMap;
        private static int cachedStatsTick = -99999;
        private static ColonyStats cachedStats;

        /// <summary>带缓存的殖民地表统计（默认 250 tick），界面与满意度共用同一份扫描结果。</summary>
        public static ColonyStats Cached(Map map, int ttl = 250)
        {
            if (map == null) return new ColonyStats();
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (map == cachedStatsMap && now - cachedStatsTick < ttl) return cachedStats;
            cachedStats = Compute(map);
            cachedStatsMap = map;
            cachedStatsTick = now;
            return cachedStats;
        }

        /// <summary>基尼系数，0 = 绝对平均，1 = 绝对不均。</summary>
        public static float Gini(List<float> values)
        {
            if (values == null || values.Count < 2) return 0f;
            values.Sort();
            float sum = 0f;
            for (int i = 0; i < values.Count; i++) sum += values[i];
            if (sum <= 0f) return 0f;

            float weighted = 0f;
            for (int i = 0; i < values.Count; i++) weighted += (i + 1) * values[i];
            float n = values.Count;
            return UnityEngine.Mathf.Clamp((2f * weighted) / (n * sum) - (n + 1f) / n, 0f, 1f);
        }

        public static WealthDistribution Classify(Map map, TaxPolicy policy)
        {
            WealthDistribution d = new WealthDistribution();
            if (map == null || map.mapPawns == null) return d;

            List<Pawn> pawns = map.mapPawns.FreeColonists;
            if (pawns == null || pawns.Count == 0) return d;

            List<float> values = new List<float>(pawns.Count);
            for (int i = 0; i < pawns.Count; i++) values.Add(PawnWealth(pawns[i]));
            values.Sort();

            d.total = values.Count;
            d.thresholdLow = values[values.Count / 3];
            d.thresholdHigh = values[(values.Count * 2) / 3];

            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] <= d.thresholdLow) d.low++;
                else if (values[i] <= d.thresholdHigh) d.mid++;
                else d.high++;
            }

            if (policy != null)
            {
                float sum = policy.RateFor(WealthTier.Low) * d.low
                          + policy.RateFor(WealthTier.Mid) * d.mid
                          + policy.RateFor(WealthTier.High) * d.high;
                d.averageRate = d.total > 0 ? sum / d.total : 0f;
            }
            return d;
        }

        /// <summary>个人随身财富：穿戴 + 装备 + 背包物品的市场价（不分公私，用于总量）。</summary>
        public static float PawnWealth(Pawn p)
        {
            if (p == null) return 0f;
            float v = 0f;

            if (p.apparel != null)
            {
                List<Apparel> worn = p.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++) v += worn[i].MarketValue * worn[i].stackCount;
            }
            if (p.equipment != null && p.equipment.Primary != null)
            {
                v += p.equipment.Primary.MarketValue;
            }
            if (p.inventory != null && p.inventory.innerContainer != null)
            {
                foreach (Thing t in p.inventory.innerContainer)
                {
                    v += t.MarketValue * t.stackCount;
                }
            }
            return v;
        }

        /// <summary>只统计"私有"的物品与白银：带私有标签且归属这个人。</summary>
        public static float PawnPrivateWealth(Pawn p)
        {
            if (p == null) return 0f;
            int silver = p.MapHeld != null ? EconomyBank.PrivateSilver(p.MapHeld, p) : 0;
            return PawnPrivateWealth(p, silver);
        }

        /// <summary>同上，但私有银币由调用方预先算好（批量计算时用，避免 O(n²) 扫描）。</summary>
        public static float PawnPrivateWealth(Pawn p, int privateSilver)
        {
            if (p == null) return 0f;
            float v = privateSilver;

            // 用私有物品索引：它已覆盖 背包 / 穿戴 / 手持 / 私有仓库（箱子、货架、地窖）。
            // 以前只扫背包，东西一进箱子财富就"蒸发"，等于鼓励藏私逃税。
            Map map = p.MapHeld;
            if (map == null) map = p.Map;
            if (map != null)
            {
                List<Thing> owned = EconomyPrivate.OwnedItems(p, map);
                if (owned != null)
                {
                    for (int i = 0; i < owned.Count; i++)
                    {
                        Thing t = owned[i];
                        if (t == null || t.Destroyed) continue;
                        if (t.def == RimWorld.ThingDefOf.Silver) continue;
                        v += t.MarketValue * t.stackCount;
                    }
                }
                return v;
            }

            // 不在任何地图上（远征途中）：退回只扫随身。
            if (p.apparel != null)
            {
                List<Apparel> worn = p.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    if (EconomyPrivate.OwnerOf(worn[i]) == p) v += worn[i].MarketValue * worn[i].stackCount;
                }
            }
            if (p.equipment != null && p.equipment.Primary != null
                && EconomyPrivate.OwnerOf(p.equipment.Primary) == p)
            {
                v += p.equipment.Primary.MarketValue;
            }
            if (p.inventory != null && p.inventory.innerContainer != null)
            {
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (t.def == RimWorld.ThingDefOf.Silver) continue;
                    if (EconomyPrivate.OwnerOf(t) == p) v += t.MarketValue * t.stackCount;
                }
            }
            return v;
        }

        /// <summary>随身携带但不属于他私人的部分（公共物资）。</summary>
        public static float PawnPublicWealth(Pawn p)
        {
            int silver = p != null && p.MapHeld != null ? EconomyBank.PrivateSilver(p.MapHeld, p) : 0;
            return PawnPublicWealth(p, silver);
        }

        public static float PawnPublicWealth(Pawn p, int privateSilver)
        {
            return UnityEngine.Mathf.Max(0f, PawnWealth(p) - PawnPrivateWealth(p, privateSilver));
        }
    }
}
