using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>搜索并挑选一件商品，选中后回调。</summary>
    public class Window_ItemPicker : Window
    {
        private readonly Action<ThingDef> onPick;
        private readonly List<ThingDef> all = new List<ThingDef>();
        private readonly List<ThingDef> shown = new List<ThingDef>();
        private string filter = string.Empty;
        private string lastFilter = null;
        private Vector2 scroll;

        public Window_ItemPicker(Action<ThingDef> onPick)
        {
            this.onPick = onPick;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(540f, 620f); }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            all.Clear();
            for (int c = 0; c < EconomyRegistry.Categories.Count; c++)
            {
                List<ThingDef> list = EconomyRegistry.ItemsIn(EconomyRegistry.Categories[c]);
                if (list == null) continue;
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null) all.Add(list[i]);
                }
            }
            all.SortBy(d => d.label);
            Rebuild();
        }

        private void Rebuild()
        {
            shown.Clear();
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef d = all[i];
                if (filter.NullOrEmpty()
                    || d.label.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                    || d.defName.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    shown.Add(d);
                }
            }
            lastFilter = filter;
            scroll = Vector2.zero;
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(inRect.x, inRect.y, 80f, 26f), "搜索");
            filter = Widgets.TextField(new Rect(inRect.x + 60f, inRect.y, inRect.width - 60f, 26f), filter);
            if (filter != lastFilter) Rebuild();

            Rect listRect = new Rect(inRect.x, inRect.y + 32f, inRect.width, inRect.height - 32f);
            Rect viewRect = new Rect(0f, 0f, listRect.width - 18f, shown.Count * 24f + 4f);
            Widgets.BeginScrollView(listRect, ref scroll, viewRect);
            float y = 0f;
            for (int i = 0; i < shown.Count; i++)
            {
                ThingDef d = shown[i];
                if (Widgets.ButtonText(new Rect(4f, y, viewRect.width - 8f, 22f), d.label))
                {
                    if (onPick != null) onPick(d);
                    Close();
                    break;
                }
                y += 24f;
            }
            Widgets.EndScrollView();

            if (shown.Count == 0)
            {
                Widgets.Label(new Rect(inRect.x, listRect.y + 4f, inRect.width, 24f), "没有匹配的商品。");
            }
        }
    }
}
