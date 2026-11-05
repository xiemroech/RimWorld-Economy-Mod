using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>奖金组成员选择：勾选式列表，不用在工资表里逐个下滑。</summary>
    public class Window_BonusMembers : Window
    {
        private readonly BonusGroup group;
        private readonly List<Pawn> colonists;
        private Vector2 scroll;

        public Window_BonusMembers(BonusGroup group, List<Pawn> colonists)
        {
            this.group = group;
            this.colonists = colonists ?? new List<Pawn>();
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = false;
            forcePause = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(340f, 460f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (group == null)
            {
                Widgets.Label(inRect, "没有选中任何组。");
                return;
            }

            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f), group.name + " 成员（勾选即加入）");

            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y + 26f, 100f, 24f), "全选"))
            {
                for (int i = 0; i < colonists.Count; i++)
                {
                    if (!group.Has(colonists[i])) group.Toggle(colonists[i]);
                }
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 108f, inRect.y + 26f, 100f, 24f), "全不选"))
            {
                group.members.Clear();
            }

            Rect outRect = new Rect(inRect.x, inRect.y + 56f, inRect.width, inRect.height - 56f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, colonists.Count * 28f + 8f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 2f;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null) continue;

                bool inGroup = group.Has(p);
                bool before = inGroup;
                Widgets.Checkbox(new Vector2(4f, y + 3f), ref inGroup, 22f);
                if (inGroup != before) group.Toggle(p);

                Widgets.Label(new Rect(34f, y + 4f, viewRect.width - 40f, 22f),
                    p.LabelShort + "    劳动 " + (EconomyLedger.GetRawLabor(p) / GenDate.TicksPerDay).ToString("0.00") + " 人日");
                y += 28f;
            }

            Widgets.EndScrollView();
        }
    }
}
