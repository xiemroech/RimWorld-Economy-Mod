using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;

namespace DshEconomy
{
    /// <summary>
    /// 别人不能吃你的私有食物。
    /// 挂在原版 FoodUtility.WillEat 上，而不是挂在 StartJob 上：
    /// 挂在 StartJob 时，食物搜索已经把这份食物选出来了，job 被拒后小人就没有任务、
    /// 站在原地不动（没有任务闪烁）。挂在 WillEat 上，搜索阶段就会跳过别人的私有食物，
    /// 自然改选其它食物。
    /// </summary>
    public static class PrivateFoodEatBlock
    {
        /// <summary>
        /// 搜索阶段就排除"别人私有营养膏"，不在 WillEat 里拦（那里会在进食中途重查导致打断）。
        /// </summary>
        public static void FindFoodPostfix(Pawn eater, ref bool __result, ref Thing foodSource)
        {
            if (!__result || eater == null || foodSource == null) return;
            // 自己有存货：优先吃自己的，不要再去机器前排队
            if (foodSource is Building_NutrientPasteDispenser && EconomyPasteDispenser.OwnsPaste(eater))
            {
                __result = false;
                foodSource = null;
                return;
            }
            if (!EconomyPasteDispenser.IsPaste(foodSource)) return;
            Pawn owner = EconomyPrivate.OwnerOf(foodSource);
            if (owner == null || owner == eater) return;
            if (eater.IsPrisoner || eater.IsSlave) return;
            __result = false;
            foodSource = null;
        }

        /// <summary>
        /// 自己的私有食物在"找吃的"时加权：让自己买的饭优先于地上的公共饭。
        /// 挂在 FoodUtility.FoodOptimality 上，只影响选择，不影响进食本身。
        /// </summary>
        public static void OptimalityPostfix(Pawn eater, Thing foodSource, ref float __result)
        {
            if (eater == null || foodSource == null) return;
            if (EconomyPrivate.OwnerOf(foodSource) != eater) return;
            __result += 8f;   // 原版评分大致 0~60，+8 足够压过"更近一点"的公共饭
        }

        public static bool Prefix(Pawn p, Thing food, ref bool __result)
        {
            if (p == null || food == null) return true;

            // 营养膏一律不拦：拦了会在"正在吃"的时候打断，饭吃一半掉地上又变成公共货。
            // 归属与收费只在 TryStartCarry 那一刻结算。
            if (EconomyPasteDispenser.IsPaste(food)) return true;

            Pawn owner = EconomyPrivate.OwnerOf(food);
            if (owner == null || owner == p) return true;

            // 囚犯/奴隶的普通食物：允许互相吃（送饭场景）
            if (p.IsPrisoner || p.IsSlave) return true;
            if (owner.IsPrisoner || owner.IsSlave) return true;

            __result = false;
            return false;
        }
    }
}
