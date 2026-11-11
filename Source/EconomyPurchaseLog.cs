using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>一条购买记录：谁、买了什么、几件、多少钱、什么 tick。</summary>
    public class PurchaseEntry : IExposable
    {
        public int pawnId;
        public string pawnName;
        public string label;
        public int count;
        public float price;
        public int tick;

        public void ExposeData()
        {
            Scribe_Values.Look(ref pawnId, "pawnId", 0);
            Scribe_Values.Look(ref pawnName, "pawnName", null);
            Scribe_Values.Look(ref label, "label", null);
            Scribe_Values.Look(ref count, "count", 1);
            Scribe_Values.Look(ref price, "price", 0f);
            Scribe_Values.Look(ref tick, "tick", 0);
        }

        public string Describe()
        {
            string who = string.IsNullOrEmpty(pawnName) ? "（已离开）" : pawnName;
            string what = string.IsNullOrEmpty(label) ? "（未知物品）" : label;
            return who + " 买了 " + what + (count > 1 ? " ×" + count : "")
                   + "，花 " + price.ToString("0.#") + " 银"
                   + "（" + TickLabel(tick) + "）";
        }

        private static string TickLabel(int t)
        {
            int day = t / GenDate.TicksPerDay + 1;
            float hour = (t % GenDate.TicksPerDay) / (float)GenDate.TicksPerHour;
            return "第" + day + "天 " + hour.ToString("0.0") + "时";
        }
    }

    /// <summary>
    /// 购买流水：只留最近几天，超期自动清掉（天数在设置里调）。
    /// </summary>
    public static class EconomyPurchaseLog
    {
        private const int MaxEntries = 400;

        private static List<PurchaseEntry> entries = new List<PurchaseEntry>();

        public static List<PurchaseEntry> All
        {
            get
            {
                if (entries == null) entries = new List<PurchaseEntry>();
                return entries;
            }
        }

        public static void Add(Pawn pawn, Thing thing, int count, float price)
        {
            if (pawn == null || thing == null) return;
            if (price <= 0.01f && count <= 0) return;

            All.Add(new PurchaseEntry
            {
                pawnId = pawn.thingIDNumber,
                pawnName = pawn.LabelShort,
                label = thing.def != null ? thing.def.label : thing.LabelShort,
                count = Mathf.Max(1, count),
                price = price,
                tick = Find.TickManager != null ? Find.TickManager.TicksGame : 0
            });

            if (All.Count > MaxEntries) All.RemoveRange(0, All.Count - MaxEntries);
        }

        public static void Prune()
        {
            float days = DshEconomyMod.Settings != null ? DshEconomyMod.Settings.purchaseLogDays : 2f;
            if (days <= 0f) return;                         // 0 = 永久保留
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            int limit = Mathf.RoundToInt(days * GenDate.TicksPerDay);
            List<PurchaseEntry> list = All;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                if (now - list[i].tick > limit) list.RemoveAt(i);
            }
        }

        public static void Clear()
        {
            All.Clear();
        }

        public static void ExposeData()
        {
            Scribe_Collections.Look(ref entries, "dsh_purchaseLog", LookMode.Deep);
            if (entries == null) entries = new List<PurchaseEntry>();
        }
    }

    /// <summary>购买记录窗口（按时间倒序）。</summary>
    public class Window_PurchaseLog : Window
    {
        private Vector2 scroll;

        public Window_PurchaseLog()
        {
            doCloseX = true;
            draggable = true;
            closeOnClickedOutside = true;
            forcePause = false;
            absorbInputAroundWindow = false;
            onlyOneOfTypeAllowed = true;
            resizeable = true;
        }

        public override Vector2 InitialSize => new Vector2(560f, 460f);

        public override void DoWindowContents(Rect inRect)
        {
            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(0f, 0f, inRect.width - 120f, 32f), "购买记录");
            Text.Font = GameFont.Small;

            float days = DshEconomyMod.Settings != null ? DshEconomyMod.Settings.purchaseLogDays : 2f;
            Widgets.Label(new Rect(0f, 32f, inRect.width, 24f),
                days <= 0f ? "保留：永久（设置里可调）" : ("保留最近 " + days.ToString("0.#") + " 天（设置里可调）"));

            if (Widgets.ButtonText(new Rect(inRect.width - 110f, 0f, 110f, 28f), "清空记录"))
            {
                EconomyPurchaseLog.Clear();
            }

            Rect outRect = new Rect(0f, 60f, inRect.width, inRect.height - 60f);
            List<PurchaseEntry> list = EconomyPurchaseLog.All;
            float rowH = 24f;
            Rect view = new Rect(0f, 0f, outRect.width - 20f, Mathf.Max(outRect.height, list.Count * rowH));
            Widgets.BeginScrollView(outRect, ref scroll, view);
            float y = 0f;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                PurchaseEntry e = list[i];
                if (e == null) continue;
                if (y + rowH > scroll.y - rowH && y < scroll.y + outRect.height + rowH)
                {
                    GUI.color = i % 2 == 0 ? Color.white : new Color(1f, 1f, 1f, 0.85f);
                    Widgets.Label(new Rect(0f, y, view.width, rowH), e.Describe());
                    GUI.color = Color.white;
                }
                y += rowH;
            }
            if (list.Count == 0)
            {
                Widgets.Label(new Rect(0f, 0f, view.width, rowH), "（还没有记录）");
            }
            Widgets.EndScrollView();
        }
    }
}
