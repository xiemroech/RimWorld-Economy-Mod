using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>医用植入物（仿生体/血清/基因/器官）：付不起现价就不能做手术 / 不能用。</summary>
    public static class EconomyAugmentGate
    {
        public static bool Patch(Harmony harmony)
        {
            if (harmony == null) return false;
            MethodInfo m = AccessTools.Method(typeof(RecipeWorker), "AvailableReport", new[] { typeof(Thing), typeof(BodyPartRecord) });
            if (m == null) return false;
            harmony.Patch(m, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyAugmentGate), "Postfix")));
            return true;
        }

        /// <summary>这次手术要花多少钱（按配方原料的现价合计）。</summary>
        public static int PriceOf(RecipeDef recipe, Map map)
        {
            if (recipe == null || map == null || recipe.ingredients == null) return 0;
            float sum = 0f;
            for (int i = 0; i < recipe.ingredients.Count; i++)
            {
                IngredientCount ing = recipe.ingredients[i];
                if (ing == null || ing.filter == null) continue;
                ThingDef d = ing.filter.AnyAllowedDef;
                if (d == null) continue;
                sum += EconomyPrice.UnitPrice(d, map) * ing.GetBaseCount();
            }
            return UnityEngine.Mathf.RoundToInt(sum);
        }

        public static void Postfix(RecipeWorker __instance, Thing thing, ref AcceptanceReport __result)
        {
            try
            {
                if (__result == null || !__result.Accepted) return;
                Pawn pawn = thing as Pawn;
                if (pawn == null || !pawn.IsColonist || pawn.Map == null) return;

                RecipeDef recipe = __instance != null ? __instance.recipe : null;
                if (recipe == null || recipe.ingredients == null) return;

                bool isAugment = false;
                for (int i = 0; i < recipe.ingredients.Count; i++)
                {
                    ThingDef d = recipe.ingredients[i]?.filter?.AnyAllowedDef;
                    EconomyCategoryDef cat = d != null ? EconomyRegistry.CategoryOf(d) : null;
                    if (cat != null && cat.defName == "DshEco_Augment") { isAugment = true; break; }
                }
                if (!isAugment) return;

                int price = PriceOf(recipe, pawn.Map);
                if (price <= 0) return;

                int silver = EconomyBank.PrivateSilver(pawn.Map, pawn);
                if (silver < price)
                {
                    __result = new AcceptanceReport("付不起：需要 " + price + " 银，个人只有 " + silver + " 银");
                }
            }
            catch { }
        }

        /// <summary>手术完成后结算，把钱从病人私银转到殖民地。</summary>
        public static void Charge(Pawn patient, RecipeDef recipe)
        {
            if (patient == null || patient.Map == null || recipe == null) return;
            int price = PriceOf(recipe, patient.Map);
            if (price <= 0) return;
            int paid = EconomyBank.TakePrivateSilver(patient.Map, patient, price);
            Log.Message("[DSH-ECONOMY] 植入手术结算：" + patient.LabelShort + " 付 " + paid + " / " + price + " 银");
        }
    }
}
