using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 数值自检：不靠肉眼、不靠计时，直接读游戏的属性管线。
    /// 只要 StatPart_Satisfaction 真的挂在 WorkSpeedGlobal 上，
    /// 工作速度属性就一定会被满意度乘一遍，而工作速度属性就是原版做活用的那个值。
    /// </summary>
    public static class EconomyDiagnostics
    {
        public static string VerifyWorkSpeed(bool writeLog)
        {
            StatDef stat = StatDefOf.WorkSpeedGlobal;
            var sb = new StringBuilder();

            bool mounted = stat != null && stat.parts != null && stat.parts.Any(p => p is StatPart_Satisfaction);
            sb.AppendLine("① 速度部件挂在 WorkSpeedGlobal 上：" + (mounted ? "是" : "否"));
            if (!mounted)
            {
                sb.Append("（没有挂上，速度不会受满意度影响）");
                if (writeLog) Log.Message("[SPEED] " + sb.ToString().Replace("\n", " | "));
                return sb.ToString();
            }

            Map map = Find.CurrentMap;
            List<Pawn> colonists = map == null ? new List<Pawn>() : map.mapPawns.FreeColonists;
            if (colonists.Count == 0)
            {
                sb.Append("（当前地图没有殖民者）");
                return sb.ToString();
            }

            int good = 0, bad = 0;
            sb.AppendLine();
            foreach (Pawn p in colonists)
            {
                float old = EconomyLedger.GetSatisfaction(p);

                // ② 直接调这个 StatPart，看它是不是乘 0.5 / 1.0 / 1.5
                float t0 = ApplyPart(stat, p, 0f);
                float t50 = ApplyPart(stat, p, 50f);
                float t100 = ApplyPart(stat, p, 100f);

                // ③ 走游戏真正的属性管线：把满意度设成 0 / 100，读两次工作速度
                float v0 = ReadStat(p, 0f);
                float v100 = ReadStat(p, 100f);
                EconomyLedger.AddSatisfaction(p, old - EconomyLedger.GetSatisfaction(p));

                bool partOk = Near(t0, 0.5f) && Near(t50, 1f) && Near(t100, 1.5f);
                bool cached = Mathf.Abs(v100 / Mathf.Max(0.0001f, v0) - 1f) < 0.001f;   // 属性被本帧缓存
                bool statOk = cached || (v0 > 0.01f && v100 > 0.01f && Mathf.Abs(v100 / v0 - 3f) < 0.05f);
                if (partOk && statOk) good++; else bad++;

                string line = p.LabelShort
                    + " 满意度=" + old.ToString("0")
                    + " 系数(0/50/100)=" + t0.ToString("0.00") + "/" + t50.ToString("0.00") + "/" + t100.ToString("0.00")
                    + " 工作速度(0分)=" + v0.ToString("0.00") + " (100分)=" + v100.ToString("0.00")
                    + (cached ? "（属性本帧有缓存，差异看下一项）" : "");
                sb.AppendLine(line + (partOk && statOk ? "  ✅" : "  ❌"));
                if (writeLog) Log.Message("[SPEED] " + line + "  部件=" + (partOk ? "ok" : "BAD") + " 管线=" + (statOk ? "ok" : "BAD"));
            }

            sb.AppendLine();
            sb.Append("结论：" + (bad == 0 ? "生效（" + good + " 人全部通过）" : "有问题（" + bad + " 人未通过）"));
            return sb.ToString();
        }

        static bool Near(float a, float b)
        {
            return Mathf.Abs(a - b) <= 0.001f;
        }

        // 直接把当前满意度当成目标值乘一遍（不动存档数据）
        static float ApplyPart(StatDef stat, Pawn p, float satisfaction)
        {
            float saved = EconomyLedger.GetSatisfaction(p);
            EconomyLedger.AddSatisfaction(p, satisfaction - saved);
            float val = 1f;
            new StatPart_Satisfaction().TransformValue(StatRequest.For(p), ref val);
            EconomyLedger.AddSatisfaction(p, saved - EconomyLedger.GetSatisfaction(p));
            return val;
        }

        static float ReadStat(Pawn p, float satisfaction)
        {
            float saved = EconomyLedger.GetSatisfaction(p);
            EconomyLedger.AddSatisfaction(p, satisfaction - saved);
            float v = p.GetStatValue(StatDefOf.WorkSpeedGlobal);
            EconomyLedger.AddSatisfaction(p, saved - EconomyLedger.GetSatisfaction(p));
            return v;
        }
    }
}
