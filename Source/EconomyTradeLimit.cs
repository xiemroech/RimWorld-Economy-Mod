using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 外部贸易限制：按“本次访问已成交的金额”累进。
    /// 原版价格不动，只对超过阈值的部分加惩罚单价：
    ///   买入（玩家向商人买）超过阈值后价格翻倍，每多一档再翻一倍；
    ///   卖出（玩家卖给商人）超过阈值后价格打骨折，每多一档再打一次折。
    /// 状态按“商人 + 一次交易窗口”重置，不写档案（贸易是瞬时行为）。
    /// </summary>
    public static class EconomyTradeLimit
    {
        public class Session
        {
            public ITrader trader;
            public float boughtValue;
            public float soldValue;
            public float traderValue;
            public int mapId;
        }

        private static Session current;

        public static Session Current => current;
        public static bool Active => current != null;

        public static float BoughtValue => current?.boughtValue ?? 0f;
        public static float SoldValue => current?.soldValue ?? 0f;
        public static float TraderValue { get { RefreshTrader(); return current?.traderValue ?? 0f; } }

        /// <summary>商人随身的白银（原版商队用它收购玩家的东西）。-1 = 这个商人类型读不到。</summary>
        public static int TraderSilver()
        {
            try
            {
                // 不要直接读 deal.CurrencyTradeable：没有货币条目时原版会在 getter 里
                // 打 ERROR "RimWorld.Tradeable lacks AnyThing."，而我们每次询价都会读一次，把日志刷爆。
                // 所以先用反射拿 tradeables，自己找 IsCurrency 的那条。
                TradeDeal deal = TradeSession.deal;
                Tradeable currency = FindCurrencyTradeable(deal);
                if (currency != null)
                {
                    int n = currency.CountHeldBy(Transactor.Trader);
                    if (n >= 0) return n;
                }
                Pawn p = TradeSession.trader as Pawn;
                if (p == null) return -1;
                int sum = 0;
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (t.def == ThingDefOf.Silver) sum += t.stackCount;
                }
                return sum;
            }
            catch { return -1; }
        }

        private static System.Reflection.FieldInfo tradeablesField;
        private static bool tradeablesFieldLooked;

        /// <summary>从 TradeDeal 的 tradeables 里找货币条目，绕过原版那个会报错的 getter。</summary>
        private static Tradeable FindCurrencyTradeable(TradeDeal deal)
        {
            if (deal == null) return null;
            if (!tradeablesFieldLooked)
            {
                tradeablesFieldLooked = true;
                tradeablesField = HarmonyLib.AccessTools.Field(typeof(TradeDeal), "tradeables");
            }
            if (tradeablesField == null) return null;
            System.Collections.IList list = tradeablesField.GetValue(deal) as System.Collections.IList;
            if (list == null) return null;
            for (int i = 0; i < list.Count; i++)
            {
                Tradeable t = list[i] as Tradeable;
                if (t != null && t.IsCurrency) return t;
            }
            return null;
        }

        public static void Begin(ITrader trader)
        {
            current = new Session { trader = trader, boughtValue = 0f, soldValue = 0f };
            try { current.traderValue = trader == null ? 0f : TradeUtility.TotalMarketValue(trader.Goods); }
            catch { current.traderValue = 0f; }
            OpenBoard();
        }

        public static void End()
        {
            current = null;
            CloseBoard();
        }

        private static Window_TradeBoard board;

        private static bool inPendingCalc;
        internal static bool suppressPrice;

        /// <summary>购物车里还没成交的金额（含已选数量），用于面板实时显示。</summary>
        public static void PendingValues(out float buy, out float sell)
        {
            PendingValues(out buy, out sell, out float baseBuy, out float baseSell);
        }

        /// <summary>加价/折价前后的对比金额：原价按“不打补丁”的单价算。</summary>
        public static void PendingValues(out float buy, out float sell, out float baseBuy, out float baseSell)
        {
            buy = 0f;
            sell = 0f;
            baseBuy = 0f;
            baseSell = 0f;
            if (current == null) return;
            if (inPendingCalc) return;
            inPendingCalc = true;
            try
            {
                TradeDeal deal = TradeSession.deal;
                if (deal == null) return;
                List<Tradeable> all = deal.AllTradeables;
                if (all == null) return;
                for (int i = 0; i < all.Count; i++)
                {
                    Tradeable t = all[i];
                    if (t == null || t.IsCurrency || t.CountToTransfer == 0) continue;
                    float price;
                    try { price = t.GetPriceFor(t.ActionToDo); }
                    catch { continue; }
                    float basePrice;
                    try
                    {
                        suppressPrice = true;
                        basePrice = t.GetPriceFor(t.ActionToDo);
                    }
                    catch { basePrice = price; }
                    finally { suppressPrice = false; }
                    float b = UnityEngine.Mathf.Abs(basePrice * t.CountToTransfer);
                    if (t.ActionToDo == TradeAction.PlayerBuys) baseBuy += b;
                    else if (t.ActionToDo == TradeAction.PlayerSells) baseSell += b;
                }

                // 整辆购物车共用同一档位：原价合计越过收购/买入线后，全部按同一乘数结算
                cachedBaseBuy = baseBuy;
                cachedBaseSell = baseSell;
                cachedTick = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

                Map map = null;
                try { map = TradeSession.playerNegotiator?.MapHeld ?? Find.CurrentMap; }
                catch { map = Find.CurrentMap; }
                MapEconomyComponent cfg = ConfigOf(map);
                if (cfg != null && cfg.tradeLimitEnabled)
                {
                    buy = baseBuy * BuyMultiplier(cfg, baseBuy);
                    sell = baseSell * SellMultiplier(cfg, baseSell);
                }
                else
                {
                    buy = baseBuy;
                    sell = baseSell;
                }
            }
            catch (System.Exception ex) { EcoLog.WarnOnce("EconomyTradeLimit", ex); }
            finally { inPendingCalc = false; }
        }

        private static float cachedBaseBuy;
        private static float cachedBaseSell;
        private static int cachedTick = -99999;

        /// <summary>整辆购物车的原价合计快照（最多 30 tick 更新一次），供单价补丁判断档位。</summary>
        private static void EnsureCartSnapshot()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (cachedTick >= 0 && now - cachedTick <= 30) return;
            cachedTick = now;
            PendingValues(out float b, out float s);
        }

        public static float CartBase( TradeAction action )
        {
            EnsureCartSnapshot();
            return action == TradeAction.PlayerBuys ? cachedBaseBuy : cachedBaseSell;
        }

        public static void OpenBoard()
        {
            try
            {
                if (Find.WindowStack == null) return;
                if (board != null && Find.WindowStack.IsOpen(board)) return;
                board = new Window_TradeBoard();
                Find.WindowStack.Add(board);
            }
            catch (System.Exception ex) { Log.WarningOnce("[DSH-ECONOMY] 打开贸易倍率窗失败：" + ex.Message, 0x5D5E4); }
        }

        public static void CloseBoard()
        {
            try
            {
                if (board != null && Find.WindowStack != null) Find.WindowStack.TryRemove(board, false);
            }
            catch (System.Exception ex) { EcoLog.WarnOnce("EconomyTradeLimit", ex); }
            board = null;
        }

        /// <summary>把一次成交按成交前的价格折成银额记进本次访问。</summary>
        public static void RecordTrade(TradeDeal deal)
        {
            if (current == null || deal == null) return;
            List<Tradeable> all = deal.AllTradeables;
            if (all == null) return;
            for (int i = 0; i < all.Count; i++)
            {
                Tradeable t = all[i];
                if (t == null || t.IsCurrency || t.CountToTransfer == 0) continue;
                float price;
                try { price = t.GetPriceFor(t.ActionToDo); }
                catch { continue; }
                if (price <= 0f) continue;
                float value = price * t.CountToTransfer;
                if (value < 0f) value = -value;
                if (t.ActionToDo == TradeAction.PlayerBuys) current.boughtValue += value;
                else if (t.ActionToDo == TradeAction.PlayerSells) current.soldValue += value;
            }
        }

        public static int TierOf(float value, float threshold)
        {
            if (threshold <= 0f || value < threshold) return 0;
            return (int)(value / threshold);
        }


        /// <summary>涨价线 = max(下限, 商人携带货值 × 比例)。商人越富，一次能消化的额度越大。</summary>
        /// <summary>交易会话的商人在 PreOpen 时可能还没写进 TradeSession，这里按需补齐。</summary>
        public static void RefreshTrader()
        {
            if (current == null) return;
            if (current.trader != null && current.traderValue > 0f) return;
            try
            {
                ITrader t = TradeSession.trader;
                if (t == null) return;
                current.trader = t;
                current.traderValue = TradeUtility.TotalMarketValue(t.Goods);
            }
            catch (System.Exception ex) { EcoLog.WarnOnce("EconomyTradeLimit", ex); }
        }

        /// <summary>涨价线 = 商人携带货值 × 比例（没有下限）。读不到商人货值时退回设置值。</summary>
        public static float Threshold(MapEconomyComponent cfg)
        {
            if (cfg == null) return 0f;
            RefreshTrader();
            float traderValue = TraderValue;
            float dyn = traderValue * UnityEngine.Mathf.Max(0f, cfg.tradeLimitRatio);
            if (dyn < 1f) dyn = cfg.tradeLimitValue;      // 商人货值读不到时的兜底
            if (dyn < 1f) dyn = 1f;
            return UnityEngine.Mathf.Clamp(dyn, 1f, 1e7f);
        }

        /// <summary>收购（玩家卖出）的涨价线：商人身上有多少白银，就能按原价收多少货；超出部分才骨折。</summary>
        public static float SellThreshold(MapEconomyComponent cfg)
        {
            if (cfg == null) return 0f;
            int silver = TraderSilver();
            float ratio = UnityEngine.Mathf.Clamp(cfg.tradeSellRatio, 0.01f, 1f);
            if (silver > 0) return UnityEngine.Mathf.Clamp(silver * ratio, 1f, 1e7f);
            // 读不到商人白银（轨道商船等）时退回“商人货值 × 比例”
            return Threshold(cfg);
        }

        /// <summary>
        /// 边际计价：只有超出涨价线的部分才涨价/折价，返回这一笔的平均单价。
        /// 累计值按基价推进，避免“涨价部分再涨价”的复利。
        /// </summary>
        public static float MarginalUnitPrice(float basePrice, float alreadyValue, int count, float threshold, float factor, bool buy)
        {
            if (count <= 0 || threshold <= 0f || basePrice <= 0f) return basePrice;
            float total = 0f;
            float cursor = UnityEngine.Mathf.Max(0f, alreadyValue);
            for (int i = 0; i < count; i++)
            {
                int tier = (int)(cursor / threshold);
                if (tier < 0) tier = 0;
                if (tier > 20) tier = 20;                 // 档位封顶，避免 2^n 溢出成 Infinity
                float unit = basePrice * UnityEngine.Mathf.Pow(factor, tier);
                if (float.IsNaN(unit) || float.IsInfinity(unit)) unit = basePrice;
                if (unit < 0.01f) unit = 0.01f;
                total += unit;
                cursor += basePrice;
            }
            float avg = total / count;
            if (float.IsNaN(avg) || float.IsInfinity(avg) || avg <= 0f) return basePrice;
            avg = UnityEngine.Mathf.Clamp(avg, basePrice * 0.02f, basePrice * 100f);
            return buy ? UnityEngine.Mathf.Max(avg, basePrice) : UnityEngine.Mathf.Min(avg, basePrice);
        }
        /// <summary>
        /// 边际累进：超出阈值的部分按“每再超出 1 个阈值就再乘一次 factor”累加，
        /// 所以超出越多，平均乘数越陡（卖出越卖越贱，买入越买越贵）。
        /// </summary>
        private static float MarginalFactor(float total, float threshold, float factor)
        {
            if (threshold <= 0f || factor <= 0f) return 1f;
            if (total <= threshold) return 1f;
            float paid = threshold;
            float remaining = total - threshold;
            int step = 1;
            while (remaining > 0.001f && step <= 20)
            {
                float chunk = UnityEngine.Mathf.Min(remaining, threshold);
                paid += chunk * UnityEngine.Mathf.Pow(factor, step);
                remaining -= chunk;
                step++;
            }
            if (remaining > 0.001f) paid += remaining * UnityEngine.Mathf.Pow(factor, 20);
            if (float.IsNaN(paid) || float.IsInfinity(paid) || paid <= 0f) return 1f;
            return paid / total;
        }

        public static float BuyMultiplier(MapEconomyComponent cfg)
        {
            return BuyMultiplier(cfg, 0f);
        }

        public static float SellMultiplier(MapEconomyComponent cfg)
        {
            return SellMultiplier(cfg, 0f);
        }
        /// <summary>pendingValue = total base price of the current cart.</summary>
        /// <summary>pendingValue = total base price of the current cart.</summary>
        public static float BuyMultiplier(MapEconomyComponent cfg, float pendingValue)
        {
            if (cfg == null || !cfg.tradeLimitEnabled || current == null) return 1f;
            float total = current.boughtValue + UnityEngine.Mathf.Max(0f, pendingValue);
            float f = MarginalFactor(total, Threshold(cfg), UnityEngine.Mathf.Max(1.05f, cfg.tradeBuyMult));
            return UnityEngine.Mathf.Clamp(f, 0.1f, 50f);
        }

        public static float SellMultiplier(MapEconomyComponent cfg, float pendingValue)
        {
            if (cfg == null || !cfg.tradeLimitEnabled || current == null) return 1f;
            float total = current.soldValue + UnityEngine.Mathf.Max(0f, pendingValue);
            float f = MarginalFactor(total, SellThreshold(cfg), UnityEngine.Mathf.Clamp(cfg.tradeSellMult, 0.01f, 1f));
            return UnityEngine.Mathf.Clamp(f, 0.02f, 1f);
        }

        public static MapEconomyComponent ConfigOf(Map map)
        {
            return map == null ? null : map.GetComponent<MapEconomyComponent>();
        }

        public static string Describe(Map map)
        {
            MapEconomyComponent cfg = ConfigOf(map);
            if (cfg == null) return "";
            if (!cfg.tradeLimitEnabled) return "贸易限制：已关闭";
            float thr = Threshold(cfg);
            return "贸易限制：本次已买 " + BoughtValue.ToString("F0") + " 银 · 已卖 " + SoldValue.ToString("F0")
                   + " 银 · 当前涨价线 " + thr.ToString("F0") + " 银（商品总值 × "
                   + (cfg.tradeLimitRatio * 100f).ToString("0.#") + "%；读不到商人货值时用 " + cfg.tradeLimitValue.ToString("F0") + "）";
        }

        public static class Patches
        {
            public static void BeginPostfix(ITrader trader)
            {
                Begin(trader);
            }

            public static void ClosePostfix()
            {
                End();
            }

            public static void ExecutePrefix(TradeDeal __instance)
            {
                RecordTrade(__instance);
            }

            public static void PricePostfix(Tradeable __instance, TradeAction action, ref float __result)
            {
                if (inPendingCalc || suppressPrice) return;
                if (action == TradeAction.None || __result <= 0f) return;
                if (__instance.IsCurrency) return;
                Map map = null;
                try { map = TradeSession.playerNegotiator?.MapHeld ?? Find.CurrentMap; }
                catch { map = Find.CurrentMap; }
                MapEconomyComponent cfg = ConfigOf(map);
                if (cfg == null || !cfg.tradeLimitEnabled) return;
                int pendingCount = __instance.CountToTransfer;
                if (pendingCount < 0) pendingCount = -pendingCount;
                if (pendingCount <= 0) return;

                // 整辆购物车按同一个档位结算：原价合计越过阈值后，全部商品一起加价/折价
                float cart = CartBase(action);
                if (action == TradeAction.PlayerBuys)
                {
                    __result *= BuyMultiplier(cfg, cart);
                }
                else
                {
                    __result *= SellMultiplier(cfg, cart);
                    if (__result < TradeUtility.MinimumSellPrice) __result = TradeUtility.MinimumSellPrice;
                }
            }

            public static bool Patch(Harmony h)
            {
                bool ok = true;
                System.Reflection.MethodInfo pre = AccessTools.Method(typeof(Dialog_Trade), "PreOpen");
                if (pre != null)
                {
                    h.Patch(pre, postfix: new HarmonyMethod(typeof(Patches), nameof(DialogBeginPostfix)));
                }
                else { ok = false; Log.Warning("[DSH-ECONOMY] 贸易限制：未找到 Dialog_Trade.PreOpen"); }

                System.Reflection.MethodInfo close = AccessTools.Method(typeof(Dialog_Trade), "Close", new[] { typeof(bool) });
                if (close != null)
                {
                    h.Patch(close, postfix: new HarmonyMethod(typeof(Patches), nameof(DialogClosePostfix)));
                }
                else { ok = false; Log.Warning("[DSH-ECONOMY] 贸易限制：未找到 Dialog_Trade.Close"); }

                System.Reflection.MethodInfo exec = AccessTools.Method(typeof(TradeDeal), "TryExecute", new[] { typeof(bool).MakeByRefType() });
                if (exec != null)
                {
                    h.Patch(exec, prefix: new HarmonyMethod(typeof(Patches), nameof(ExecutePrefix)));
                }
                else { ok = false; Log.Warning("[DSH-ECONOMY] 贸易限制：未找到 TradeDeal.TryExecute"); }

                System.Reflection.MethodInfo draw = AccessTools.Method(typeof(RimWorld.UIRoot_Play), "UIRootOnGUI");
                if (draw != null)
                {
                    h.Patch(draw, postfix: new HarmonyMethod(typeof(Patches), nameof(UiRootPostfix)));
                }
                else { ok = false; Log.Warning("[DSH-ECONOMY] 贸易限制：未找到 UIRoot_Play.UIRootOnGUI"); }

                System.Reflection.MethodInfo win = AccessTools.Method(typeof(Dialog_Trade), "DoWindowContents", new[] { typeof(UnityEngine.Rect) });
                if (win != null)
                {
                    h.Patch(win, postfix: new HarmonyMethod(typeof(Patches), nameof(DialogContentsPostfix)));
                }
                else { ok = false; Log.Warning("[DSH-ECONOMY] 贸易限制：未找到 Dialog_Trade.DoWindowContents"); }

                System.Reflection.MethodInfo price = AccessTools.Method(typeof(Tradeable), "GetPriceFor", new[] { typeof(TradeAction) });
                if (price != null)
                {
                    h.Patch(price, postfix: new HarmonyMethod(typeof(Patches), nameof(PricePostfix)));
                }
                else { ok = false; Log.Warning("[DSH-ECONOMY] 贸易限制：未找到 Tradeable.GetPriceFor"); }
                return ok;
            }

            private static void DialogBeginPostfix(Dialog_Trade __instance)
            {
                ITrader t = null;
                try { t = TradeSession.trader; } catch (System.Exception ex) { EcoLog.WarnOnce("EconomyTradeLimit", ex); }
                Begin(t);
                try
                {
                    ITrader tr = TradeSession.trader;
                    if (tr != null)
                    {
                        int goods = 0;
                        foreach (Thing th in tr.Goods) goods++;
                        int willing = 0;
                        try { foreach (Thing th in tr.ColonyThingsWillingToBuy(TradeSession.playerNegotiator)) willing++; } catch { willing = -1; }
                        var sample = new System.Text.StringBuilder();
                        int shown = 0;
                        try
                        {
                            foreach (Thing th in tr.ColonyThingsWillingToBuy(TradeSession.playerNegotiator))
                            {
                                if (shown++ < 12) sample.Append(th.def.defName).Append(' ');
                            }
                        }
                        catch (System.Exception ex) { EcoLog.WarnOnce("EconomyTradeLimit", ex); }
                        int mapThings = 0, haulable = 0;
                        try
                        {
                            Map m = Find.CurrentMap;
                            if (m != null)
                            {
                                foreach (Thing th in m.listerThings.AllThings)
                                {
                                    mapThings++;
                                    if (th.def.category == ThingCategory.Item) haulable++;
                                }
                            }
                        }
                        catch (System.Exception ex) { EcoLog.WarnOnce("EconomyTradeLimit", ex); }
                        DevLog.Msg("[TRADE] 商人=" + tr.TraderKind?.defName + " 货物=" + goods
                            + " 愿意收购=" + willing + " 白银=" + TraderSilver()
                            + " 地图物品=" + mapThings + " 其中可堆物品=" + haulable
                            + " 样本=[" + sample + "]");
                    }
                }
                catch (System.Exception ex) { EcoLog.WarnOnce("EconomyTradeLimit", ex); }
                DevLog.Msg("[DSH-ECONOMY] 贸易限制：本次访问开始（" + (t == null ? "未知商人" : (t.TraderKind != null ? t.TraderKind.LabelCap.ToString() : "商人")) + "）");
            }

            private static void DialogContentsPostfix(UnityEngine.Rect inRect)
            {
                try
                {
                    if (!Active) return;
                    UnityEngine.Rect r = new UnityEngine.Rect(inRect.xMax - 342f, inRect.yMax - 128f, 330f, 118f);
                    DrawOverlay(r);
                    if (!drewOnce)
                    {
                        drewOnce = true;
                        DevLog.Msg("[DSH-ECONOMY] 贸易倍率悬浮窗（窗口内）：已绘制");
                    }
                }
                catch (System.Exception ex) { Log.WarningOnce("[DSH-ECONOMY] 贸易倍率悬浮窗（窗口内）绘制失败：" + ex.Message, 0x5D5E2); }
            }

            private static bool drewOnce;

            private static void UiRootPostfix()
            {
                try
                {
                    if (!Active) return;
                    if (!TradeSession.Active) return;
                    if (Find.WindowStack == null) return;
                    UnityEngine.Rect r = new UnityEngine.Rect(12f, UnityEngine.Screen.height - 132f, 330f, 118f);
                    DrawOverlay(r);
                }
                catch (System.Exception ex) { Log.WarningOnce("[DSH-ECONOMY] 贸易倍率悬浮窗绘制失败：" + ex.Message, 0x5D5E1); }
            }

            private static void DrawOverlay(UnityEngine.Rect r)
            {
                if (!Active) return;
                Map map = Find.CurrentMap;
                MapEconomyComponent cfg = ConfigOf(map);
                if (cfg == null || !cfg.tradeLimitEnabled) return;
                float w = r.width;
                float h = r.height;
                Widgets.DrawBoxSolid(r, new UnityEngine.Color(0.10f, 0.11f, 0.13f, 0.92f));
                Widgets.DrawBox(r, 1);

                GameFont old = Text.Font;
                Text.Font = GameFont.Small;
                UnityEngine.Color oldColor = UnityEngine.GUI.color;
                UnityEngine.GUI.color = new UnityEngine.Color(0.93f, 0.88f, 0.72f, 1f);
                float thr = Threshold(cfg);
                Widgets.Label(new UnityEngine.Rect(r.x + 8f, r.y + 4f, w - 16f, 22f),
                    "边际涨价　涨价线 " + thr.ToString("F0") + " 银（商品总值×" + (cfg.tradeLimitRatio * 100f).ToString("0.#") + "%）");
                UnityEngine.GUI.color = UnityEngine.Color.white;

                float buy = BuyMultiplier(cfg);
                float sell = SellMultiplier(cfg);
                Widgets.Label(new UnityEngine.Rect(r.x + 8f, r.y + 28f, w - 16f, 22f),
                    "本次已买 " + BoughtValue.ToString("F0") + " 银　单价 ×" + buy.ToString("F2")
                    + "　（第 " + TierOf(BoughtValue, thr) + " 档）");
                Widgets.Label(new UnityEngine.Rect(r.x + 8f, r.y + 50f, w - 16f, 22f),
                    "本次已卖 " + SoldValue.ToString("F0") + " 银　单价 ×" + sell.ToString("F2")
                    + "　（第 " + TierOf(SoldValue, thr) + " 档）");
                UnityEngine.GUI.color = new UnityEngine.Color(0.72f, 0.72f, 0.72f, 1f);
                Widgets.Label(new UnityEngine.Rect(r.x + 8f, r.y + 74f, w - 16f, 30f),
                    "超出涨价线的部分才变价：买入 ×" + cfg.tradeBuyMult.ToString("F2") + "^档，卖出 ×" + cfg.tradeSellMult.ToString("F2") + "^档");
                UnityEngine.GUI.color = oldColor;
                Text.Font = old;
            }

            private static void DialogClosePostfix()
            {
                End();
            }
        }
    }
}

