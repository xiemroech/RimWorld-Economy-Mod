using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 通货膨胀：MV = PQ。
    ///
    ///   M = 货币量：国库白银 + 所有人手里的私银
    ///   Q = 商品总量：地图上所有物品的**价值**合计（不是件数）
    ///
    /// 均衡状态下货币量约为商品价值的一半（0.5Q）；偏离就会推高/压低物价。
    /// 物价上涨又会通过"工资跟随"反过来推高成本 → 正反馈，可能真的飞天，
    /// 这正是要玩家去调控的部分。
    /// </summary>
    public static class EconomyInflation
    {
        // 货币量相对商品价值的均衡比例：M = EquilibriumRatio × Q + Baseline
        public const float EquilibriumRatio = 0.5f;
        public const float Baseline = 300f;

        // 指数曲线：指数 = (M / 均衡)^Curve
        public const float Curve = 0.6f;
        public const float MinIndex = 0.5f;
        public const float MaxIndex = 12f;

        // 工资跟随：通胀压力每 1.0，劳动日价值上浮多少（0.8 = 80%）
        public const float WageFollowStrength = 0.8f;

        // 恶性通胀分界：指数超过这个数就进入失控区间
        public const float HyperThreshold = 3f;

        /// <summary>货币总量：国库 + 私银。</summary>
        public static float MoneySupply(Map map)
        {
            if (map == null) return 0f;
            float m = EconomyBank.ColonySilver(map);
            Dictionary<Pawn, int> priv = EconomyBank.PrivateSilverByOwner(map);
            if (priv != null)
            {
                foreach (KeyValuePair<Pawn, int> kv in priv) m += kv.Value;
            }
            return m;
        }

        /// <summary>商品总价值（地图上所有物品按 c+v+s 计）。</summary>
        public static float GoodsValue(Map map)
        {
            if (map == null) return 0f;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            return comp != null ? comp.GoodsValue : 0f;
        }

        /// <summary>银价指数：由货币量与商品价值之比决定，夹在 0.5–5.0。</summary>
        public static float Index(Map map)
        {
            if (map == null) return 1f;
            float m = MoneySupply(map);
            float q = GoodsValue(map);
            if (q <= 1f) return 1f;
            float equilibrium = EquilibriumRatio * q + Baseline;

            // 流通速度：同样的钱，转手越快推起来的物价越高（费雪方程 MV = PQ 的 V）。
            // 用"本期成交件数 ÷ 货币量"粗估：钱少、货卖得多，说明周转快。
            float velocity = 1f;
            if (m > 1f)
            {
                float traded = EconomyTradeVolume.TotalThisPeriod();
                velocity = Mathf.Clamp(traded / m, 0.5f, 2f);
            }

            float raw = (m * velocity) / Mathf.Max(1f, equilibrium);
            return Mathf.Clamp(Mathf.Pow(Mathf.Max(raw, 0.01f), Curve), MinIndex, MaxIndex);
        }

        /// <summary>通胀压力（0 以上表示物价在涨），存在地图组件上，逐段逼近。</summary>
        public static float Pressure(Map map)
        {
            if (map == null) return 0f;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            return comp != null ? comp.InflationPressure : 0f;
        }

        /// <summary>每 250 tick 调一次：刷新商品价值、逼近压力、必要时让工资跟随。</summary>
        public static void Tick(Map map, int interval)
        {
            if (map == null) return;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return;

            comp.RefreshGoodsValue();

            float index = Index(map);
            float target = Mathf.Clamp(index - 1f, -0.5f, 8f);
            // 涨得快、落得慢：涨价压力有惯性，这就是螺旋的燃料
            float rate = target > comp.InflationPressure ? 0.08f : 0.02f;
            comp.InflationPressure = Mathf.Lerp(comp.InflationPressure, target, rate);

            // 工资跟随：压力越大，全局劳动日价值被推得越高
            if (comp.wages != null)
            {
                float mult = 1f + Mathf.Max(0f, comp.InflationPressure) * WageFollowStrength;
                comp.wages.inflationMultiplier = Mathf.Clamp(mult, 1f, 10f);
            }
        }

        public static string Describe(Map map)
        {
            float m = MoneySupply(map);
            float q = GoodsValue(map);
            float idx = Index(map);
            string tier = idx >= HyperThreshold ? "　【恶性通胀】" : "";
            return "货币 " + m.ToString("0") + " 银"
                + "　商品价值 " + q.ToString("0") + " 银"
                + "　银价指数 ×" + idx.ToString("0.000")
                + "　通胀压力 " + Pressure(map).ToString("0.00") + tier;
        }
    }
}
