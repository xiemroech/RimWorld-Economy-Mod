using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 医疗白名单：有些 mod 把普通消费品挂在「医药」分类下（比如营养剂、兴奋剂），
    /// 这些不该被"没开自我治疗就不许买"的规则挡住。这里由玩家逐个放行。
    /// </summary>
    public class Window_MedicalWhitelist : Window
    {
        private Vector2 scroll;
        private string filter = "";
        private readonly List<ThingDef> defs = new List<ThingDef>();

        public Window_MedicalWhitelist()
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
            get { return new Vector2(620f, 560f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Widgets.Label(new Rect(0f, 0f, inRect.width, 22f),
                "医疗白名单：勾选的物品即使挂在「医药」分类，也被当作普通消费品，不需要开自我治疗就能买。");
            Widgets.Label(new Rect(0f, 22f, inRect.width, 22f),
                "药品（成瘾品分类）不受限制，本来就允许购买。");

            filter = Widgets.TextField(new Rect(0f, 50f, 220f, 26f), filter);
            if (Widgets.ButtonText(new Rect(228f, 50f, 100f, 26f), "全部清空"))
            {
                DshEconomyMod.Settings.medicalWhitelist.Clear();
            }

            BuildList();

            Rect outRect = new Rect(0f, 84f, inRect.width, inRect.height - 92f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, defs.Count * 30f + 8f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            string f = filter != null ? filter.Trim() : "";
            float y = 2f;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef def = defs[i];
                if (def == null) continue;
                if (f.Length > 0
                    && def.defName.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) < 0
                    && def.label.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                bool on = DshEconomyMod.Settings.medicalWhitelist.Contains(def.defName);
                Rect row = new Rect(0f, y, viewRect.width, 28f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                bool now = on;
                Widgets.Checkbox(new Vector2(6f, y + 4f), ref now, 22f);
                if (now != on)
                {
                    if (now) DshEconomyMod.Settings.medicalWhitelist.Add(def.defName);
                    else DshEconomyMod.Settings.medicalWhitelist.Remove(def.defName);
                }
                Widgets.Label(new Rect(36f, y + 4f, 300f, 24f), def.LabelCap);
                Widgets.Label(new Rect(340f, y + 4f, 260f, 24f), def.defName);
                y += 30f;
            }
            Widgets.EndScrollView();
        }

        private void BuildList()
        {
            defs.Clear();
            EconomyCategoryDef med = DefDatabase<EconomyCategoryDef>.GetNamedSilentFail("DshEco_Medicine");
            if (med == null) return;
            List<ThingDef> list = EconomyRegistry.ItemsIn(med);
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i] != null) defs.Add(list[i]);
            }
        }
    }
}
