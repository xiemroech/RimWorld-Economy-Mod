using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 囤积统计：每人手上的私有物品和私银，按估值排序。
    /// 社会化越高、价格越低，囤积越多——这个窗口用来观察消费冲动是否失控。
    /// </summary>
    public class Window_Hoarding : Window
    {
        private Vector2 scroll;

        public Window_Hoarding()
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

            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f),
                "囤积统计：私有物品按物主汇总。社会化越高 → 价格越低 → 消费冲动越强 → 囤积越多，"
                + "私有物重量超过负重 " + (EconomyConsumption.WasteFillRatio * 100f).ToString("0") + "% 开始出现浪费。");
            Widgets.Label(new Rect(0f, 24f, inRect.width, 22f),
                "本期消费 " + (comp != null ? comp.consumptionBought : 0) + " 件，囤积浪费 "
                + (comp != null ? comp.consumptionWasted : 0) + " 件");

            Widgets.Label(new Rect(6f, 54f, 160f, 22f), "殖民者");
            Widgets.Label(new Rect(170f, 54f, 80f, 22f), "私有物品");
            Widgets.Label(new Rect(252f, 54f, 50f, 22f), "件数");
            Widgets.Label(new Rect(305f, 54f, 80f, 22f), "私银");
            Widgets.Label(new Rect(390f, 54f, 100f, 22f), "私产估值");
            Widgets.Label(new Rect(495f, 54f, 150f, 22f), "私有仓库空余");

            Dictionary<Pawn, List<Thing>> index = EconomyPrivate.BuildIndex(map);
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            var rows = new List<KeyValuePair<Pawn, float>>();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                List<Thing> list;
                if (!index.TryGetValue(p, out list) || list == null) continue;
                float value = 0f;
                for (int k = 0; k < list.Count; k++)
                {
                    if (list[k] == null) continue;
                    value += EconomyPrice.UnitPrice(list[k], map);
                }
                rows.Add(new KeyValuePair<Pawn, float>(p, value));
            }
            rows.Sort((a, b) => b.Value.CompareTo(a.Value));

            Rect outRect = new Rect(0f, 78f, inRect.width, inRect.height - 86f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, rows.Count * 26f + 8f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 2f;
            for (int i = 0; i < rows.Count; i++)
            {
                Pawn p = rows[i].Key;
                List<Thing> list = index[p];
                if (list == null) list = new List<Thing>();

                Rect row = new Rect(4f, y, viewRect.width - 8f, 24f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                Widgets.Label(new Rect(8f, y + 2f, 156f, 22f), p.LabelShort);
                Widgets.Label(new Rect(170f, y + 2f, 80f, 22f), Describe(list));
                Widgets.Label(new Rect(252f, y + 2f, 50f, 22f), list.Count.ToString("0"));
                Widgets.Label(new Rect(305f, y + 2f, 80f, 22f),
                    EconomyBank.PrivateSilver(map, p).ToString("N0"));
                Widgets.Label(new Rect(390f, y + 2f, 100f, 22f), rows[i].Value.ToString("0.#"));

                int cells, used;
                float free = EconomyPrivateStorageBehavior.FreeRatio(p, map, out used, out cells);
                string freeText = free < 0f ? "无仓库" : (free * 100f).ToString("0") + "%（" + used + "/" + cells + "）";
                Widgets.Label(new Rect(495f, y + 2f, 150f, 22f), freeText);
                y += 26f;
            }
            Widgets.EndScrollView();
        }

        private static string Describe(List<Thing> list)
        {
            if (list == null || list.Count == 0) return "—";
            Thing top = list[0];
            string s = top.LabelNoCount;
            if (list.Count > 1) s += " 等";
            return s;
        }
    }
}
