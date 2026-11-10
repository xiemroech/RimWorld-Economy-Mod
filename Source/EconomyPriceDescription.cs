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
            if (true) return false;   // 已停用：价格改在右下角独立小窗显示
            // 只挂 Def 的 description：大信息卡的"描述"列读的就是它。
            // 不要再挂 Thing.DescriptionDetailed —— 它本身就包含 description，
            // 两处都挂会让价格出现两遍（这就是上一版重复的原因）。
            // 大信息卡的"描述"列读的是 Def 的 description 原文，
            // 所以只在这一个地方追加；别再挂 DescriptionDetailed（会重复两遍）。
            System.Reflection.MethodInfo md = AccessTools.PropertyGetter(typeof(ThingDef), "Description");
            if (md != null)
            {
                harmony.Patch(md, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPriceDescription), "PostfixDef")));
            }
            return true;
        }

        /// <summary>Def 版：大信息卡里只有一件的语境，所以不带整叠行。</summary>
        private static void PostfixDef(ThingDef __instance, ref string __result)
        {
            try
            {
                if (__instance == null || __instance.category != ThingCategory.Item) return;
                if (__instance == ThingDefOf.Silver || EconomyRegistry.IsCurrency(__instance)) return;
                Map map = Find.CurrentMap;
                if (map == null) return;

                float value = EconomyValue.Of(__instance, map);
                float price = EconomyPrice.UnitPrice(__instance, map);
                if (value <= 0f && price <= 0f) return;

                __result = (__result ?? "")
                           + "\n\n【现价】" + price.ToString("F2") + " 银 / 件"
                           + "　（价值 " + value.ToString("F1") + " = 原料 " + EconomyValue.MaterialCost(__instance, map).ToString("F1")
                           + " + 劳动 " + EconomyValue.LaborValue(__instance, map).ToString("F1") + "）";
            }
            catch { }
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

