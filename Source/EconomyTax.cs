using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>所得税：按本期收入 × 分档税率，从小人个人白银里扣给殖民地。</summary>
    public static class EconomyTax
    {
        public static int CollectIncomeTax(Map map, MapEconomyComponent comp)
        {
            if (comp != null && comp.incomeTax != null) EconomyDoctrine.EnforceTax(map, comp.incomeTax);
            if (map == null || comp == null || comp.incomeTax == null) return 0;

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            TaxPolicy policy = comp.incomeTax;

            if (policy.IsZero)
            {
                for (int i = 0; i < colonists.Count; i++) EconomyLedger.ClearIncome(colonists[i]);
                return 0;
            }

            WealthDistribution dist = TaxPopulation.Classify(map, policy);
            var tiers = TaxPopulation.RankTiers(map);
            int total = 0;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead) continue;

                float income = EconomyLedger.GetIncome(p);
                if (income <= 0f) continue;

                WealthTier tier;
                if (!tiers.TryGetValue(p, out tier)) tier = WealthTier.Mid;
                float rate = policy.RateFor(tier) / 100f;
                int due = Mathf.Max(0, Mathf.FloorToInt(income * rate));

                if (due > 0) total += EconomyBank.TakePrivateSilver(map, p, due);
                EconomyLedger.ClearIncome(p);

                if (due > 0)
                {
                    DevLog.Msg("[DSH-ECONOMY] 所得税：" + p.LabelShort + " 收入 " + income.ToString("0.#")
                        + " 税率 " + (rate * 100f).ToString("0") + "% 应缴 " + due);
                }
            }

            // 人头税：与收入无关，每人固定
            if (policy.pollTaxEnabled && policy.pollTaxAmount > 0f)
            {
                int perHead = Mathf.RoundToInt(policy.pollTaxAmount);
                if (perHead > 0)
                {
                    for (int i = 0; i < colonists.Count; i++)
                    {
                        Pawn p = colonists[i];
                        if (p == null || p.Dead) continue;
                        total += EconomyBank.TakePrivateSilver(map, p, perHead);
                    }
                }
            }

            if (total > 0)
            {
                DevLog.Msg("[DSH-ECONOMY] 本期税收合计 " + total + " 银，国库现有 " + EconomyBank.ColonySilver(map));
            }
            return total;
        }

        private static WealthTier TierOf(Pawn p, WealthDistribution dist)
        {
            float wealth = TaxPopulation.PawnWealth(p);
            if (wealth <= dist.thresholdLow) return WealthTier.Low;
            if (wealth <= dist.thresholdHigh) return WealthTier.Mid;
            return WealthTier.High;
        }
    }
}

