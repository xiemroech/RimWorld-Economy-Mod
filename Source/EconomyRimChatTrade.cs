using System.Reflection;
using HarmonyLib;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// RimChat（Word to Actions）的物资空投交易：它显示的是「预期价格/参考总价」，
    /// 实际成交由 AI 交涉决定。这里只改它的预期总价公式，把边际涨价/折价叠进去。
    /// 找不到 RimChat 时整段静默跳过。
    /// </summary>
    public static class EconomyRimChatTrade
    {
        private const string CardType = "RimChat.UI.Dialog_ItemAirdropTradeCard";

        /// <summary>按值分段累加：前 thr 按原价，之后每段乘 factor（买 up / 卖 down）。</summary>
        public static float ApplyMarginal(float total, float thr, float factor, bool buy)
        {
            if (total <= 0f || thr <= 0f) return total;
            if (total <= thr) return total;
            float result = 0f;
            float left = total;
            int tier = 0;
            while (left > 0.0001f)
            {
                float seg = UnityEngine.Mathf.Min(left, thr);
                float f = UnityEngine.Mathf.Pow(factor, tier);
                result += seg * (buy ? UnityEngine.Mathf.Max(1f, f) : UnityEngine.Mathf.Min(1f, f));
                left -= seg;
                tier++;
                if (tier > 200) break;
            }
            return result;
        }

        public static float Adjust(float total, Map map, bool buy)
        {
            MapEconomyComponent cfg = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (cfg == null || !cfg.tradeLimitEnabled) return total;
            float thr = UnityEngine.Mathf.Max(1f, cfg.tradeLimitValue);
            float factor = buy
                ? UnityEngine.Mathf.Max(1.05f, cfg.tradeBuyMult)
                : UnityEngine.Mathf.Clamp(cfg.tradeSellMult, 0.01f, 1f);
            return ApplyMarginal(total, thr, factor, buy);
        }

        private static int lastLogTick = -9999;
        private static string lastLogKey;

        private static void LogOnce(string dir, float before, float after)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            string key = dir + "|" + before.ToString("F0") + "|" + after.ToString("F0");
            if (key == lastLogKey && now - lastLogTick < 600) return;
            lastLogKey = key;
            lastLogTick = now;
            DevLog.Msg("[DSH-ECONOMY] RimChat 预期总价（" + dir + "）：" + before.ToString("F0") + " → " + after.ToString("F0"));
        }

        public static class Patches
        {
            // 玩家买入：参考总价（我方要付的）
            public static void NeedTotalPostfix(ref float __result)
            {
                try
                {
                    float before = __result;
                    __result = Adjust(__result, Find.CurrentMap, true);
                    if (before != __result) LogOnce("买", before, __result);
                }
                catch { }
            }

            // 玩家卖出：出价总价（对方要付的）
            public static void OfferTotalPostfix(ref float __result)
            {
                try
                {
                    float before = __result;
                    __result = Adjust(__result, Find.CurrentMap, false);
                    if (before != __result) LogOnce("卖", before, __result);
                }
                catch { }
            }

            public static bool Patch(Harmony h)
            {
                System.Type card = AccessTools.TypeByName(CardType);
                if (card == null) return false;
                bool ok = false;

                MethodInfo need = AccessTools.Method(card, "ComputeNeedReferenceTotal", System.Type.EmptyTypes);
                if (need != null)
                {
                    h.Patch(need, postfix: new HarmonyMethod(typeof(Patches), nameof(NeedTotalPostfix)));
                    ok = true;
                }

                MethodInfo offer = AccessTools.Method(card, "ComputeOfferTotal", System.Type.EmptyTypes);
                if (offer != null)
                {
                    h.Patch(offer, postfix: new HarmonyMethod(typeof(Patches), nameof(OfferTotalPostfix)));
                    ok = true;
                }
                return ok;
            }
        }
    }
}

