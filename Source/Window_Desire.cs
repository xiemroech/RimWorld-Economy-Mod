using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>购买欲望明细：每人一屏，列出基础冲动与各项系数，方便核对。</summary>
    public class Window_Desire : Window
    {
        private Vector2 scroll;
        private readonly Map map;

        public Window_Desire(Map map)
        {
            this.map = map;
            doCloseX = true;
            doCloseButton = true;
            absorbInputAroundWindow = true;
            forcePause = false;
        }

        public override Vector2 InitialSize => new Vector2(680f, 640f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "购买欲望实时计算");
            Text.Font = GameFont.Small;

            List<Pawn> crew = map.mapPawns.FreeColonists;
            crew.Sort((a, b) => b.LabelShort.CompareTo(a.LabelShort));

            float height = 40f;
            List<List<string>> all = new List<List<string>>();
            for (int i = 0; i < crew.Count; i++)
            {
                List<string> lines = new List<string>();
                EconomyConsumption.DesireBreakdown(crew[i], map, lines);
                all.Add(lines);
                int planLines = EconomySavings.PlanText(crew[i], map).Split('\n').Length;
                height += 46f + planLines * 20f + lines.Count * 20f + 8f;
            }

            Rect outRect = new Rect(0f, 36f, inRect.width, inRect.height - 36f);
            Rect view = new Rect(0f, 0f, outRect.width - 20f, height);
            Widgets.BeginScrollView(outRect, ref scroll, view);

            float y = 0f;
            for (int i = 0; i < crew.Count; i++)
            {
                Pawn p = crew[i];
                float desire = EconomyConsumption.DesireOf(p, map);
                Color old = GUI.color;
                GUI.color = desire >= 0.65f ? new Color(1f, 0.6f, 0.5f)
                          : (desire <= 0.2f ? new Color(0.7f, 0.85f, 1f) : Color.white);
                Widgets.Label(new Rect(0f, y, view.width, 24f),
                    p.LabelShort + "：欲望 " + (desire * 100f).ToString("0") + "%");
                GUI.color = old;
                y += 26f;

                GUI.color = new Color(0.95f, 0.9f, 0.6f);
                string[] plan = EconomySavings.PlanText(p, map).Split('\n');
                for (int k = 0; k < plan.Length; k++)
                {
                    float lh = Mathf.Max(20f, Text.CalcHeight(plan[k], view.width - 24f));
                    Widgets.Label(new Rect(16f, y, view.width - 16f, lh), plan[k]);
                    y += lh + 2f;
                }
                GUI.color = Color.white;

                Text.Font = GameFont.Tiny;
                GUI.color = new Color(0.85f, 0.85f, 0.85f);
                List<string> lines = all[i];
                for (int k = 0; k < lines.Count; k++)
                {
                    Widgets.Label(new Rect(16f, y, view.width - 16f, 20f), lines[k]);
                    y += 20f;
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
