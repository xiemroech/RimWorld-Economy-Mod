using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>选择所有者：公共 / 某个殖民者。用于堆放区与存储建筑的产权变更，支持搜索。</summary>
    public class Window_PawnPicker : Window
    {
        private readonly Action<Pawn> onPick;
        private readonly List<Pawn> colonists;
        private Vector2 scroll;
        private string filter = "";
        private bool onlyUnowned;
        private readonly List<Pawn> shown = new List<Pawn>();

        public Window_PawnPicker(Action<Pawn> onPick, List<Pawn> colonists)
        {
            this.onPick = onPick;
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
            get { return new Vector2(340f, 440f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f), "选择所有者（可搜索）");

            filter = Widgets.TextField(new Rect(inRect.x, inRect.y + 26f, inRect.width - 150f, 26f), filter);
            Widgets.CheckboxLabeled(new Rect(inRect.x + inRect.width - 146f, inRect.y + 27f, 146f, 24f),
                "只看无不动产者", ref onlyUnowned);

            // 过滤
            shown.Clear();
            string f = filter != null ? filter.Trim() : "";
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null) continue;
                if (onlyUnowned && EconomyRealEstate.HasAnyRealEstate(p)) continue;
                if (f.Length > 0
                    && p.LabelShort.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0
                    && (p.Name == null || p.Name.ToStringFull.IndexOf(f, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                shown.Add(p);
            }

            Rect outRect = new Rect(inRect.x, inRect.y + 58f, inRect.width, inRect.height - 58f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, (shown.Count + 1) * 30f + 10f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 0f;
            if (Widgets.ButtonText(new Rect(0f, y, viewRect.width, 26f), "公共（无主）"))
            {
                if (onPick != null) onPick(null);
                Close();
                return;
            }
            y += 30f;

            for (int i = 0; i < shown.Count; i++)
            {
                Pawn p = shown[i];
                if (Widgets.ButtonText(new Rect(0f, y, viewRect.width, 26f), p.LabelShort))
                {
                    if (onPick != null) onPick(p);
                    Close();
                    return;
                }
                y += 30f;
            }

            Widgets.EndScrollView();
        }
    }
}
