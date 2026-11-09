using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// "这件东西凭什么这么定价"的查看窗。
    /// 搜索物品，每行显示：配方 / 矿 / 建造 / 器官 / 作物工时 / 肉成本 / 市价，
    /// 以及最终命中的是哪条规则。
    ///
    /// 性能：文字在 Rebuild() 里一次性算好并缓存。
    /// 以前是每帧对最多 400 行各调一次 Explain()（里头要走配方表、矿物表、作物表），
    /// 等于每帧做几万次查找，所以滑动严重卡顿。
    /// </summary>
    public class Window_EconomyExplain : Window
    {
        private string filter = "";
        private Vector2 scroll;
        private readonly List<ThingDef> rows = new List<ThingDef>();
        private readonly List<string> lines = new List<string>();
        private string lastFilter = "\u0000";

        public Window_EconomyExplain()
        {
            doCloseX = true;
            closeOnClickedOutside = false;
            absorbInputAroundWindow = true;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(920f, 660f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map map = Find.CurrentMap;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 22f),
                "搜索物品名 / defName：看它被哪条规则定价，以及每个判断的真假。");
            filter = Widgets.TextField(new Rect(0f, 26f, 300f, 24f), filter ?? "");
            if (Widgets.ButtonText(new Rect(308f, 26f, 80f, 24f), "刷新")) lastFilter = "\u0000";
            // 临时：食物定价诊断。放在这里是因为设置页的调试按钮要先勾"开发者模式"才显示，
            // 而这个窗口随时能打开，少一层门槛。
            if (Widgets.ButtonText(new Rect(392f, 26f, 96f, 24f), "诊断食物"))
            {
                EconomyValue.DumpFoodBranch();
            }
            int pickedCount = DshEconomyMod.Settings != null && DshEconomyMod.Settings.shortageOnlyDefs != null
                ? DshEconomyMod.Settings.shortageOnlyDefs.Count : 0;
            Widgets.Label(new Rect(496f, 30f, inRect.width - 496f, 22f),
                map == null ? "需要进入地图"
                : "共 " + rows.Count + " 条" + (pickedCount > 0
                    ? "（预警只提示勾选的 " + pickedCount + " 件）"
                    : "（左侧勾选 = 加入预警，不勾则全部提示）"));

            if (map == null) return;
            if (filter != lastFilter)
            {
                lastFilter = filter;
                Rebuild(map);
            }

            Rect outRect = new Rect(0f, 58f, inRect.width, inRect.height - 58f);
            const float rowH = 118f;
            Rect view = new Rect(0f, 0f, outRect.width - 18f, rows.Count * rowH + 8f);
            Widgets.BeginScrollView(outRect, ref scroll, view);

            // 只画可视区内的行：滚出屏幕的不必再交给 UI 排版。
            int first = Mathf.Max(0, Mathf.FloorToInt(scroll.y / rowH) - 1);
            int last = Mathf.Min(rows.Count - 1, Mathf.CeilToInt((scroll.y + outRect.height) / rowH) + 1);

            for (int i = first; i <= last; i++)
            {
                float y = 4f + i * rowH;
                Rect r = new Rect(4f, y, view.width - 8f, rowH - 4f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.04f));

                ThingDef rowDef = rows[i];
                if (rowDef != null)
                {
                    DshEconomySettings st = DshEconomyMod.Settings;
                    if (st != null && st.shortageOnlyDefs != null)
                    {
                        bool picked = st.shortageOnlyDefs.Contains(rowDef.defName);
                        bool nowPicked = picked;
                        Widgets.Checkbox(new Vector2(r.x + 6f, r.y + 6f), ref nowPicked, 24f);
                        if (nowPicked != picked)
                        {
                            if (nowPicked) st.shortageOnlyDefs.Add(rowDef.defName);
                            else st.shortageOnlyDefs.Remove(rowDef.defName);
                        }
                    }
                }

                Widgets.Label(new Rect(r.x + 34f, r.y + 2f, r.width - 40f, rowH - 8f), lines[i]);
            }
            Widgets.EndScrollView();
        }

        private void Rebuild(Map map)
        {
            rows.Clear();
            lines.Clear();
            string f = filter != null ? filter.Trim() : "";
            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count && rows.Count < 400; i++)
            {
                ThingDef d = all[i];
                if (d == null || d.category != ThingCategory.Item) continue;
                if (f.Length > 0
                    && d.defName.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) < 0
                    && (d.label == null || d.label.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) < 0)) continue;
                rows.Add(d);
            }

            // 文字一次性算完。这里允许慢一点——只在改搜索词或点刷新时执行。
            for (int i = 0; i < rows.Count; i++)
            {
                try { lines.Add(EconomyValue.Explain(rows[i], map)); }
                catch { lines.Add(rows[i] != null ? rows[i].defName : "?"); }
            }
        }
    }
}
