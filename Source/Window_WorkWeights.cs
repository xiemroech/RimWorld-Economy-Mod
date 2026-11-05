using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>工作类型工资权重：两列网格，避免在主界面里一条长列表往下滑。</summary>
    public class Window_WorkWeights : Window
    {
        private Vector2 scroll;

        public Window_WorkWeights()
        {
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = false;
            forcePause = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(720f, 520f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            DshEconomySettings s = DshEconomyMod.Settings;
            if (s == null) return;

            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 22f),
                "工作类型工资权重：劳动量 × 权重 = 计入工资的有效工时（艺术/研究 2.0、养育 1.7、手工 1.5）");

            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y + 24f, 140f, 24f), "全部恢复默认"))
            {
                s.workMult.Clear();
            }
            Widgets.Label(new Rect(inRect.x + 152f, inRect.y + 28f, 500f, 22f),
                "范围 0 - 5，可直接输入数字");

            List<WorkTypeDef> wts = DefDatabase<WorkTypeDef>.AllDefsListForReading;
            int rows = Mathf.CeilToInt(wts.Count / 2f);

            Rect outRect = new Rect(inRect.x, inRect.y + 54f, inRect.width, inRect.height - 54f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, rows * 28f + 8f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float colWidth = (viewRect.width - 10f) / 2f;
            for (int i = 0; i < wts.Count; i++)
            {
                WorkTypeDef wt = wts[i];
                int col = i / rows;
                int row = i % rows;

                float x = col * (colWidth + 10f);
                float y = 2f + row * 28f;

                Widgets.Label(new Rect(x, y + 3f, 90f, 22f), wt.labelShort);
                if (Widgets.ButtonText(new Rect(x + 92f, y, 26f, 22f), "-"))
                {
                    s.SetWorkMult(wt, s.GetWorkMult(wt) - 0.1f);
                }
                string text = s.GetWorkMult(wt).ToString("0.0");
                string edited = Widgets.TextField(new Rect(x + 122f, y, 44f, 22f), text);
                float parsed;
                if (edited != text && float.TryParse(edited, out parsed))
                {
                    s.SetWorkMult(wt, parsed);
                }
                if (Widgets.ButtonText(new Rect(x + 170f, y, 26f, 22f), "+"))
                {
                    s.SetWorkMult(wt, s.GetWorkMult(wt) + 0.1f);
                }
            }

            Widgets.EndScrollView();
        }
    }
}
