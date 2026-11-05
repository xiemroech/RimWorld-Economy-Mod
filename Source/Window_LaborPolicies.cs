using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>指定哪些殖民者是军人。</summary>
    public class Window_Soldiers : Window
    {
        private readonly Map map;
        private Vector2 scroll;

        public Window_Soldiers(Map map)
        {
            this.map = map;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            draggable = true;
        }

        public override Vector2 InitialSize => new Vector2(560f, 600f);

        public override void DoWindowContents(Rect inRect)
        {
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp == null) return;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 32f), "指定军人");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 34f, inRect.width, 40f),
                "军人：禁掉全部工作（只保留战斗），对罢工/暴动/革命及其它精神状态有 98% 免疫；\n"
                + "满意度 ≥15：免疫 98%；10：70%；5：42%；0：-30%（免疫为负 = 比普通人更容易被卷进去）。");

            bool enabled = comp.militaryEnabled;
            Widgets.CheckboxLabeled(new Rect(0f, 80f, 240f, 24f), "启用军人制度", ref enabled);
            if (enabled != comp.militaryEnabled)
            {
                comp.militaryEnabled = enabled;
                EconomyMilitary.ReapplyAll(map);
            }

            // 军人固定工资
            bool sw = comp.soldierWageEnabled;
            Widgets.CheckboxLabeled(new Rect(0f, 110f, 300f, 24f), "军人固定工资（不按劳动量）", ref sw);
            if (sw != comp.soldierWageEnabled) comp.soldierWageEnabled = sw;
            if (Widgets.ButtonText(new Rect(310f, 112f, 28f, 22f), "-")) comp.soldierWage = Mathf.Max(0, comp.soldierWage - 10);
            Widgets.Label(new Rect(342f, 112f, 90f, 22f), comp.soldierWage + " 银/期");
            if (Widgets.ButtonText(new Rect(438f, 112f, 28f, 22f), "+")) comp.soldierWage = Mathf.Min(9999, comp.soldierWage + 10);
            Widgets.Label(new Rect(474f, 112f, 240f, 22f), "每期发一次，不受劳动量与日薪上限影响");

            Rect body = new Rect(0f, 148f, inRect.width, inRect.height - 148f);
            List<Pawn> colonists = new List<Pawn>(map.mapPawns.FreeColonists);
            Rect view = new Rect(0f, 0f, body.width - 20f, Mathf.Max(colonists.Count * 30f + 10f, body.height));
            Widgets.BeginScrollView(body, ref scroll, view);
            float y = 0f;
            foreach (Pawn p in colonists)
            {
                if (p == null || p.Dead) continue;
                bool on = EconomyMilitary.IsSoldier(p);
                bool nv = on;
                Widgets.CheckboxLabeled(new Rect(0f, y + 3f, 30f, 24f), "", ref nv);
                Widgets.Label(new Rect(34f, y + 6f, 220f, 24f), p.LabelShort);
                Widgets.Label(new Rect(260f, y + 6f, 120f, 24f),
                    "满意度 " + Mathf.RoundToInt(EconomyLedger.GetSatisfaction(p)));
                Widgets.Label(new Rect(384f, y + 6f, 120f, 24f),
                    on ? "免疫 " + Mathf.RoundToInt(EconomyMilitary.Immunity(p) * 100f) + "%" : "—");
                if (nv != on) EconomyMilitary.SetSoldier(p, nv);
                y += 30f;
            }
            Widgets.EndScrollView();
        }
    }

    /// <summary>搬运白名单（按人）：每个人可以单独设置只搬哪些物品。</summary>
    public class Window_HaulFilter : Window
    {
        private readonly Map map;
        private Pawn sel;
        private Vector2 pawnScroll;
        private Vector2 itemScroll;
        private string search = "";
        private List<ThingDef> cached;
        private string cachedKey;

        public Window_HaulFilter(Map map)
        {
            this.map = map;
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            draggable = true;
            List<Pawn> all = Haulers();
            if (all.Count > 0) sel = all[0];
        }

        public override Vector2 InitialSize => new Vector2(820f, 620f);

        /// <summary>能搬运的单位：殖民者、会搬运训练的动物、玩家机械族。</summary>
        private List<Pawn> Haulers()
        {
            List<Pawn> list = new List<Pawn>();
            if (map != null)
            {
                IReadOnlyList<Pawn> all = map.mapPawns.AllPawnsSpawned;
                for (int i = 0; i < all.Count; i++)
                {
                    Pawn p = all[i];
                    if (p == null || p.Dead || p.Downed) continue;
                    if (p.Faction != Faction.OfPlayer) continue;
                    bool ok = p.IsColonist || p.RaceProps.Animal || p.RaceProps.IsMechanoid;
                    if (ok) list.Add(p);
                }
            }
            list.Sort((a, b) => string.Compare(a.LabelShort, b.LabelShort, System.StringComparison.Ordinal));
            return list;
        }

        private List<ThingDef> Allowed()
        {
            string key = search ?? "";
            if (cached != null && cachedKey == key) return cached;
            List<ThingDef> list = new List<ThingDef>();
            foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
            {
                if (d == null || d.category == null || d.IsCorpse) continue;
                if (!d.PlayerAcquirable) continue;
                if (!string.IsNullOrEmpty(key) && d.label != null
                    && d.label.IndexOf(key, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                list.Add(d);
            }
            list.Sort((a, b) => string.Compare(a.label, b.label, System.StringComparison.Ordinal));
            cached = list;
            cachedKey = key;
            return list;
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (map == null) { Widgets.Label(inRect, "没有地图"); return; }

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width, 30f), "搬运白名单（按人）");
            Text.Font = GameFont.Small;
            Widgets.Label(new Rect(0f, 30f, inRect.width, 40f),
                "只对这个人生效：勾选后，他只会自动搬运列表里的物品；列表为空 = 不限制。"
                + "\n手动右键指派不受限制。");

            Rect left = new Rect(0f, 76f, 200f, inRect.height - 120f);
            Widgets.DrawMenuSection(left);
            Rect lp = left.ContractedBy(6f);
            float rowH = 28f;
            Rect lview = new Rect(0f, 0f, lp.width - 16f, Haulers().Count * rowH);
            Widgets.BeginScrollView(lp, ref pawnScroll, lview);
            List<Pawn> cs = Haulers();
            for (int i = 0; i < cs.Count; i++)
            {
                Pawn p = cs[i];
                Rect r = new Rect(0f, i * rowH, lview.width, rowH - 2f);
                bool isSel = p == sel;
                if (isSel) Widgets.DrawHighlightSelected(r);
                else if (i % 2 == 1) Widgets.DrawLightHighlight(r);
                bool en = EconomyHaulFilter.IsEnabled(p);
                Widgets.Label(new Rect(r.x + 4f, r.y + 4f, r.width - 30f, 22f),
                    (en ? "√ " : "  ") + p.LabelShort);
                if (Widgets.ButtonInvisible(r)) sel = p;
            }
            Widgets.EndScrollView();

            Rect right = new Rect(208f, 76f, inRect.width - 208f, inRect.height - 120f);
            if (sel == null) { Widgets.Label(right, "左边选一个人"); return; }

            HaulFilter f = EconomyHaulFilter.For(map, sel, true);
            bool enabled = f != null && f.enabled;
            Widgets.CheckboxLabeled(new Rect(right.x, right.y, 260f, 24f), "启用白名单（勾选后只搬列表物品）", ref enabled);
            if (f != null && enabled != f.enabled) EconomyHaulFilter.SetEnabled(sel, enabled);

            int cnt = f != null && f.allowed != null ? f.allowed.Count : 0;
            Widgets.Label(new Rect(right.x + 280f, right.y + 3f, 200f, 22f), "已选 " + cnt + " 种");
            if (Widgets.ButtonText(new Rect(right.xMax - 90f, right.y, 90f, 24f), "全部清空"))
                EconomyHaulFilter.Clear(sel);

            Rect sr = new Rect(right.x, right.y + 30f, 220f, 26f);
            search = Widgets.TextField(sr, search ?? "");
            if (string.IsNullOrEmpty(search)) Widgets.Label(new Rect(sr.x + 6f, sr.y + 4f, sr.width, 20f), "<color=#808080>搜索物品</color>");

            Rect list = new Rect(right.x, right.y + 62f, right.width, right.height - 62f);
            Widgets.DrawMenuSection(list);
            Rect inner = list.ContractedBy(4f);
            List<ThingDef> defs = Allowed();
            int show = Mathf.Min(defs.Count, 400);
            Rect view = new Rect(0f, 0f, inner.width - 16f, show * 26f);
            Widgets.BeginScrollView(inner, ref itemScroll, view);
            for (int i = 0; i < show; i++)
            {
                ThingDef d = defs[i];
                Rect r = new Rect(0f, i * 26f, view.width, 24f);
                if (i % 2 == 1) Widgets.DrawLightHighlight(r);
                bool on = f != null && f.allowed != null && f.allowed.Contains(d);
                bool nn = on;
                Widgets.Checkbox(new Vector2(r.x + 2f, r.y + 2f), ref nn, 20f);
                if (nn != on) EconomyHaulFilter.Toggle(sel, d, nn);
                Widgets.ThingIcon(new Rect(r.x + 28f, r.y + 2f, 20f, 20f), d);
                Widgets.Label(new Rect(r.x + 54f, r.y + 3f, r.width - 60f, 20f), d.LabelCap);
            }
            Widgets.EndScrollView();
            if (defs.Count > show)
                Widgets.Label(new Rect(list.x + 6f, list.yMax - 20f, list.width, 18f),
                    "<color=#909090>只显示前 " + show + " 项，用搜索缩小范围</color>");
        }
    }
}
