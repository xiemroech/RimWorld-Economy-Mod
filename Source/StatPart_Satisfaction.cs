using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 工作速度修正：满意度（0 分 → 0.5 倍，100 分 → 1.5 倍）
    /// 再叠加奖金末位的效率惩罚（倒数第 4 起，每往下一名多 1%）。
    /// </summary>
    public class StatPart_Satisfaction : StatPart
    {
        public override void TransformValue(StatRequest req, ref float val)
        {
            Pawn p = req.Thing as Pawn;
            if (p == null || !p.IsColonist) return;
            val *= EconomySatisfaction.WorkSpeedFactor(p);
            val *= EconomySatisfaction.RestFactor(p);
            val *= EconomySatisfaction.HealthFactor(p);
            val *= 1f + BonusSpeedOf(p);
        }

        public override string ExplanationPart(StatRequest req)
        {
            Pawn p = req.Thing as Pawn;
            if (p == null || !p.IsColonist) return null;
            string s = "工作满意度 " + EconomyLedger.GetSatisfaction(p).ToString("0")
                + " → 工作速度 ×" + EconomySatisfaction.WorkSpeedFactor(p).ToString("0.00");
            float rest = EconomySatisfaction.RestFactor(p);
            if (Mathf.Abs(rest - 1f) > 0.0001f)
            {
                s += "\n休息 " + (p.needs != null && p.needs.rest != null
                    ? (p.needs.rest.CurLevel * 100f).ToString("0") + "%" : "-")
                    + " → 劳动效率 ×" + rest.ToString("0.00");
            }
            float health = EconomySatisfaction.HealthFactor(p);
            if (Mathf.Abs(health - 1f) > 0.0001f)
            {
                s += "\n健康（营养不良/力竭/伤痛） → 劳动效率 ×" + health.ToString("0.00");
            }
            float b = BonusSpeedOf(p);
            if (Mathf.Abs(b) > 0.0001f)
            {
                s += "\n奖金排名 ×" + (1f + b).ToString("0.00");
            }
            s += "\n工作效率总计 ×" + EconomySatisfaction.TotalEfficiency(p).ToString("0.00");
            return s;
        }

        private static float BonusSpeedOf(Pawn p)
        {
            Map map = p.MapHeld ?? p.Map;
            if (map == null) return 0f;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            return comp != null ? comp.GetBonusSpeed(p) : 0f;
        }
    }
}
