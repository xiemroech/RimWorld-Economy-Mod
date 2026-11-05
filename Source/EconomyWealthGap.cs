using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>贫富差距：最富/自己的倍数超过阈值时，较穷的人会掉工作满意度。</summary>
    public static class EconomyWealthGap
    {
        public const float BaseThreshold = 3f;      // 完全公有时的阈值
        public const float PrivateBoost = 5f;       // 完全私有制时阈值最多 +5（更难触发）
        private static int cacheTick = -99999;
        private static Map cacheMap;
        private static float cacheMax = 1f;
        private static float cacheMin = 0f;
        private static float cacheGini = 0f;

        public static float Gini
        {
            get
            {
                Map map = Find.CurrentMap;
                if (map != null) EnsureCache(map);
                return cacheGini;
            }
        }

        public static bool Enabled(Pawn p)
        {
            return p != null && p.IsColonist;
        }

        /// <summary>复用 TaxPopulation.ColonyStats 的统计（同一份扫描结果，缓存 2500 tick）。</summary>
        private static void EnsureCache(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (map == cacheMap && now - cacheTick < 250) return;
            cacheMap = map;
            cacheTick = now;
            TaxPopulation.ColonyStats st = TaxPopulation.Cached(map, 250);
            cacheMax = Mathf.Max(1f, st.wealthMax);
            cacheMin = st.wealthMin;
            cacheGini = st.wealthGini;
        }

        public static float RatioOf(Pawn p)
        {
            Map map = p.MapHeld ?? p.Map;
            if (map == null) return 1f;
            EnsureCache(map);
            float mine = TaxPopulation.PawnPrivateWealth(p, EconomyBank.PrivateSilver(map, p));
            if (mine < 1f) mine = 1f;
            return cacheMax / mine;
        }

        /// <summary>私有制程度越高，阈值越高（越不容易触发）。</summary>
        public static float ThresholdOf(Pawn p)
        {
            Map map = p.MapHeld ?? p.Map;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            float avg = comp != null ? comp.AveragePublicization() : 50f;   // 0=全私有，100=全公有
            float privateShare = 1f - Mathf.Clamp01(avg / 100f);
            return BaseThreshold + PrivateBoost * privateShare;
        }

        public static string Describe(Pawn p)
        {
            Map map = p.MapHeld ?? p.Map;
            if (map == null) return "-";
            EnsureCache(map);
            return "最富 " + cacheMax.ToString("0") + " / 最穷 " + cacheMin.ToString("0")
                + " 银，你的倍数 " + RatioOf(p).ToString("0.0") + "，阈值 " + ThresholdOf(p).ToString("0.0")
                + "，财富基尼 " + cacheGini.ToString("0.00");
        }
    }
}
