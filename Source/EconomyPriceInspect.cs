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
            if (true) return false;   // 已停用：价格改在右下角独立小窗显示
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
                PriceOf(__instance, map, out value, out price);
                if (price <= 0f && value <= 0f) return;

                // 饭菜这类有"实际用料"的东西，提示框要显示真实原料，而不是配方平均值
                float c = EconomyValue.MaterialCost(d, map);
                float lab = EconomyValue.LaborValue(d, map);
                float realIn = EconomyValue.RealIngredientCost(__instance);
                if (realIn > 0.01f)
                {
                    c = realIn;
                    value = realIn + lab;
                }

                string line = "现价：" + price.ToString("F2") + " 银 / 件（价值 " + value.ToString("F1") + " = 原料 " +
                              c.ToString("F1") + " + 劳动 " +
                              lab.ToString("F1") + "）";
                if (__instance.stackCount > 1)
                {
                    line += "\n整叠 " + __instance.stackCount + " 件 ≈ " + (price * __instance.stackCount).ToString("F0") + " 银";
                }

                __result = string.IsNullOrEmpty(__result) ? line : __result + "\n" + line;
            }
            catch { }
        }

        /// <summary>价格必须按"这一件"算：同一 def 的饭，用料不同价不同。</summary>
        private static void PriceOf(Thing thing, Map map, out float value, out float price)
        {
            ThingDef d = thing.def;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - cacheTick > 250)
            {
                valueCache.Clear();
                cacheTick = now;
            }

            string key = d.shortHash + "|" + map.uniqueID;
            if (!valueCache.TryGetValue(key, out value))
            {
                value = EconomyValue.Of(d, map);
                valueCache[key] = value;
            }

            price = EconomyPrice.UnitPrice(thing, map);
        }
    }
}

