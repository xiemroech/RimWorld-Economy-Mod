using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 随身白银上限：小人只会带「3 倍低价消费品均价」的零钱，
    /// 多出来的白银存进自己的私有仓库（没有仓库就继续带着）。
    /// 买东西时仍然可以从任何属于自己的私银堆里扣（EconomyBank 会找）。
    /// </summary>
    public static class EconomyPocketMoney
    {
        // 随身零钱 = 均价 × 这个倍数
        public const float PocketMultiplier = 3f;

        private static float cachedAvg = -1f;
        private static int cachedTick = -99999;

        public static void Tick(Map map)
        {
            if (map == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Deposit(colonists[i], map);
            }
        }

        /// <summary>低价消费品的平均市价（食物/制成品/成瘾品里便宜的那一半）。</summary>
        public static float CheapConsumerAverage(Map map)
        {
            if (map == null) return 10f;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (cachedAvg > 0f && now - cachedTick < 600) return cachedAvg;

            cachedTick = now;
            var prices = new List<float>();
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.def == null || t.stackCount <= 0) continue;
                if (t.def.category != ThingCategory.Item) continue;
                EconomyCategoryDef cat = EconomyRegistry.CategoryOf(t.def);
                if (cat == null) continue;
                string n = cat.defName;
                if (n != "DshEco_Food" && n != "DshEco_Goods" && n != "DshEco_Drugs") continue;
                float price = EconomyPrice.UnitPrice(t, map);
                if (price > 0.01f) prices.Add(price);
            }

            if (prices.Count == 0)
            {
                cachedAvg = 10f;
                return cachedAvg;
            }
            prices.Sort();
            int take = Mathf.Max(1, prices.Count / 2);
            float sum = 0f;
            for (int i = 0; i < take; i++) sum += prices[i];
            cachedAvg = Mathf.Max(1f, sum / take);
            return cachedAvg;
        }

        public static int PocketLimit(Map map)
        {
            return Mathf.Max(1, Mathf.RoundToInt(CheapConsumerAverage(map) * PocketMultiplier));
        }

        /// <summary>把超过零钱上限的白银放进自己的仓库。</summary>
        private static void Deposit(Pawn p, Map map)
        {
            if (p == null || p.Dead || p.Downed || p.inventory == null) return;

            ThingDef currency = EconomyRegistry.PrimaryCurrency;
            if (currency == null) return;

            int carried = 0;
            Thing stack = null;
            foreach (Thing t in p.inventory.innerContainer)
            {
                if (t == null || t.def != currency) continue;
                carried += t.stackCount;
                if (stack == null) stack = t;
            }
            if (stack == null) return;

            int limit = PocketLimit(map);
            int excess = carried - limit;
            if (excess <= 0) return;

            IntVec3 cell;
            if (!FindFreeCell(p, map, out cell))
            {
                // 自己仓库塞不下：超额的零钱直接"去标签"变回公共，交给公共白银回收逻辑搬走/丢下，
                // 否则这叠私有白银会永远留在背包里，既不算工资也没人处理
                Thing free = stack.SplitOff(Mathf.Min(excess, stack.stackCount));
                if (free != null)
                {
                    EconomyPrivate.SetPrivate(free, null);
                    Log.Message("[DSH-ECONOMY] 零钱超额：" + p.LabelShort + " 身上 " + carried + " 银超过上限 "
                                + limit + "，且私有仓库没空格，" + free.stackCount + " 银转回公共");
                }
                return;
            }

            Thing part = stack.SplitOff(Mathf.Min(excess, stack.stackCount));
            if (part == null) return;
            EconomyPrivate.SetPrivate(part, p);
            if (!GenPlace.TryPlaceThing(part, cell, map, ThingPlaceMode.Direct))
            {
                if (!p.inventory.innerContainer.TryAdd(part)) part.Destroy();
            }
        }

        /// <summary>在自己私有仓库里找一个空格。</summary>
        private static bool FindFreeCell(Pawn p, Map map, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (map.listerBuildings == null) return false;

            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                Building_Storage st = b as Building_Storage;
                if (st == null) continue;
                if (EconomyRealEstate.OwnerOfBuilding(st) != p) continue;

                SlotGroup g = st.GetSlotGroup();
                if (g == null || g.CellsList == null) continue;
                for (int i = 0; i < g.CellsList.Count; i++)
                {
                    IntVec3 c = g.CellsList[i];
                    if (!c.InBounds(map)) continue;
                    if (c.GetThingList(map).Count == 0)
                    {
                        cell = c;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
