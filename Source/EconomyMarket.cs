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
            
            public Thing stock;        // 当初要拿的那一堆
            public int retries;        // 重派过几次
            public int lastTryTick;    // 上次重派的时间
        }

        private static readonly Dictionary<Pawn, Pending> pending = new Dictionary<Pawn, Pending>();
        private static readonly List<Pawn> staleScratch = new List<Pawn>();

        /// <summary>上次执行"私有堆兜底合并"的 tick。</summary>
        private static int lastMergeTick = -99999;

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

        public static bool StartPurchase(Map map, Pawn p, Thing stock, int count, float unitPrice,bool force = false)
        {
            if (map == null || p == null || stock == null || stock.Destroyed || stock.stackCount <= 0) return false;
            // 器官/仿生体不参与日常冲动消费，但"这个人确实缺这个部位"时允许他买自己需要的
            if (EconomyConsumption.IsImplantLike(stock.def)
                && !EconomyImplantDemand.Wanted(p, stock.def)) return false;
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
                tick = Find.TickManager.TicksGame,
                stock = stock
            };
            // 两种路径都必须"强派"：EnqueueFirst 只是排队，小人会被思考树立刻顶掉，
            // 任务作废 → 不结算 → 货拿到手却不打标签（"买了不算买"）。
            p.jobs.TryTakeOrderedJob(job, JobTag.Misc);

            DevLog.Msg("[DSH-ECONOMY] 下单：" + p.LabelShort + " → " + stock.LabelNoCount + "×" + n
                + " 强制=" + force + " 当前任务=" + (p.CurJob == null ? "无" : p.CurJob.def.defName));
            
            return true;
        }

        // ---------------- 结算（拿到手才扣钱） ----------------

        /// <summary>物品进入某个 Pawn 背包时调用。返回是否真的买成（付款成功）。</summary>
        public static bool OnInventoryAdded(Pawn pawn, Thing item)
        {
            if (pawn == null || item == null) return false;
            Pending rec;
            if (!pending.TryGetValue(pawn, out rec)) return false;
            if (rec.def != item.def) return false;

            Map map = pawn.MapHeld;
            pending.Remove(pawn);
            if (map == null) return false;

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
                    DevLog.Msg("[DSH-ECONOMY] 购买结算失败：应付 " + cost + " 只有 " + taken
                        + "，已退货并退款（" + pawn.LabelShort + " " + rec.def.label + "×" + n + "）");
                    return false;
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
            return true;
        }
        /// <summary>任务成功结束后调用：背包里已经有货，就地结算并打标签。</summary>
        public static bool SettleFromInventory(Pawn p)
         {
            if (p == null || p.inventory == null || p.inventory.innerContainer == null) return false;

            Pending rec;
            if (!pending.TryGetValue(p, out rec)) return false;

            Thing found = null;
            IList<Thing> inner = p.inventory.innerContainer;
            for (int i = 0; i < inner.Count; i++)
            {
                Thing t = inner[i];
                if (t == null || t.Destroyed) continue;
                if (t.def != rec.def) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue;
                found = t;
                break;
            }
            if (found == null) return false;

            // 扣钱、清订单。付款失败时这里必须直接退出：
            // 否则会继续给一件已经退回地上的货打私有标签（白拿）。
            if (!OnInventoryAdded(p, found)) return false;

            if (EconomyPrivate.OwnerOf(found) == null)        // 打私有标签
            {
                EconomyPrivate.SetPrivate(found, p);
            }

            MergeOwnedStacks(p, found.def);                   // 同类私有合并成一堆

            DevLog.Msg("[DSH-ECONOMY] 结算完成：" + p.LabelShort + " 拿到 " + found.def.label);
            return true;
        }

        /// <summary>
        /// 把背包里同一个 def 的私有堆合并成一堆。
        /// 购买时 TryAddPrefix 禁止了合并（否则会并进公共堆、标签丢失），
        /// 代价是同类的私有堆也散成好几条，所以结算后手动收拢一次。
        /// </summary>
        private static void MergeOwnedStacks(Pawn p, ThingDef def)
        {
            if (p == null || def == null) return;
            if (p.inventory == null || p.inventory.innerContainer == null) return;

            IList<Thing> inner = p.inventory.innerContainer;
            Thing keep = null;
            for (int i = inner.Count - 1; i >= 0; i--)
            {
                Thing t = inner[i];
                if (t == null || t.Destroyed) continue;
                if (t.def != def) continue;
                if (EconomyPrivate.OwnerOf(t) != p) continue;
                if (keep == null) { keep = t; continue; }

                int room = keep.def.stackLimit - keep.stackCount;
                if (room <= 0) break;
                int move = Mathf.Min(room, t.stackCount);
                keep.stackCount += move;
                t.stackCount -= move;
                if (t.stackCount <= 0) t.Destroy();
            }
        }

        /// <summary>
        /// 把这个小人背包里"每一个 def 的私有堆"各自收拢成一堆。
        /// 不依赖订单：搬运、捡拾、救济、购买，任何来源进来的都会被执行到。
        /// </summary>
        public static void MergeAllOwned(Pawn p)
        {
            if (p == null || p.inventory == null || p.inventory.innerContainer == null) return;

            IList<Thing> inner = p.inventory.innerContainer;
            for (int i = inner.Count - 1; i >= 0; i--)
            {
                Thing t = inner[i];
                if (t == null || t.Destroyed) continue;
                if (EconomyPrivate.OwnerOf(t) != p) continue;
                MergeOwnedStacks(p, t.def);
            }
        }

        private static void MergeAllColonists()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            List<Pawn> crew = map.mapPawns.FreeColonists;
            for (int i = 0; i < crew.Count; i++) MergeAllOwned(crew[i]);
        }

        // ---------------- 清理 ----------------

                public static void Tick()
        {
            int now = Find.TickManager.TicksGame;

            // 每 600 tick（10 秒）兜底合并一次，任何来源进背包的私有堆都会收拢
            if (now - lastMergeTick >= 600)
            {
                lastMergeTick = now;
                MergeAllColonists();
            }

            if (pending.Count == 0) return;
            staleScratch.Clear();

            foreach (KeyValuePair<Pawn, Pending> kv in pending)
            {
                Pawn p = kv.Key;
                Pending rec = kv.Value;

                // 人没了 / 倒地 / 超时 → 放弃
                if (p == null || p.Dead || p.Downed || now - rec.tick > PendingTimeout)
                {
                    staleScratch.Add(p);
                    continue;
                }

                // 任务还在跑 → 什么都不做
                if (p.CurJobDef == JobDefOf.TakeInventory) continue;

                // 任务被顶掉了 → 重派，但有冷却和次数上限
                if (now - rec.lastTryTick < 60) continue;
                if (rec.retries >= 5) { staleScratch.Add(p); continue; }
                if (rec.stock == null || rec.stock.Destroyed || !rec.stock.Spawned || rec.stock.stackCount <= 0)
                {
                    staleScratch.Add(p);
                    continue;
                }
                // 抢不到就别重派了，直接放弃这一单，下一轮重新挑
                if (!p.CanReserve(rec.stock))
                {
                    staleScratch.Add(p);
                    continue;
                }

                rec.retries++;
                rec.lastTryTick = now;

                Job job = JobMaker.MakeJob(JobDefOf.TakeInventory, rec.stock);
                job.count = Mathf.Clamp(rec.count, 1, rec.stock.stackCount);
                job.haulMode = HaulMode.ToCellStorage;

                DevLog.Msg("[DSH-ECONOMY] 诊断 " + p.LabelShort
                    + " 禁止=" + rec.stock.IsForbidden(p)
                    + " 可预约=" + p.CanReserve(rec.stock)
                    + " 可到达=" + p.CanReach(rec.stock, PathEndMode.ClosestTouch, Danger.Some)
                    + " 手上有货=" + (p.carryTracker.CarriedThing != null));

                p.jobs.TryTakeOrderedJob(job, JobTag.Misc);

                DevLog.Msg("[DSH-ECONOMY] 派后=" + (p.CurJobDef == null ? "无" : p.CurJobDef.defName)
                    + " 有司机=" + (p.jobs.curDriver != null));

                DevLog.Msg("[DSH-ECONOMY] 重派：" + p.LabelShort + " 第 " + rec.retries + " 次");
            }
                for (int i = 0; i < staleScratch.Count; i++)
            {
                Pawn sp = staleScratch[i];
                if (sp != null)
                {
                    DevLog.Msg("[DSH-ECONOMY] 订单放弃：" + sp.LabelShort);
                    pending.Remove(sp);
                }
            }
            staleScratch.Clear();
        }
    }
}
