using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 个人工资参数：劳动日价值、工资比例、日薪上限、标准工作时、加班。
    /// 每一项都能单独"用全局"，没设置的项沿用全局值。
    /// </summary>
    public class Window_PawnWage : Window
    {
        private Pawn sel;
        private Vector2 scroll;
        private Vector2 listScroll;

        public Window_PawnWage()
        {
            forcePause = false;
            absorbInputAroundWindow = false;
            doCloseX = true;
            closeOnClickedOutside = true;
            draggable = true;
            resizeable = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(760f, 560f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            WagePolicy w = comp != null ? comp.wages : null;
            if (w == null)
            {
                Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "没有工资政策");
                return;
            }

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            if (sel == null || !colonists.Contains(sel))
            {
                sel = colonists.Count > 0 ? colonists[0] : null;
            }

            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f),
                "个人工资参数：选一个人，单独改他的劳动日价值 / 比例 / 上限 / 标准工时 / 加班。");
            Widgets.Label(new Rect(0f, 22f, inRect.width, 24f),
                "标 * 的项正在用全局值；点「用全局」可以退回全局。全局：日价值 "
                + w.laborDayValue.ToString("0") + "，比例 " + (w.wageShare * 100f).ToString("0") + "%，上限 "
                + w.wageCapPerDay.ToString("0") + "，标准工时 " + w.standardWorkday.ToString("0.0"));

            // ── 左：居民列表 ──
            Rect listRect = new Rect(0f, 54f, 200f, inRect.height - 86f);
            Widgets.DrawMenuSection(listRect);
            Rect listInner = listRect.ContractedBy(4f);
            float listH = Mathf.Max(listInner.height, colonists.Count * 30f + 8f);
            Widgets.BeginScrollView(listInner, ref listScroll, new Rect(0f, 0f, listInner.width - 16f, listH));
            float ly = 4f;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                bool custom = w.HasOverride(p);
                Rect row = new Rect(0f, ly, listInner.width - 18f, 28f);
                if (p == sel) Widgets.DrawHighlightSelected(row);
                string label = p.LabelShort + (custom ? " *" : "");
                if (Widgets.ButtonText(row, label))
                {
                    sel = p;
                }
                ly += 30f;
            }
            Widgets.EndScrollView();

            // ── 右：参数 ──
            Rect panel = new Rect(212f, 54f, inRect.width - 212f, inRect.height - 86f);
            Widgets.DrawMenuSection(panel);
            Rect inner = panel.ContractedBy(10f);

            if (sel == null)
            {
                Widgets.Label(inner, "没有殖民者。");
                return;
            }

            WageOverride o = w.OverrideOf(sel, false);
            float y = inner.y + 2f;
            Widgets.Label(new Rect(inner.x, y, inner.width, 24f), "正在编辑：" + sel.LabelShort);
            y += 30f;

            NumberRow(w, inner, ref y, "劳动日价值（银）", w.LaborDayValueFor(sel),
                o != null && o.laborDayValue >= 0f, 5f, "0",
                v => SetField(w, sel, 0, v), () => ResetField(w, sel, 0));

            NumberRow(w, inner, ref y, "工资比例（%）", w.ShareFor(sel) * 100f,
                o != null && o.wageShare >= 0f, 10f, "0",
                v => SetField(w, sel, 1, v / 100f), () => ResetField(w, sel, 1));

            NumberRow(w, inner, ref y, "日薪上限（银/日）", w.CapFor(sel),
                o != null && o.capPerDay >= 0f, 5f, "0",
                v => SetField(w, sel, 2, v), () => ResetField(w, sel, 2));

            NumberRow(w, inner, ref y, "标准工作时（人日）", w.StandardWorkdayFor(sel),
                o != null && o.standardWorkday >= 0f, 0.1f, "0.0",
                v => SetField(w, sel, 3, v), () => ResetField(w, sel, 3));

            // 加班：开关 + 倍率
            bool otOn = w.OvertimeOnFor(sel);
            bool otCustom = o != null && o.overtimeEnabled >= 0;
            Widgets.Label(new Rect(inner.x, y + 4f, 150f, 24f), "加班工资");
            if (Widgets.ButtonText(new Rect(inner.x + 152f, y, 70f, 26f), otOn ? "开" : "关"))
            {
                WageOverride ov = w.OverrideOf(sel, true);
                ov.overtimeEnabled = otOn ? 0 : 1;
            }
            Widgets.Label(new Rect(inner.x + 228f, y + 4f, 60f, 24f), otCustom ? "自定义" : "全局");
            if (otCustom && Widgets.ButtonText(new Rect(inner.x + 288f, y, 70f, 26f), "用全局"))
            {
                ResetField(w, sel, 5);
            }
            y += 32f;

            NumberRow(w, inner, ref y, "加班倍率（×）", w.OvertimeMultFor(sel),
                o != null && o.overtimeMultiplier >= 0f, 0.1f, "0.0",
                v => SetField(w, sel, 4, v), () => ResetField(w, sel, 4));

            y += 6f;
            float produced = w.ProducedValue(EconomyLedger.GetLabor(sel), sel);
            float wage = w.WageFor(EconomyLedger.GetLabor(sel), sel, EconomyLedger.GetRawLabor(sel));
            Widgets.Label(new Rect(inner.x, y, inner.width, 22f),
                "本期：劳动 " + (EconomyLedger.GetLabor(sel) / GenDate.TicksPerDay).ToString("0.00")
                + " 人日，创造 " + produced.ToString("0.0") + " 银，实发 " + wage.ToString("0.0") + " 银");
            y += 26f;
            if (sel.MapHeld != null)
            {
                float hrs = comp != null ? comp.DailyWorkHours(sel) : 0f;
                Widgets.Label(new Rect(inner.x, y, inner.width, 22f),
                    "今日劳动 " + hrs.ToString("0.0") + " 小时；超过 "
                    + EconomySatisfaction.StandardHoursPerDay.ToString("0") + " 小时的部分每 1 小时扣 "
                    + EconomySatisfaction.OvertimePenaltyPerHour.ToString("0.00") + " 满意度（最多 "
                    + EconomySatisfaction.MaxOvertimePenalty.ToString("0") + "）");
            }

            if (Widgets.ButtonText(new Rect(inner.x, inner.yMax - 34f, 160f, 28f), "这个人全部用全局"))
            {
                w.ClearOverride(sel);
            }
            if (Widgets.ButtonText(new Rect(inner.x + 170f, inner.yMax - 34f, 160f, 28f), "所有人全部用全局"))
            {
                w.overrides.Clear();
            }
        }

        private static void SetField(WagePolicy w, Pawn p, int field, float v)
        {
            WageOverride o = w.OverrideOf(p, true);
            switch (field)
            {
                case 0: o.laborDayValue = Mathf.Clamp(v, 0f, 9999f); break;
                case 1: o.wageShare = Mathf.Clamp01(v); break;
                case 2: o.capPerDay = Mathf.Clamp(v, 0f, 9999f); break;
                case 3: o.standardWorkday = Mathf.Clamp(v, 0f, 10f); break;
                case 4: o.overtimeMultiplier = Mathf.Clamp(v, 1f, 10f); break;
                case 5: o.overtimeEnabled = -1; break;
            }
        }

        private static void ResetField(WagePolicy w, Pawn p, int field)
        {
            WageOverride o = w.OverrideOf(p, false);
            if (o == null) return;
            switch (field)
            {
                case 0: o.laborDayValue = -1f; break;
                case 1: o.wageShare = -1f; break;
                case 2: o.capPerDay = -1f; break;
                case 3: o.standardWorkday = -1f; break;
                case 4: o.overtimeMultiplier = -1f; break;
                case 5: o.overtimeEnabled = -1; break;
            }
            if (!o.AnyCustom) w.ClearOverride(p);
        }

        private static void NumberRow(WagePolicy w, Rect inner, ref float y, string label,
            float value, bool custom, float step, string fmt, Action<float> set, Action reset)
        {
            Widgets.Label(new Rect(inner.x, y + 4f, 170f, 24f), label);
            if (Widgets.ButtonText(new Rect(inner.x + 172f, y, 34f, 26f), "−"))
            {
                set(value - step);
            }
            Widgets.Label(new Rect(inner.x + 210f, y + 4f, 110f, 24f),
                value.ToString(fmt) + (custom ? "" : " *"));
            if (Widgets.ButtonText(new Rect(inner.x + 324f, y, 34f, 26f), "+"))
            {
                set(value + step);
            }
            if (custom && Widgets.ButtonText(new Rect(inner.x + 366f, y, 80f, 26f), "用全局"))
            {
                reset();
            }
            y += 32f;
        }
    }
}
