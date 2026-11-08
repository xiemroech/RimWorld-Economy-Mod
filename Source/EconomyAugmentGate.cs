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

                // 新机制：钱在"购买"那一刻就付过了，手术阶段不再收费，也不再拦"付不起"。
                // 这里只做一件事：如果他背包里带着私有的对应植入体，把它放到脚边，
                // 否则原版手术取料只认地面/仓库，会把"有货"当成"没货"。
                if (OwnsImplant(pawn, recipe)) DropForSurgery(pawn, recipe);
            }
            catch { }
        }

        /// <summary>
        /// 手术要用的植入体如果在他背包里，原版的手术取料只认地面/仓库，会把"有货"当成"没货"。
        /// 所以在可行性检查这一步把它放到脚边（并解除禁用）。
        /// </summary>
        public static void DropForSurgery(Pawn pawn, RecipeDef recipe)
        {
            try
            {
                if (pawn == null || pawn.Map == null || recipe == null || recipe.ingredients == null) return;
                if (pawn.inventory == null || pawn.inventory.innerContainer == null) return;

                for (int i = 0; i < recipe.ingredients.Count; i++)
                {
                    IngredientCount ing = recipe.ingredients[i];
                    if (ing == null || ing.filter == null) continue;
                    ThingDef d = ing.filter.AnyAllowedDef;
                    if (d == null || !EconomyConsumption.IsImplantLike(d)) continue;

                    IList<Thing> inner = pawn.inventory.innerContainer;
                    for (int j = inner.Count - 1; j >= 0; j--)
                    {
                        Thing t = inner[j];
                        if (t == null || t.Destroyed || t.def != d) continue;
                        if (EconomyPrivate.OwnerOf(t) != pawn) continue;

                        Thing dropped;
                        if (pawn.inventory.innerContainer.TryDrop(t, pawn.PositionHeld, pawn.MapHeld,
                                ThingPlaceMode.Near, out dropped) && dropped != null)
                        {
                            dropped.SetForbidden(false, false);
                            DevLog.Msg("[DSH-ECONOMY] 手术取材：" + pawn.LabelShort
                                + " 把 " + dropped.def.label + " 放到脚边");
                        }
                        return;
                    }
                }
            }
            catch { }
        }

        /// <summary>这个人背包里有没有私有的、这次手术要用的植入体。</summary>
        public static bool OwnsImplant(Pawn pawn, RecipeDef recipe)
        {
            try
            {
                if (pawn == null || recipe == null || recipe.ingredients == null) return false;
                if (pawn.inventory == null || pawn.inventory.innerContainer == null) return false;

                for (int i = 0; i < recipe.ingredients.Count; i++)
                {
                    IngredientCount ing = recipe.ingredients[i];
                    if (ing == null || ing.filter == null) continue;
                    ThingDef d = ing.filter.AnyAllowedDef;
                    if (d == null || !EconomyConsumption.IsImplantLike(d)) continue;

                    IList<Thing> inner = pawn.inventory.innerContainer;
                    for (int j = 0; j < inner.Count; j++)
                    {
                        Thing t = inner[j];
                        if (t == null || t.Destroyed || t.def != d) continue;
                        if (EconomyPrivate.OwnerOf(t) == pawn) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        // 旧版 Charge() 已删除：手术不再单独收钱（钱在购买时付过）。
    }
}
