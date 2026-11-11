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
        /// 所有"能用"的白银来源：地上的银堆 + 建筑内部容器里（木货箱/冰箱/摊位等）的银。
        /// 以前只查地面，导致国库账面有 1600、实际只拆得出 1~3 银。
        /// </summary>
        public static List<Thing> SilverSources(Map map)
        {
            List<Thing> list = new List<Thing>();
            if (map == null) return list;

            if (map.listerThings != null)
            {
                List<Thing> onMap = map.listerThings.ThingsOfDef(ThingDefOf.Silver);
                for (int i = 0; i < onMap.Count; i++) list.Add(onMap[i]);
            }

            if (map.listerBuildings != null)
            {
                List<Building> bs = map.listerBuildings.allBuildingsColonist;
                for (int i = 0; i < bs.Count; i++)
                {
                    ThingOwner held = null;
                    try
                    {
                        IThingHolder holder = bs[i] as IThingHolder;
                        if (holder != null) held = holder.GetDirectlyHeldThings();
                    }
                    catch { }
                    if (held == null || held.Count == 0) continue;
                    for (int j = 0; j < held.Count; j++)
                    {
                        Thing t = held[j];
                        if (t != null && t.def == ThingDefOf.Silver) list.Add(t);
                    }
                }
            }
            return list;
        }

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
                List<Thing> stacks = SilverSources(map);
                for (int i = 0; i < stacks.Count; i++)
                {
                    if (stacks[i] == null || stacks[i].Destroyed) continue;
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
            List<Thing> stacks = SilverSources(map);
            for (int i = 0; i < stacks.Count && remaining > 0; i++)
            {
                Thing stack = stacks[i];
                if (stack == null || stack.Destroyed) continue;
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
                DevLog.Msg("[DSH-ECONOMY] 扣私银：" + pawn.LabelShort + " 请求 " + amount
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

        // 短缓存：界面每帧都会问"这个人有多少私银"，而下面要扫全图银堆，太贵。
        // 缓存 60 tick（1 秒）足够，且花钱/发薪都会立刻改变实体，1 秒内的误差可接受。
        private static readonly Dictionary<string, int> privateSilverCache = new Dictionary<string, int>();
        private static int privateSilverTick = -99999;

        /// <summary>某人名下的私有银币总量（实体物品，含背包内）。</summary>
        public static int PrivateSilver(Map map, Pawn pawn)
        {
            if (map == null || pawn == null) return 0;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - privateSilverTick > 60)
            {
                privateSilverCache.Clear();
                privateSilverTick = now;
            }
            string key = map.uniqueID + "|" + pawn.GetUniqueLoadID();
            int cached;
            if (privateSilverCache.TryGetValue(key, out cached)) return cached;

            int total = 0;
            List<Thing> stacks = AllSilverStacks(map);
            for (int i = 0; i < stacks.Count; i++)
            {
                if (EconomyPrivate.OwnerOf(stacks[i]) == pawn) total += stacks[i].stackCount;
            }
            privateSilverCache[key] = total;
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

            // ① 先花"小人背包里的公共银"。国库（ColonySilver）把它算在内，但它不在
            //    listerThings 里，以前只从地面拆 → 国库显示 1641、地上只有 1 银时，
            //    每人只能拿到 1，差额静默丢失（WAGE-2 日志里的"计划 57 实拆 1"）。
            List<Pawn> bagOwners = map.mapPawns.FreeColonists;
            for (int b = 0; b < bagOwners.Count && remaining > 0; b++)
            {
                Pawn owner = bagOwners[b];
                if (owner.inventory == null || owner.inventory.innerContainer == null) continue;
                IList<Thing> innerBag = owner.inventory.innerContainer;
                for (int j = 0; j < innerBag.Count && remaining > 0; j++)
                {
                    Thing bagSilver = innerBag[j];
                    if (bagSilver == null || bagSilver.Destroyed) continue;
                    if (bagSilver.def != ThingDefOf.Silver) continue;
                    if (EconomyPrivate.IsPrivate(bagSilver)) continue;
                    int bagTake = remaining < bagSilver.stackCount ? remaining : bagSilver.stackCount;
                    bagSilver.stackCount -= bagTake;
                    remaining -= bagTake;
                    if (bagSilver.stackCount <= 0) bagSilver.Destroy();
                }
            }

            List<Thing> stacks = SilverSources(map);
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

                // ① 他名下已经有一堆私有银，且装得下 → 直接并进去，标签天然保留
                bool merged = false;
                if (pawn.inventory != null && pawn.inventory.innerContainer != null)
                {
                    IList<Thing> inner = pawn.inventory.innerContainer;
                    for (int j = 0; j < inner.Count; j++)
                    {
                        Thing t = inner[j];
                        if (t == null || t.Destroyed) continue;
                        if (t.def != ThingDefOf.Silver) continue;
                        if (EconomyPrivate.OwnerOf(t) != pawn) continue;
                        if (t.stackCount + take > t.def.stackLimit) continue;
                        t.stackCount += take;
                        merged = true;
                        break;
                    }
                }
                if (merged) { DevLog.Msg("[PAY] " + pawn.LabelShort + " 并进背包已有的私有银 +" + take); continue; }

                // ② 否则不合并地塞进背包：合并会让 coin 被销毁，标签随之丢失
                EconomyPrivate.SetPrivate(coin, pawn);
                if (pawn.inventory != null && pawn.inventory.innerContainer != null
                    && pawn.inventory.innerContainer.TryAdd(coin, false))
                {
                    // 已入包，标签就在 coin 上
                    DevLog.Msg("[PAY] " + pawn.LabelShort + " 银币入包 +" + take
                        + " 现在背包银=" + BagSilverOf(pawn));
                }
                else
                {
                    GenPlace.TryPlaceThing(coin, pawn.PositionHeld, map, ThingPlaceMode.Near, out Thing placed);
                    if (placed != null && placed != coin) EconomyPrivate.SetPrivate(placed, pawn);
                    DevLog.Msg("[PAY] " + pawn.LabelShort + " 入包失败，丢在脚下 +" + take
                        + " 落地=" + (placed != null) + " 现在背包银=" + BagSilverOf(pawn));
                }

                // 关键：把币塞进背包时，若它被并进了一堆"没有标签的公共银"，
                // 引擎会销毁 coin、标签随之消失 → 工资变回国库。
                // 这里事后检查：coin 没了，就把那堆并过去的目标重新打上标签。
                if (coin.Destroyed || coin.ParentHolder == null)
                {
                    Thing host = FindUntaggedSilverInBag(pawn);
                    if (host != null)
                    {
                        EconomyPrivate.SetPrivate(host, pawn);
                        DevLog.Msg("[PAY] " + pawn.LabelShort + " 的银币被并进公共堆，已把该堆 ×"
                            + host.stackCount + " 重新标为私有");
                    }
                    else
                    {
                        DevLog.Msg("[PAY] " + pawn.LabelShort + " 的银币被并掉且找不到宿主堆（钱可能已丢）");
                    }
                }
            }
            return amount - remaining;
        }

        /// <summary>在背包里找一堆"没有主人标签"的白银，用来接手被合并掉的那笔工资。</summary>
        public static Thing FindUntaggedSilverInBag(Pawn p)
        {
            if (p == null || p.inventory == null || p.inventory.innerContainer == null) return null;
            IList<Thing> inner = p.inventory.innerContainer;
            for (int i = 0; i < inner.Count; i++)
            {
                Thing t = inner[i];
                if (t == null || t.Destroyed) continue;
                if (t.def != ThingDefOf.Silver) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue;
                return t;
            }
            return null;
        }

        public static int BagSilverOf(Pawn p)
        {
            if (p == null || p.inventory == null || p.inventory.innerContainer == null) return 0;
            int n = 0;
            IList<Thing> inner = p.inventory.innerContainer;
            for (int i = 0; i < inner.Count; i++)
                if (inner[i] != null && !inner[i].Destroyed && inner[i].def == ThingDefOf.Silver) n += inner[i].stackCount;
            return n;
        }
    }
}

