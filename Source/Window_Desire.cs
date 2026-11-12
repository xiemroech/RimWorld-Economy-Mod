using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 购买欲望的逐项拆解。
    /// 照价格表的做法：所有数字先算好放进 rows，绘制时只画字，
    /// 滚动不重算；要最新的就点右上角「重新计算」。
    /// </summary>
    public class Window_Desire : Window
    {
        private readonly Map map;
        private Vector2 scroll;

        private class Row
        {
            public Pawn pawn;
            public float desire;
            public string[] plan;
            public List<string> lines;
        }

        private List<Row> rows = new List<Row>();
        private float totalHeight;
        private Map builtMap;
        private bool dirty = true;

        private const float HeaderH = 32f;
        private const float PlanLineH = 20f;
        private const float MathLineH = 18f;

        public Window_Desire(Map map)
        {
            this.map = map;
            doCloseX = true;
            draggable = true;
            closeOnClickedOutside = true;
            forcePause = false;
            absorbInputAroundWindow = false;
        }

        public override Vector2 InitialSize => new Vector2(620f, 600f);

        private void Rebuild()
        {
            rows.Clear();
            totalHeight = 8f;
            dirty = false;
            builtMap = map;
            if (map == null || map.mapPawns == null) return;

            List<Pawn> crew = new List<Pawn>(map.mapPawns.FreeColonists);
            crew.Sort((a, b) => string.Compare(b.LabelShort, a.LabelShort, System.StringComparison.Ordinal));

            for (int i = 0; i < crew.Count; i++)
            {
                Pawn p = crew[i];
                if (p == null) continue;
                Row r = new Row { pawn = p };
                try
                {
                    r.desire = EconomyConsumption.DesireOf(p, map);
                    List<string> lines = new List<string>();
                    EconomyConsumption.DesireBreakdown(p, map, lines);
                    r.lines = lines;
                    r.plan = EconomySavings.PlanText(p, map).Split('\n');
                }
                catch
                {
                    r.desire = 0f;
                    r.lines = new List<string>();
                    r.plan = new string[0];
                }
                rows.Add(r);
                totalHeight += RowHeight(r);
            }
        }

        private static float RowHeight(Row r)
        {
            int plan = r.plan != null ? r.plan.Length : 0;
            int lines = r.lines != null ? r.lines.Count : 0;
            return 26f + plan * PlanLineH + lines * MathLineH + 8f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 120f, HeaderH), "购买欲望拆解");
            Text.Font = GameFont.Small;

            if (Widgets.ButtonText(new Rect(inRect.width - 110f, 0f, 110f, 28f), "重新计算"))
            {
                dirty = true;
            }
            if (dirty || builtMap != map)
            {
                Rebuild();
            }

            Rect outRect = new Rect(0f, HeaderH + 4f, inRect.width, inRect.height - HeaderH - 4f);
            Rect view = new Rect(0f, 0f, outRect.width - 20f, Mathf.Max(outRect.height, totalHeight));
            Widgets.BeginScrollView(outRect, ref scroll, view);

            float y = 0f;
            for (int i = 0; i < rows.Count; i++)
            {
                Row r = rows[i];
                float rowH = RowHeight(r);

                // 只画可视区，滚出屏幕的行一个像素都不画
                if (y + rowH < scroll.y - 60f || y > scroll.y + outRect.height + 60f)
                {
                    y += rowH;
                    continue;
                }

                Color old = GUI.color;
                GUI.color = r.desire >= 0.65f ? new Color(1f, 0.6f, 0.5f)
                          : (r.desire <= 0.2f ? new Color(0.7f, 0.85f, 1f) : Color.white);
                Widgets.Label(new Rect(0f, y, view.width, 24f),
                    r.pawn.LabelShort + "：欲望 " + (r.desire * 100f).ToString("0") + "%");
                GUI.color = old;
                y += 26f;

                GUI.color = new Color(0.95f, 0.9f, 0.6f);
                for (int k = 0; k < r.plan.Length; k++)
                {
                    Widgets.Label(new Rect(16f, y, view.width - 16f, PlanLineH), r.plan[k]);
                    y += PlanLineH;
                }
                GUI.color = Color.white;

                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.85f, 0.85f, 0.85f);
                for (int k = 0; k < r.lines.Count; k++)
                {
                    Widgets.Label(new Rect(16f, y, view.width - 16f, MathLineH), r.lines[k]);
                    y += MathLineH;
                }
                GUI.color = Color.white;
                Text.Font = GameFont.Small;
                y += 8f;

                Widgets.DrawLineHorizontal(0f, y - 4f, view.width);
            }

            Widgets.EndScrollView();
        }
    }
}
