using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 经济概览窗口：分类公共化 / 商品税 两个页签。
    /// 全部使用原版控件手绘，不依赖任何贴图资源。
    /// </summary>
    public class Window_Economy : Window
    {
        private Vector2 scroll;
        private EconomyPanelMode mode = EconomyPanelMode.Categories;
        private int covered;
        private int uncovered;

        public Window_Economy()
        {
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = false;
            draggable = true;
            resizeable = false;
            forcePause = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(900f, 580f); }
        }

        public override void PreOpen()
        {
            base.PreOpen();
            covered = EconomyRegistry.CoveredCount;
            uncovered = EconomyRegistry.CountUncovered();
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map map = Find.CurrentMap;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;

            Rect header = new Rect(inRect.x, inRect.y, inRect.width, 78f);
            Widgets.DrawBoxSolid(header, new Color(0.15f, 0.15f, 0.18f, 0.9f));

            string curLabel = EconomyRegistry.PrimaryCurrency != null ? EconomyRegistry.PrimaryCurrency.label : "无";
            string line1 = "货币：" + curLabel
                + "     库存：" + (comp != null ? comp.SilverCount.ToString("N0") : "-")
                + "     银价指数：" + (comp != null ? comp.SilverIndex.ToString("0.000") : "-");
            string line2 = "商品价值：" + (comp != null ? comp.GoodsValue.ToString("N0") : "-")
                + " 银     通胀压力：" + (comp != null ? comp.InflationPressure.ToString("0.00") : "-")
                + "     本期消费：" + (comp != null ? comp.consumptionBought : 0) + " 件"
                + "     囤积浪费：" + (comp != null ? comp.consumptionWasted : 0) + " 件";
            string line3 = "已纳入经济 " + covered + " 种物品，未归类 " + uncovered
                + " 种　｜　价值 = 原料 c + 劳动 v+s，价格围绕价值波动"
                + "　｜　总社会劳动生产率 " + FormatProductivity(map);

            Widgets.Label(new Rect(header.x + 10f, header.y + 5f, header.width - 20f, 22f), line1);
            Widgets.Label(new Rect(header.x + 10f, header.y + 26f, header.width - 20f, 22f), line2);
            Widgets.Label(new Rect(header.x + 10f, header.y + 47f, header.width - 20f, 22f), line3);

            Rect panelRect = new Rect(inRect.x, inRect.y + 84f, inRect.width, inRect.height - 84f - 40f);
            if (map == null)
            {
                Widgets.Label(panelRect, "需要在殖民地地图中打开。");
            }
            else
            {
                EconomyPanel.Draw(panelRect, comp, map, ref scroll, ref mode);
            }

            Rect footer = new Rect(inRect.x, inRect.yMax - 34f, inRect.width, 30f);
            if (Widgets.ButtonText(new Rect(footer.x, footer.y, 140f, 28f), "全部恢复默认"))
            {
                if (comp != null) comp.ResetAll();
            }
            if (Widgets.ButtonText(new Rect(footer.x + 150f, footer.y, 140f, 28f), "运行自检"))
            {
                DshEconomyDebug.RunSelfTest(map);
            }
            if (Widgets.ButtonText(new Rect(footer.x + 300f, footer.y, 130f, 28f), "医疗白名单"))
            {
                Find.WindowStack.Add(new Window_MedicalWhitelist());
            }
            bool debug = DshEconomyMod.Settings != null && DshEconomyMod.Settings.debugMode;
            if (debug && Widgets.ButtonText(new Rect(footer.x + 440f, footer.y, 120f, 28f), "测试沙盒"))
            {
                Find.WindowStack.Add(new Window_TestSandbox());
            }
            if (Widgets.ButtonText(new Rect(footer.x + 570f, footer.y, 90f, 28f), "军人"))
            {
                Find.WindowStack.Add(new Window_Soldiers(map));
            }
            if (Widgets.ButtonText(new Rect(footer.x + 668f, footer.y, 90f, 28f), "搬运"))
            {
                Find.WindowStack.Add(new Window_HaulFilter(map));
            }
            if (Widgets.ButtonText(new Rect(footer.x + 766f, footer.y, 120f, 28f), "说明"))
            {
                Find.WindowStack.Add(new Dialog_MessageBox(
                    "社会化程度\n0% = 成本价 × 利润倍率（盈利价）\n50% = 原版市场价\n100% = 免费\n\n"
                    + "军人：指定脱产人员，几乎免疫动乱传染，但仍会灭火与开关。\n"
                    + "搬运：只自动搬运白名单里的物品，空列表 = 不限制。\n\n"
                    + "—— 工作满意度 ——\n"
                    + "基准 50 分，各项相加后夹在 0–100。\n\n"
                    + "【扣分】\n"
                    + "· 剩余价值抽成：工资占比低于 50% 时按缺口扣，最多 −8\n"
                    + "· 日薪上限截留：被上限截掉的比例，最多 −6\n"
                    + "· 工资欠发：国库不足没发出去的比例，最多 −6\n"
                    + "· 露天工作 −1；超时工作每小时 −1（最多 −6）\n"
                    + "· 多人宿舍 −2；贫富差距最多 −12；欠薪第 1 次 −2 起指数放大（最多 −40）\n"
                    + "· 营养不良 −3/−6/−12；力竭 −8；伤痛每处 −2（最多 −10）；疼痛最多 −6\n"
                    + "· 情绪传染 −2/次；道德指责 −1；繁复的寻找 −2\n\n"
                    + "【加分】\n"
                    + "· 超额分配：工资占比高于 50%，最多 +8\n"
                    + "· 工资足额 +3；实发超过该发再 +3~+6（加班费）\n"
                    + "· 长期工作保证：连续按时发薪 3/10/20/30 期 → +1/+2/+3/+4\n"
                    + "· 短工时：不满 8 小时按缺口给，最多 +6\n"
                    + "· 居住环境 +1~+4；工作间等级 +1~+3\n"
                    + "· 私有财产 500/1500/4000/10000 银 → +1/+2/+3/+4\n"
                    + "· 公共福利：基础消费品社会化 75% 起算线性给分，满 100% 时 +10（最近买不到东西则失效）\n"
                    + "· 集体供给：关闭发薪时 +8（同样要求需求被满足）\n"
                    + "· 人人有饭吃：食物 100% 社会化且无人营养不良 +3\n"
                    + "· 较为平均：财富基尼 0.80→0.20 线性 +0~+4（两套制度都有）\n"
                    + "· 无阶级差别：财富基尼 < 0.20 +5\n"
                    + "· 劳动光荣：九成以上的人本期都在劳动 +3\n"
                    + "· 医疗保障 +1~+2；工作与兴趣匹配 +1~+2；配偶 +2 / 亲友 +1\n"
                    + "· 心情/睡眠/娱乐 >80% 各 +2；吃得好（家常菜以上）+2\n"
                    + "· 食物储备 5/10 天 → +1/+2；温度舒适 +1；免费教育 +1；公共食堂 +1\n"
                    + "· 服装整洁 +1；工作被认可 +1；信仰一致按同信仰比例 +1~+4\n\n"
                    + "低于 30 可能罢工，低于 10 暴动，低于 4 革命。"));
            }
        }

        /// <summary>
        /// 总社会劳动生产率：产出价值 ÷ 投入人日，单位"银/人日"。
        /// 样本不足（< 3 件）显示"—"，避免开局给出误导性数字。
        /// </summary>
        private static string FormatProductivity(Map map)
        {
            if (map == null) return "—";
            float v = EconomyProductionStats.SocialLaborProductivity(map);
            if (v < 0f) return "—（样本不足）";
            return v.ToString("0.0") + " 银/人日";
        }
    }

    public class MainButtonWorker_Economy : MainButtonWorker
    {
        public override void Activate()
        {
            Find.WindowStack.Add(new Window_Economy());
        }

        // 不重写 DoButton：交给原版画，底板/颜色/悬停都和「研究」「任务」一致。
        // 之前自己画背景才出现了颜色不搭的问题。
    }
}
