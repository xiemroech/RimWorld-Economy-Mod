using System.Collections.Generic;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>工单账本：把"每个产品投入多少、产出多少、剩余多少"摆给玩家看，由玩家自己决定调哪门工资。</summary>
    public class Window_BillLedger : Window
    {
        private Vector2 scroll;

        public Window_BillLedger()
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
            get { return new Vector2(860f, 560f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            EconomyBillLedger ledger = map.GetComponent<EconomyBillLedger>();
            if (ledger == null) return;

            float capital = 0f;
            float surplus = 0f;
            for (int i = 0; i < ledger.records.Count; i++)
            {
                capital += ledger.records[i].inputValue + ledger.records[i].WageValue;
                surplus += ledger.records[i].Surplus;
            }
            float avg = capital > 0.01f ? surplus / capital : 0f;

            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f),
                "加权平均利润率 " + (avg * 100f).ToString("0.0") + "%"
                + "　（总资本 " + capital.ToString("0") + "　总剩余 " + surplus.ToString("0") + "）");
            Widgets.Label(new Rect(0f, 22f, inRect.width, 22f),
                "点每行右侧「调工资」可以直接改这门工种的工作权重，改完回来看利润率变化。");

            Rect outRect = new Rect(0f, 50f, inRect.width, inRect.height - 60f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 20f, Mathf.Max(outRect.height, ledger.records.Count * 32f + 32f));

            // 表头：先写清楚每列含义，否则只有数字看不出哪个是利润率
            Widgets.Label(new Rect(6f, 50f, 210f, 22f), "工单");
            Widgets.Label(new Rect(220f, 50f, 72f, 22f), "投入 c");
            Widgets.Label(new Rect(292f, 50f, 72f, 22f), "产出");
            Widgets.Label(new Rect(364f, 50f, 72f, 22f), "工资 v");
            Widgets.Label(new Rect(436f, 50f, 72f, 22f), "剩余 s");
            Widgets.Label(new Rect(508f, 50f, 90f, 22f), "利润率");
            Widgets.Label(new Rect(610f, 50f, 220f, 22f), "操作 / 工种");

            outRect = new Rect(0f, 74f, inRect.width, inRect.height - 84f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);
            float y = 0f;
            for (int i = 0; i < ledger.records.Count; i++)
            {
                BillRecord r = ledger.records[i];
                Rect row = new Rect(0f, y, viewRect.width, 30f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));

                Widgets.Label(new Rect(6f, y + 5f, 210f, 24f), r.label + (r.runs > 1 ? " ×" + r.runs : ""));
                Widgets.Label(new Rect(220f, y + 5f, 72f, 24f), r.inputValue.ToString("0.0"));
                Widgets.Label(new Rect(292f, y + 5f, 72f, 24f), r.outputValue.ToString("0.0"));
                Widgets.Label(new Rect(364f, y + 5f, 72f, 24f), r.WageValue.ToString("0.0"));
                Widgets.Label(new Rect(436f, y + 5f, 72f, 24f), r.Surplus.ToString("0.0"));

                float pr = r.ProfitRate;
                Color old = GUI.color;
                GUI.color = pr < 0f ? new Color(0.95f, 0.55f, 0.55f) : new Color(0.75f, 0.92f, 0.7f);
                Widgets.Label(new Rect(508f, y + 5f, 90f, 24f), (pr * 100f).ToString("0.0") + "%");
                GUI.color = old;

                WorkTypeDef wt = GuessWorkType(r);
                if (wt != null && Widgets.ButtonText(new Rect(610f, y + 3f, 110f, 24f), "调工资"))
                {
                    Find.WindowStack.Add(new Window_IndustryWage(wt));
                }
                if (wt != null)
                {
                    Widgets.Label(new Rect(726f, y + 5f, 110f, 24f), wt.label);
                }

                y += 32f;
            }
            Widgets.EndScrollView();
        }

        /// <summary>把一条账目猜到一个工种上（配方按工作技能，采矿/建造按前缀）。</summary>
        public static WorkTypeDef GuessWorkType(BillRecord r)
        {
            if (r == null) return null;
            if (r.label != null)
            {
                if (r.label.StartsWith("采矿")) return DefDatabase<WorkTypeDef>.GetNamedSilentFail("Mining");
                if (r.label.StartsWith("建造")) return DefDatabase<WorkTypeDef>.GetNamedSilentFail("Construction");
            }
            string defName = "Crafting";
            if (r.recipe != null && r.recipe.workSkill != null)
            {
                switch (r.recipe.workSkill.defName)
                {
                    case "Cooking": defName = "Cooking"; break;
                    case "Artistic": defName = "Art"; break;
                    case "Intellectual": defName = "Research"; break;
                    case "Medicine": defName = "Doctor"; break;
                    case "Construction": defName = "Construction"; break;
                    case "Mining": defName = "Mining"; break;
                    case "Plants": defName = "Growing"; break;
                    case "Animals": defName = "Animals"; break;
                }
            }
            return DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName);
        }
    }

    /// <summary>改某一门工种的工作权重，改完账本里的利润率会立刻变。</summary>
    public class Window_IndustryWage : Window
    {
        private readonly WorkTypeDef workType;

        public Window_IndustryWage(WorkTypeDef workType)
        {
            this.workType = workType;
            forcePause = false;
            absorbInputAroundWindow = false;
            doCloseX = true;
            closeOnClickedOutside = true;
            draggable = true;
            resizeable = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(460f, 220f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            DshEconomySettings s = DshEconomyMod.Settings;
            if (s == null || workType == null) return;

            Widgets.Label(new Rect(0f, 0f, inRect.width, 24f), "工种：" + workType.label);
            Widgets.Label(new Rect(0f, 24f, inRect.width, 44f),
                "工作权重决定这门活每小时的计价倍率。权重越高，同样工时算出的有效工时越多、工资越高，这门活的利润率就越低。");

            float v = s.GetWorkMult(workType);
            if (Widgets.ButtonText(new Rect(0f, 80f, 40f, 28f), "−"))
            {
                s.SetWorkMult(workType, v - 0.1f);
            }
            Widgets.Label(new Rect(48f, 84f, 80f, 24f), v.ToString("0.0"));
            if (Widgets.ButtonText(new Rect(132f, 80f, 40f, 28f), "+"))
            {
                s.SetWorkMult(workType, v + 0.1f);
            }
            if (Widgets.ButtonText(new Rect(190f, 80f, 90f, 28f), "恢复默认"))
            {
                s.SetWorkMult(workType, DshEconomySettings.DefaultWorkMult(workType.defName));
            }

            Widgets.Label(new Rect(0f, 120f, inRect.width, 24f),
                "当前：×" + v.ToString("0.0") + "（默认 ×" + DshEconomySettings.DefaultWorkMult(workType.defName).ToString("0.0") + "）");
        }
    }
}
