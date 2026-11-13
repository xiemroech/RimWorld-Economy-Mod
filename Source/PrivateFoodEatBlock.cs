using HarmonyLib;
using RimWorld;
using System.Collections.Generic;
using Verse;
using Verse.AI;
using UnityEngine;

namespace DshEconomy
{
    /// <summary>
    /// 别人的私有食物不能吃；自己的私有食物要优先吃。
    ///
    /// 判定"这堆饭是谁的"有两层，缺一层就会误判：
    ///   1) 物品上的私有标签（CompItemTag.owner）
    ///   2) 它所在的存储格归属（私有仓库里没打标签的，也算这个人的）
    /// 以前只看第一层，标签一旦在合并/搬运里丢了，就变成"人人都能吃、谁都不优先"。
    /// </summary>
    public static class PrivateFoodEatBlock
    {
        /// <summary>这堆食物是不是这个人的（标签归属，或放在他的私有仓库里，或就在他背包里）。</summary>
        public static Pawn BelongsTo(Thing t)
        {
            if (t == null) return null;
            Pawn owner = EconomyPrivate.OwnerOf(t);
            if (owner != null) return owner;

            // 背包里的东西：没标签也算背着的人的
            Pawn_InventoryTracker inv = t.ParentHolder as Pawn_InventoryTracker;
            if (inv != null && inv.pawn != null) return inv.pawn;

            Map map = t.MapHeld;
            if (map == null || map.haulDestinationManager == null) return null;
            SlotGroup g = map.haulDestinationManager.SlotGroupAt(t.Position);
            if (g == null) return null;
            return EconomyPrivateStorageBehavior.OwnerOfGroup(g);
        }

        /// <summary>
        /// 搜索阶段：
        ///   ①别人私有的，直接跳过（不是拦，是让它改选别的）
        ///   ②自己有饭，就不去营养膏机排队
        ///   ③自己有饭时，把已经选中的"公共饭"换成自己的，保证优先吃自己的
        /// </summary>
        public static void FindFoodPostfix(Pawn eater, ref bool __result, ref Thing foodSource)
        {
            if (eater == null) return;

            // 自己有营养膏存货：别去机器前排队
            if (__result && foodSource is Building_NutrientPasteDispenser && EconomyPasteDispenser.OwnsPaste(eater))
            {
                __result = false;
                foodSource = null;
                return;
            }

            if (__result && foodSource != null)
            {
                Pawn owner = BelongsTo(foodSource);
                if (owner != null && owner != eater && !eater.IsPrisoner && !eater.IsSlave
                    && !owner.IsPrisoner && !owner.IsSlave)
                {
                    __result = false;
                    foodSource = null;
                    return;
                }

                // 公共的：不硬换，只把分数压到最低，让他自己去挑自己的
            }
        }

        /// <summary>找这个人手上/仓库里最好的一份能吃的东西；没有就返回 null。</summary>
        private static Thing BestOwnFood(Pawn p)
        {
            Thing best = null;
            float bestOpt = float.MinValue;

            // 背包
            if (p.inventory != null && p.inventory.innerContainer != null)
            {
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (!Usable(p, t)) continue;
                    float opt = Opt(p, t);
                    if (opt > bestOpt) { bestOpt = opt; best = t; }
                }
            }

            // 私有仓库里的格子
            Map map = p.MapHeld;
            if (map != null && map.haulDestinationManager != null)
            {
                foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                {
                    if (g == null || g.CellsList == null) continue;
                    if (EconomyPrivateStorageBehavior.OwnerOfGroup(g) != p) continue;
                    for (int i = 0; i < g.CellsList.Count; i++)
                    {
                        List<Thing> here = g.CellsList[i].GetThingList(map);
                        for (int k = 0; k < here.Count; k++)
                        {
                            Thing t = here[k];
                            if (!Usable(p, t)) continue;
                            if (!p.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                            float opt = Opt(p, t);
                            if (opt > bestOpt) { bestOpt = opt; best = t; }
                        }
                    }
                }
            }
            return best;
        }

        private static bool Usable(Pawn p, Thing t)
        {
            if (t == null || t.def == null || t.Destroyed) return false;
            if (!t.def.IsNutritionGivingIngestible) return false;
            try { if (!FoodUtility.WillEat(p, t, p, true, true)) return false; }
            catch { return false; }
            if (t.IsForbidden(p)) return false;
            return true;
        }

        private static float Opt(Pawn p, Thing t)
        {
            try { return FoodUtility.FoodOptimality(p, t, t.def, 0f); }
            catch { return 0f; }
        }

        /// <summary>
        /// 口味评分调整（只影响"选哪份"，不强行替换）：
        ///   自己的 → +25
        ///   别人的 → -100（另外 WillEat 也会直接挡掉）
        ///   公共的 → -100（压到最低，让他先挑自己的；实在没得吃时仍会吃，不饿死）
        /// </summary>
        public static void OptimalityPostfix(Pawn eater, Thing foodSource, ref float __result)
        {
            if (eater == null || foodSource == null) return;
            Pawn owner = BelongsTo(foodSource);
            if (owner == eater) __result += 25f;
            else __result -= 100f;
        }

        public static bool Prefix(Pawn p, Thing food, ref bool __result)
        {
            if (p == null || food == null) return true;

            // 营养膏一律不拦：拦了会在"正在吃"的时候打断，饭吃一半掉地上又变成公共货。
            // 归属与收费只在 TryStartCarry 那一刻结算。
            if (EconomyPasteDispenser.IsPaste(food)) return true;

            Pawn owner = BelongsTo(food);
            if (owner == null || owner == p) return true;

            // 囚犯/奴隶的普通食物：允许互相吃（送饭场景）
            if (p.IsPrisoner || p.IsSlave) return true;
            if (owner.IsPrisoner || owner.IsSlave) return true;

            __result = false;
            return false;
        }
    }
}
