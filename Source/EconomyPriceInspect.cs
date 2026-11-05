using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>在物品信息里直接显示当前价格（价值 = c + v+s，价格 = 价值 × 系数 × 银价 × 稀缺）。</summary>
    public static class EconomyPriceInspect
    {
        private static readonly Dictionary<string, float> priceCache = new Dictionary<string, float>();
        private static readonly Dictionary<string, float> valueCache = new Dictionary<string, float>();
        private static int cacheTick = -99999;

        public static bool Patch(Harmony harmony)
        {
            if (harmony == null) return false;
            System.Reflection.MethodInfo m = AccessTools.Method(typeof(Thing), "GetInspectString");
            if (m == null) return false;
            harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPriceInspect), "Postfix")));
            return true;
        }

        private static void Postfix(Thing __instance, ref string __result)
        {
            try
            {
                if (__instance == null) return;
                ThingDef d = __instance.def;
                if (d == null || d.category != ThingCategory.Item) return;
                if (d == ThingDefOf.Silver || EconomyRegistry.IsCurrency(d)) return;

                Map map = __instance.MapHeld ?? __instance.Map;
                if (map == null) return;

                float value, price;
                PriceOf(d, map, out value, out price);
                if (price <= 0f && value <= 0f) return;

                string line = "现价：" + price.ToString("F2") + " 银 / 件（价值 " + value.ToString("F1") + " = 原料 " +
                              EconomyValue.MaterialCost(d, map).ToString("F1") + " + 劳动 " +
                              EconomyValue.LaborValue(d, map).ToString("F1") + "）";
                if (__instance.stackCount > 1)
                {
                    line += "\n整叠 " + __instance.stackCount + " 件 ≈ " + (price * __instance.stackCount).ToString("F0") + " 银";
                }

                __result = string.IsNullOrEmpty(__result) ? line : __result + "\n" + line;
            }
            catch { }
        }

        private static void PriceOf(ThingDef d, Map map, out float value, out float price)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - cacheTick > 250)
            {
                priceCache.Clear();
                valueCache.Clear();
                cacheTick = now;
            }

            string key = d.shortHash + "|" + map.uniqueID;
            if (!priceCache.TryGetValue(key, out price))
            {
                value = EconomyValue.Of(d, map);
                price = EconomyPrice.UnitPrice(d, map);
                priceCache[key] = price;
                valueCache[key] = value;
            }
            else
            {
                valueCache.TryGetValue(key, out value);
            }
        }
    }
}
