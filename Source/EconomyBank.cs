using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>殖民地公共库存与个人白银之间的搬运工具。</summary>
    public static class EconomyBank
    {
        /// <summary>
        /// 公共库存白银总量：地图上的公共银堆 + 小人背包里尚未私有化的银币
        /// （"搬运中的公共物资"也算国库，否则收税后银币留在背包里会从账面消失）。
        /// </summary>
        public static int ColonySilver(Map map)
        {
            if (map == null) return 0;
            int total = 0;

            if (map.listerThings != null)
            {
                List<Thing> stacks = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
                for (int i = 0; i < stacks.Count; i++)
                {
                    if (EconomyPrivate.IsPrivate(stacks[i])) continue;
                    total += stacks[i].stackCount;
                }
            }

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p.inventory == null || p.inventory.innerContainer == null) continue;
                IList<Thing> inner = p.inventory.innerContainer;
                for (int j = 0; j < inner.Count; j++)
                {
                    Thing t = inner[j];
                    if (t.def != ThingDefOf.Silver) continue;
                    if (EconomyPrivate.IsPrivate(t)) continue;
                    total += t.stackCount;
                }
            }
            return total;
        }

        /// <summary>从公共库存扣白银，返回实际扣掉的数量。背包里的公共银也算。</summary>
        public static int SpendColonySilver(Map map, int amount)
        {
            if (map == null || amount <= 0) return 0;
            int remaining = amount;
            List<Thing> stacks = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            for (int i = 0; i < stacks.Count && remaining > 0; i++)
            {
                Thing stack = stacks[i];
                if (stack.Destroyed) continue;
                if (EconomyPrivate.IsPrivate(stack)) continue;
                int take = remaining < stack.stackCount ? remaining : stack.stackCount;
                stack.stackCount -= take;
                remaining -= take;
                if (stack.stackCount <= 0) stack.Destroy();
            }

            if (remaining > 0 && map.mapPawns != null)
            {
                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count && remaining > 0; i++)
                {
                    Pawn p = colonists[i];
                    if (p.inventory == null || p.inventory.innerContainer == null) continue;
                    IList<Thing> inner = p.inventory.innerContainer;
                    for (int j = inner.Count - 1; j >= 0 && remaining > 0; j--)
                    {
                        Thing t = inner[j];
                        if (t.def != ThingDefOf.Silver || EconomyPrivate.IsPrivate(t)) continue;
                        int take = remaining < t.stackCount ? remaining : t.stackCount;
                        t.stackCount -= take;
                        remaining -= take;
                        if (t.stackCount <= 0) t.Destroy();
                    }
                }
            }
            return amount - remaining;
        }

        /// <summary>往地图上放白银（拆成不超过堆叠上限的多堆）。</summary>
        public static void GiveColonySilver(Map map, int amount, IntVec3 cell)
        {
            if (map == null || amount <= 0) return;
            int limit = ThingDefOf.Silver.stackLimit;
            int left = amount;
            while (left > 0)
            {
                Thing stack = ThingMaker.MakeThing(ThingDefOf.Silver);
                stack.stackCount = left < limit ? left : limit;
                GenSpawn.Spawn(stack, cell, map);
                left -= stack.stackCount;
            }
        }

        /// <summary>
        /// 地图上所有银堆：包括散落在地上的，以及小人背包里的。
        /// 工资银币是直接塞背包的，只用 listerThings 会全漏掉。
        /// </summary>
        public static List<Thing> AllSilverStacks(Map map)
        {
            List<Thing> result = new List<Thing>();
            if (map == null) return result;

            if (map.listerThings != null)
            {
                List<Thing> onMap = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
                for (int i = 0; i < onMap.Count; i++) result.Add(onMap[i]);
            }

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p.inventory == null || p.inventory.innerContainer == null) continue;
                IList<Thing> inner = p.inventory.innerContainer;
                for (int j = 0; j < inner.Count; j++)
                {
                    if (inner[j].def == ThingDefOf.Silver) result.Add(inner[j]);
                }
            }
            return result;
        }

        /// <summary>
        /// 从某人名下的私有银堆里扣钱（税收、学费用）。
        /// 只撕标签，银币留在原处变回公共，所以钱没有消失。
        /// </summary>
        public static int TakePrivateSilver(Map map, Pawn pawn, int amount)
        {
            if (map == null || pawn == null || amount <= 0) return 0;

            int before = PrivateSilver(map, pawn);
            int taken = TakePrivateSilverCore(map, pawn, amount);
            if (taken > 0)
            {
                Log.Message("[DSH-ECONOMY] 扣私银：" + pawn.LabelShort + " 请求 " + amount
                    + " 实扣 " + taken + "，扣前 " + before + " 扣后 " + (before - taken));
            }
            return taken;
        }

        private static int TakePrivateSilverCore(Map map, Pawn pawn, int amount)
        {
            if (map == null || pawn == null || amount <= 0) return 0;

            int remaining = amount;
            List<Thing> stacks = AllSilverStacks(map);
            for (int i = 0; i < stacks.Count && remaining > 0; i++)
            {
                Thing stack = stacks[i];
                if (stack == null || stack.Destroyed) continue;
                if (EconomyPrivate.OwnerOf(stack) != pawn) continue;

                int take = Mathf.Min(remaining, stack.stackCount);
                bool inInventory = stack.ParentHolder is Pawn_InventoryTracker;

                if (inInventory)
                {
                    // 收税后银币留在小人背包里，归属变回公共：
                    // 拆出一小堆公共银币塞回背包，剩下的仍是他的私有银。
                    if (take >= stack.stackCount)
                    {
                        EconomyPrivate.SetPrivate(stack, null);
                    }
                    else
                    {
                        stack.stackCount -= take;

                        Thing part = ThingMaker.MakeThing(ThingDefOf.Silver);
                        part.stackCount = take;
                        EconomyPrivate.SetPrivate(part, null);

                        bool added = pawn.inventory != null && pawn.inventory.innerContainer != null
                                     && pawn.inventory.innerContainer.TryAdd(part, true);
                        if (!added)
                        {
                            GenPlace.TryPlaceThing(part, pawn.PositionHeld, map, ThingPlaceMode.Near, out Thing placed);
                            if (placed != null) EconomyPrivate.SetPrivate(placed, null);
                        }
                    }
                }
                else if (take >= stack.stackCount)
                {
                    EconomyPrivate.SetPrivate(stack, null);
                }
                else
                {
                    stack.stackCount -= take;
                    DeliverToColony(map, pawn, take);
                }
                remaining -= take;
            }
            return amount - remaining;
        }

        /// <summary>
        /// 把收上来的银币并进已有的公共银堆；没有可并的才掉在脚下。
        /// 这样收税不会满地散银，玩家也不用去地上捡。
        /// </summary>
        private static void DeliverToColony(Map map, Pawn pawn, int count)
        {
            if (map == null || count <= 0) return;

            List<Thing> onMap = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            for (int i = 0; i < onMap.Count; i++)
            {
                Thing t = onMap[i];
                if (t == null || t.Destroyed || t.Spawned == false) continue;
                if (EconomyPrivate.IsPrivate(t)) continue;
                if (t.ParentHolder is Pawn_InventoryTracker) continue;
                if (t.stackCount + count > t.def.stackLimit) continue;

                t.stackCount += count;
                return;
            }

            Thing coin = ThingMaker.MakeThing(ThingDefOf.Silver);
            coin.stackCount = count;
            GenPlace.TryPlaceThing(coin, pawn.PositionHeld, map, ThingPlaceMode.Near, out Thing placed);
            if (placed != null) EconomyPrivate.SetPrivate(placed, null);
        }

        /// <summary>一次遍历算出"每个人名下的私有银币总量"，避免逐人重复扫描（界面掉帧的元凶）。</summary>
        public static Dictionary<Pawn, int> PrivateSilverByOwner(Map map)
        {
            var result = new Dictionary<Pawn, int>();
            if (map == null) return result;

            List<Thing> stacks = AllSilverStacks(map);
            for (int i = 0; i < stacks.Count; i++)
            {
                Thing t = stacks[i];
                if (t == null || t.Destroyed) continue;
                Pawn owner = EconomyPrivate.OwnerOf(t);
                if (owner == null) continue;

                int cur;
                result.TryGetValue(owner, out cur);
                result[owner] = cur + t.stackCount;
            }
            return result;
        }

        /// <summary>某人名下的私有银币总量（实体物品，含背包内）。</summary>
        public static int PrivateSilver(Map map, Pawn pawn)
        {
            if (map == null || pawn == null) return 0;
            int total = 0;
            List<Thing> stacks = AllSilverStacks(map);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (EconomyPrivate.OwnerOf(stacks[i]) == pawn) total += stacks[i].stackCount;
            }
            return total;
        }

        /// <summary>
        /// 从公共银堆里拆出一份银币，丢在小人身边并打上私有标签。
        /// 返回实际拆出的数量。
        /// </summary>
        public static int SplitSilverToPrivate(Map map, Pawn pawn, int amount)
        {
            if (map == null || pawn == null || amount <= 0) return 0;

            int remaining = amount;
            List<Thing> stacks = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
            for (int i = 0; i < stacks.Count && remaining > 0; i++)
            {
                Thing src = stacks[i];
                if (src == null || src.Destroyed) continue;
                if (EconomyPrivate.IsPrivate(src)) continue;

                int take = remaining < src.stackCount ? remaining : src.stackCount;
                src.stackCount -= take;
                remaining -= take;
                if (src.stackCount <= 0) src.Destroy();

                Thing coin = ThingMaker.MakeThing(ThingDefOf.Silver);
                coin.stackCount = take;

                // 先打标签再入包：否则入包时与已有私有银堆的归属判断不一致，
                // 会分成两堆而不是合并成一堆。
                EconomyPrivate.SetPrivate(coin, pawn);

                if (pawn.inventory != null && pawn.inventory.innerContainer != null
                    && pawn.inventory.innerContainer.TryAdd(coin, true))
                {
                    // 已并入背包
                }
                else
                {
                    GenPlace.TryPlaceThing(coin, pawn.PositionHeld, map, ThingPlaceMode.Near, out Thing placed);
                    if (placed != null && placed != coin) EconomyPrivate.SetPrivate(placed, pawn);
                }
            }
            return amount - remaining;
        }
    }
}
