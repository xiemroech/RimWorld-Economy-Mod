using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>贸易倍率悬浮窗：独立小窗口，不依赖任何贸易界面的绘制链，避免被其它 mod 的窗口盖住。</summary>
    public class Window_TradeBoard : Window
    {
        public override Vector2 InitialSize => new Vector2(360f, 210f);

        public Window_TradeBoard()
        {
            doCloseX = true;
            doCloseButton = false;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnCancel = false;
            closeOnAccept = false;
            draggable = true;
            preventCameraMotion = false;
            layer = WindowLayer.Super;
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            windowRect.x = UI.screenWidth - windowRect.width - 16f;
            windowRect.y = UI.screenHeight - windowRect.height - 16f;
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                Map map = Find.CurrentMap;
                MapEconomyComponent cfg = map != null ? map.GetComponent<MapEconomyComponent>() : null;
                if (cfg == null || !cfg.tradeLimitEnabled)
                {
                    Widgets.Label(inRect, "贸易限制：已关闭（在「经济 → 价格」页开启）");
                    return;
                }
                if (!EconomyTradeLimit.Active)
                {
                    Widgets.Label(inRect, "当前没有进行中的贸易。");
                    return;
                }

                Text.Font = GameFont.Small;
                float thrBuy = EconomyTradeLimit.Threshold(cfg);
                float thrSell = EconomyTradeLimit.SellThreshold(cfg);
                GUI.color = new Color(0.93f, 0.88f, 0.72f, 1f);
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f),
                    "边际计价　买入线 " + thrBuy.ToString("F0") + " 银");
                GUI.color = Color.white;

                float y = inRect.y + 24f;
                float traderValue = EconomyTradeLimit.TraderValue;
                GUI.color = new Color(0.75f, 0.78f, 0.82f, 1f);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "商人货值 " + traderValue.ToString("F0") + " × " + (cfg.tradeLimitRatio * 100f).ToString("0.#") + "%");
                y += 20f;
                int silverForLabel = EconomyTradeLimit.TraderSilver();
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "收购线 " + thrSell.ToString("F0") + " 银（"
                    + (silverForLabel > 0
                        ? "商人白银 " + silverForLabel + " × " + (cfg.tradeSellRatio * 100f).ToString("0.#") + "%"
                        : "读不到随身白银，按货值口径")
                    + "）");
                GUI.color = Color.white;
                y += 22f;

                float pendBuy, pendSell, baseBuy, baseSell;
                EconomyTradeLimit.PendingValues(out pendBuy, out pendSell, out baseBuy, out baseSell);

                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "已买 " + EconomyTradeLimit.BoughtValue.ToString("F0")
                    + "　待成交 原价 " + baseBuy.ToString("F0") + " → 实付 " + pendBuy.ToString("F0"));
                y += 20f;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "已卖 " + EconomyTradeLimit.SoldValue.ToString("F0")
                    + "　待成交 原价 " + baseSell.ToString("F0") + " → 实收 " + pendSell.ToString("F0"));
                y += 22f;

                y += 24f;

                float buy = EconomyTradeLimit.BuyMultiplier(cfg, baseBuy);
                float sell = EconomyTradeLimit.SellMultiplier(cfg, baseSell);
                GUI.color = buy > 1.001f ? new Color(0.95f, 0.62f, 0.45f) : Color.white;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "买入单价 ×" + buy.ToString("0.00"));
                y += 20f;
                GUI.color = sell < 0.999f ? new Color(0.95f, 0.62f, 0.45f) : Color.white;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "卖出单价 ×" + sell.ToString("0.00"));
                GUI.color = Color.white;
            }
            catch (System.Exception ex)
            {
                Log.WarningOnce("[DSH-ECONOMY] 贸易倍率窗绘制失败：" + ex.Message, 0x5D5E3);
            }
        }
    }
}
