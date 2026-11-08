using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 小人的「经济」标签页：上半部分是公有空间（还没打私有标签的集体物资），
    /// 中间一条横线，下半部分是私有空间（明确划给这个人的东西）。
    /// </summary>
    public class ITab_Pawn_Economy : ITab
    {
        private Vector2 scroll;

        public ITab_Pawn_Economy()
        {
            size = new Vector2(520f, 560f);
            labelKey = "经济";
        }

        public override bool IsVisible
        {
            get { return SelPawn != null && SelPawn.IsColonist; }
        }

        protected override void FillTab()
        {
            Pawn pawn = SelPawn;
            if (pawn == null) return;

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(8f);

            List<Thing> carried = EconomyPrivate.CarriedPublic(pawn);
            List<Thing> owned = EconomyPrivate.OwnedItems(pawn, pawn.MapHeld);
            int silverBalance = pawn.MapHeld != null ? EconomyBank.PrivateSilver(pawn.MapHeld, pawn) : 0;
            Map map = pawn.MapHeld;

            float viewHeight = 120f + (carried.Count + owned.Count) * 24f;
            Rect outRect = new Rect(rect.x, rect.y + 74f, rect.width, rect.height - 74f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, viewHeight);

            Widgets.Label(new Rect(rect.x, rect.y, rect.width, 22f),
                "个人白银：" + silverBalance.ToString("N0")
                + "    劳动：" + (EconomyLedger.GetLabor(pawn) / GenDate.TicksPerDay).ToString("0.0") + " 人日");

            float pv = TaxPopulation.PawnPrivateWealth(pawn);
            float pub = TaxPopulation.PawnPublicWealth(pawn);
            Widgets.Label(new Rect(rect.x, rect.y + 24f, rect.width, 22f),
                "私有财富：" + pv.ToString("N0") + " 银"
                + "（私银 " + silverBalance.ToString("N0")
                + " + 私有物品与装备 " + Mathf.Max(0f, pv - silverBalance).ToString("N0") + "）"
                + "     公共物资：" + pub.ToString("N0") + " 银");

            // 工作满意度条
            float sat = EconomyLedger.GetSatisfaction(pawn);
            Rect barRect = new Rect(rect.x, rect.y + 48f, rect.width - 220f, 18f);
            Widgets.FillableBar(barRect, Mathf.Clamp01(sat / 100f));
            Widgets.Label(new Rect(rect.x + rect.width - 216f, rect.y + 48f, 216f, 20f),
                EconomySatisfaction.Describe(pawn)
                + "   效率 ×" + EconomySatisfaction.TotalEfficiency(pawn).ToString("0.00"));

            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 2f;

            // ── 公有空间 ──
            Widgets.Label(new Rect(2f, y, viewRect.width, 22f), "公有空间（集体所有，尚未私有化）");
            y += 24f;
            if (carried.Count == 0)
            {
                Widgets.Label(new Rect(14f, y, viewRect.width - 12f, 22f), "（空）");
                y += 22f;
            }
            for (int i = 0; i < carried.Count; i++)
            {
                Thing t = carried[i];
                Widgets.Label(new Rect(14f, y, 250f, 22f), "· " + t.LabelNoCount + " ×" + t.stackCount);
                Widgets.Label(new Rect(268f, y, 70f, 22f), map != null ? EconomyPrice.UnitPrice(t, map).ToString("0.##") : "-");
                if (Widgets.ButtonText(new Rect(340f, y - 1f, 70f, 22f), "私有化"))
                {
                    EconomyPrivate.SetPrivate(t, pawn);
                }
                y += 24f;
            }

            // ── 分隔线 ──
            y += 6f;
            Widgets.DrawBoxSolid(new Rect(2f, y, viewRect.width - 4f, 2f), new Color(0.75f, 0.75f, 0.75f, 0.6f));
            y += 10f;

            // ── 私有空间 ──
            Widgets.Label(new Rect(2f, y, viewRect.width, 22f), "私有空间（属于这个人）");
            y += 24f;
            if (owned.Count == 0)
            {
                Widgets.Label(new Rect(14f, y, viewRect.width - 12f, 22f), "（空）");
                y += 22f;
            }
            for (int i = 0; i < owned.Count; i++)
            {
                Thing t = owned[i];
                Widgets.Label(new Rect(14f, y, 250f, 22f), "· " + t.LabelNoCount + " ×" + t.stackCount);
                Widgets.Label(new Rect(268f, y, 62f, 22f), map != null ? EconomyPrice.UnitPrice(t, map).ToString("0.##") : "-");
                if (Widgets.ButtonText(new Rect(336f, y - 1f, 62f, 22f), "社会化"))
                {
                    EconomyPrivate.SetPrivate(t, null);
                }
                if (Widgets.ButtonText(new Rect(402f, y - 1f, 92f, 22f), "社会化并丢弃"))
                {
                    EconomyPrivate.SocializeAndDrop(t, pawn.MapHeld);
                }
                y += 24f;
            }

            Widgets.EndScrollView();
        }
    }
}
