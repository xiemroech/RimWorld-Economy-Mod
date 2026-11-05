using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>把现价写进物品「描述」面板（信息卡 UI mod 也会读 DescriptionDetailed，兼容性好）。</summary>
    public static class EconomyPriceDescription
    {
        public static bool Patch(Harmony harmony)
        {
            if (harmony == null) return false;
            System.Reflection.MethodInfo m = AccessTools.PropertyGetter(typeof(Thing), "DescriptionDetailed");
            if (m == null) return false;
            harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPriceDescription), "Postfix")));
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

                float value = EconomyValue.Of(d, map);
                float price = EconomyPrice.UnitPrice(d, map);
                if (value <= 0f && price <= 0f) return;

                string line = "\n\n【现价】" + price.ToString("F2") + " 银 / 件"
                              + "　（价值 " + value.ToString("F1") + " = 原料 " + EconomyValue.MaterialCost(d, map).ToString("F1")
                              + " + 劳动 " + EconomyValue.LaborValue(d, map).ToString("F1") + "）";
                if (__instance.stackCount > 1)
                {
                    line += "\n整叠 " + __instance.stackCount + " 件 ≈ " + (price * __instance.stackCount).ToString("F0") + " 银";
                }

                __result = (__result ?? "") + line;
            }
            catch { }
        }
    }
}
