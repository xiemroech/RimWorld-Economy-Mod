using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 购买＝真的走过去把货拿进背包，拿到手的那一刻才扣钱。
    /// 优先级：市场摊位 &gt; 普通仓库/地面。
    /// </summary>
    public static class EconomyMarket
    {
        public class Pending
        {
            public Pawn pawn;
            public ThingDef def;
            public int count;
            public float unitPrice;
            public int tick;
        }

        private static readonly Dictionary<Pawn, Pending> pending = new Dictionary<Pawn, Pending>();
        private static readonly List<Pawn> staleScratch = new List<Pawn>();

        /// <summary>没拿到的挂单超过这个 tick 数就作废（约 2 游戏小时）。</summary>
        public const int PendingTimeout = 1500;

        public static bool HasPending(Pawn p) => p != null && pending.ContainsKey(p);

        public static void ClearRuntime()
        {
            pending.Clear();
            staleScratch.Clear();
        }

        // ---------------- 选货 ----------------

        public static bool InMarket(Thing t)
        {
            if (t == null) return false;
            SlotGroup g = t.GetSlotGroup();
            return g != null && g.parent is Building_Market;
        }

        public static IEnumerable<Building_Market> Markets(Map map)
        {
            if (map == null) yield break;
            foreach (Building b in map.listerBuildings.AllBuildingsColonistOfClass<Building_Market>())
            {
                Building_Market m = b as Building_Market;
                if (m != null && !m.Destroyed) yield return m;
            }
        }

        /// <summary>在市场里挑一件当前买得起的货；没有就返回 null（调用方再退回仓库）。</summary>
        public static Thing PickFromMarket(Map map, Pawn p, string categoryDefName, out float price)
        {
            price = 0f;
            if (map == null || p == null) return null;

            int preferredTier = categoryDefName == "DshEco_Food" ? EconomyConsumption.PreferredFoodTier(p, map) : 99;
            int silver = EconomyBank.PrivateSilver(map, p);

            List<Thing> candidates = new List<Thing>();
            List<float> weights = new List<float>();
            int bestTier = -1;

            foreach (Building_Market m in Markets(map))
            {
                SlotGroup g = m.GetSlotGroup();
                if (g == null) continue;
                foreach (Thing t in g.HeldThings)
                {
                    if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                    if (EconomyPrivate.OwnerOf(t) != null) continue;       // 别人的私货不卖
                    if (!EconomyConsumption.IsCandidate(t, categoryDefName, p)) continue;

                    int tier = 0;
                    float weight = 1f;
                    if (categoryDefName == "DshEco_Food")
                    {
                        tier = EconomyConsumption.FoodTierOf(t.def);
                        if (tier > preferredTier) continue;
                        float opt = FoodUtility.FoodOptimality(p, t, t.def, 0f, false);
                        weight = Mathf.Max(0.05f, opt);
                    }

                    float unit = EconomyPrice.UnitPrice(t, map);
                    if (unit > 0.05f && Mathf.CeilToInt(unit) > silver) continue;

                    if (tier > bestTier) { bestTier = tier; candidates.Clear(); weights.Clear(); }
                    if (tier == bestTier) { candidates.Add(t); weights.Add(weight); }
                }
            }

            if (candidates.Count == 0) return null;

            float total = 0f;
            for (int i = 0; i < weights.Count; i++) total += weights[i];
            Thing chosen = candidates[0];
            if (total > 0f)
            {
                float roll = Rand.Value * total;
                for (int i = 0; i < candidates.Count; i++)
                {
                    roll -= weights[i];
                    if (roll <= 0f) { chosen = candidates[i]; break; }
                }
            }
            price = EconomyPrice.UnitPrice(chosen, map);
            return chosen;
        }

        // ---------------- 下单（派搬运 job，不立刻扣钱） ----------------

        public static bool StartPurchase(Map map, Pawn p, Thing stock, int count, float unitPrice)
        {
            if (map == null || p == null || stock == null || stock.Destroyed || stock.stackCount <= 0) return false;
            if (EconomyConsumption.IsImplantLike(stock.def)) return false;   // 器官/仿生体不参与消费购买
            if (HasPending(p)) return false;
            if (p.jobs == null || p.Dead || p.Downed || p.InMentalState) return false;
            if (!p.CanReserveAndReach(stock, PathEndMode.ClosestTouch, Danger.Some)) return false;

            int n = Mathf.Clamp(count, 1, stock.stackCount);
            int cost = Mathf.CeilToInt(unitPrice * n);
            if (cost > 0 && EconomyBank.PrivateSilver(map, p) < cost) return false;

            Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, stock);
            job.count = n;
            job.haulMode = HaulMode.ToCellStorage;

            pending[p] = new Pending
            {
                pawn = p,
                def = stock.def,
                count = n,
                unitPrice = unitPrice,
                tick = Find.TickManager.TicksGame
            };
            p.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);   // 排队而不是强插：强插会打断正在进行的进食/睡觉
            return true;
        }

        // ---------------- 结算（拿到手才扣钱） ----------------

        /// <summary>物品进入某个 Pawn 背包时调用。</summary>
        public static void OnInventoryAdded(Pawn pawn, Thing item)
        {
            if (pawn == null || item == null) return;
            Pending rec;
            if (!pending.TryGetValue(pawn, out rec)) return;
            if (rec.def != item.def) return;

            Map map = pawn.MapHeld;
            pending.Remove(pawn);
            if (map == null) return;

            int n = Mathf.Max(1, rec.count);
            int cost = Mathf.CeilToInt(rec.unitPrice * n);
            if (cost > 0)
            {
                int taken = EconomyBank.TakePrivateSilver(map, pawn, cost);
                if (taken < cost)
                {
                    // 钱不够：这次不算买成。把货退回去（地上），并把已扣的钱退还，避免"少付拿货"。
                    if (taken > 0) EconomyBank.GiveColonySilver(map, taken, pawn.PositionHeld);
                    if (item != null && !item.Destroyed && item.stackCount > 0)
                    {
                        int back = Mathf.Min(item.stackCount, n);
                        Thing part = item.SplitOff(back);
                        GenPlace.TryPlaceThing(part, pawn.PositionHeld, map, ThingPlaceMode.Near);
                    }
                    EconomySatisfactionEvents.Add(pawn, "买不起（市场结算时钱不够）", -1f, 3);
                    EconomySatisfaction.Recompute(pawn);
                    Log.Message("[DSH-ECONOMY] 购买结算失败：应付 " + cost + " 只有 " + taken
                        + "，已退货并退款（" + pawn.LabelShort + " " + rec.def.label + "×" + n + "）");
                    return;
                }
            }

            // TryAdd 可能已经把它并进旧堆（item 被销毁），这时给背包里那堆打私有标记
            Thing target = item;
            if (target == null || target.Destroyed || target.stackCount <= 0)
            {
                foreach (Thing t in pawn.inventory.innerContainer)
                {
                    if (t != null && t.def == rec.def && EconomyPrivate.OwnerOf(t) == null) { target = t; break; }
                }
            }
            if (target != null && !target.Destroyed) EconomyPrivate.SetPrivate(target, pawn);

            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp != null) comp.consumptionBought += n;
        }

        // ---------------- 清理 ----------------

        public static void Tick()
        {
            if (pending.Count == 0) return;
            int now = Find.TickManager.TicksGame;
            staleScratch.Clear();
            foreach (KeyValuePair<Pawn, Pending> kv in pending)
            {
                if (kv.Key == null || kv.Key.Dead || kv.Key.Downed
                    || now - kv.Value.tick > PendingTimeout
                    || kv.Key.CurJobDef != JobDefOf.TakeInventory) staleScratch.Add(kv.Key);
            }
            for (int i = 0; i < staleScratch.Count; i++)
            {
                if (staleScratch[i] != null) pending.Remove(staleScratch[i]);
            }
            staleScratch.Clear();
        }
    }
}
