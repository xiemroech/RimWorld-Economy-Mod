using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 消费与囤积。
    ///
    /// 小人每小时有一次"消费冲动"，冲动强度由价格和通胀预期决定：
    ///   - 价格低（尤其是全面社会化后的免费）→ 冲动暴增，抢购、囤积、用不完就浪费
    ///   - 价格高 → 冲动下降，能不买就不买
    ///   - 通胀压力越高 → 预期涨价，提前囤货
    ///
    /// 买来的东西从公共库存里搬进个人背包并标成私有，属于"囤积"；
    /// 囤太多会定期浪费掉一部分（腐烂/闲置），这就是"生产过剩"的代价。
    /// </summary>
    public static class EconomyConsumption
    {
        // 基础冲动：每小时 12% 概率买一件
        public const float BaseImpulse = 0.4f;

        // 每小时统计一次，用来判断"为什么不买"
        public static int StatTry, StatBroke, StatNoSpace, StatJob, StatNoStock;

        // 每人"这一小时里没买成"的次数（目标未被满足），用于满意度里的"长期目标满足"门槛
        private static readonly Dictionary<Pawn, int> unmet = new Dictionary<Pawn, int>();

        public static void RecordUnmet(Pawn p)
        {
            if (p == null) return;
            int n;
            unmet.TryGetValue(p, out n);
            unmet[p] = Mathf.Min(99, n + 1);
        }

        public static int UnmetCount(Pawn p)
        {
            if (p == null) return 0;
            int n;
            return unmet.TryGetValue(p, out n) ? n : 0;
        }

        // ---------------- 存档（unmet 以前不存档，读档就清零） ----------------

        private static List<Pawn> unmetPawns = new List<Pawn>();
        private static List<int> unmetCounts = new List<int>();

        /// <summary>由 EconomySaveState.ExposeData 调用：让"没买成的次数"跟着存档走。</summary>
        public static void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                unmetPawns = new List<Pawn>(unmet.Keys);
                unmetCounts = new List<int>();
                for (int i = 0; i < unmetPawns.Count; i++)
                    unmetCounts.Add(unmet[unmetPawns[i]]);
            }

            Scribe_Collections.Look(ref unmetPawns, "dsh_unmetPawns", LookMode.Reference);
            Scribe_Collections.Look(ref unmetCounts, "dsh_unmetCounts", LookMode.Value);
        }

        /// <summary>读档收尾：把临时列表灌回字典。必须在 ResetRuntime 之后调用。</summary>
        public static void ApplyUnmetAfterLoad()
        {
            unmet.Clear();
            if (unmetPawns == null || unmetCounts == null) return;

            int n = Mathf.Min(unmetPawns.Count, unmetCounts.Count);
            for (int i = 0; i < n; i++)
            {
                Pawn p = unmetPawns[i];
                if (p == null || p.Destroyed) continue;
                unmet[p] = unmetCounts[i];
            }
        }

        /// <summary>每小时把没买成的计数减半，反映"最近这段时间"的满足情况。</summary>
        public static void DecayUnmet()
        {
            if (unmet.Count == 0) return;
            List<Pawn> keys = new List<Pawn>(unmet.Keys);
            for (int i = 0; i < keys.Count; i++)
            {
                Pawn k = keys[i];
                if (k == null || k.Dead || k.Destroyed) { unmet.Remove(k); continue; }
                int v = unmet[k] / 2;
                if (v <= 0) unmet.Remove(k); else unmet[k] = v;
            }
        }

        // 免费时的抢购倍数
        public const float FreeBoost = 5f;

        // 价格参考：价格等于这个数时冲动为 1.0
        public const float PriceReference = 15f;

        // 背包重量用到这个比例就不再买（按实时重量算，不按件数）
        public const float HoardFillRatio = 0.8f;

        // 私有物重量占负重超过这个比例，视为"囤积过多"，开始出现浪费
        public const float WasteFillRatio = 0.6f;

        // 有购买冲动但放不下时，有多大概率因此不满
        public const float NoSpacePenaltyChance = 0.5f;

        // 每天出现浪费的概率（私有物重量超过负重的 60% 时才开始判）
        public const float WasteChance = 0.35f;

        private static int lastConsumeTick = -99999;

    /// <summary>读档时清掉跨存档的静态计时器与缓存，避免时间倒退导致长时间不消费。</summary>
    public static void ResetRuntime()
    {
        lastConsumeTick = -99999;
        foodCacheMap = null;
        foodCacheTick = -99999;
        foodCache.Clear();
    }

        public static void Tick(Map map)
        {
            if (map == null) return;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastConsumeTick < 2500) return;   // 每游戏小时一次
            lastConsumeTick = now;

            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return;

            List<Pawn> colonists = new List<Pawn>(map.mapPawns.FreeColonists);
            // 没有容器的人先买，避免"有空间的人顺手把箱子买光、没箱子的人永远买不到"
            colonists.Sort((a, b) => BuyPriority(a, map).CompareTo(BuyPriority(b, map)));
            {
                string roster = "";
                for (int i = 0; i < colonists.Count; i++)
                {
                    if (i > 0) roster += "、";
                    roster += colonists[i].LabelShort + (colonists[i].Downed ? "(倒地)" : "");
                }
                DevLog.Msg("[TRACE] 本轮购买名单（" + colonists.Count + " 人）：" + roster);
            }
            for (int i = 0; i < colonists.Count; i++)
            {
                Buy(map, comp, colonists[i]);
            }

            // 每天结算一次浪费
            if (now % GenDate.TicksPerDay < 2500)
            {
                for (int i = 0; i < colonists.Count; i++) Waste(map, comp, colonists[i]);
            }
        }


        /// <summary>饿了就先去买：私有食物不足且饿了，立刻从市场/仓库买一份，别直接抓公共的。</summary>
        public static void UrgentFood(Map map, Pawn p)
        {
            if (map == null || p == null || p.Dead || p.Downed || p.needs == null || p.needs.food == null) return;
            if (p.needs.food.CurLevelPercentage > 0.35f) return;
            if (HasPrivateFood(p, map)) return;
            if (EconomyMarket.HasPending(p)) return;

            float price;
            Thing stock = EconomyMarket.PickFromMarket(map, p, "DshEco_Food", out price);
            if (stock != null)
            {
                int n = WantedAmount(p, stock.def, "DshEco_Food", map);
                if (EconomyMarket.StartPurchase(map, p, stock, n, price)) return;
            }
            ThingDef def;
            Thing src = PickStock(map, "DshEco_Food", p, out def, out price);
            if (src != null)
            {
                int n = WantedAmount(p, def, "DshEco_Food", map);
                EconomyMarket.StartPurchase(map, p, src, n, price);
            }
        }

        /// <summary>买箱子的优先度：没容器 0，快满了 1，其它 2。</summary>
        private static int BuyPriority(Pawn p, Map map)
        {
            float free = EconomyPrivateStorageBehavior.FreeRatio(p, map, out int _, out int _);
            if (free < 0f) return 0;              // 完全没有容器
            if (free <= PrivateFreeFloor) return 1;
            return 2;
        }


        /// <summary>远近战倾向：射击比格斗高 2 级以上就只买远程，反过来只买近战。</summary>
        private static bool WeaponFitsSkill(Pawn p, ThingDef d)
        {
            if (p == null || d == null || p.skills == null) return true;
            SkillRecord sr = p.skills.GetSkill(SkillDefOf.Shooting);
            SkillRecord mr = p.skills.GetSkill(SkillDefOf.Melee);
            int shoot = sr != null ? sr.Level : 0;
            int melee = mr != null ? mr.Level : 0;
            if (Mathf.Abs(shoot - melee) < 2) return true;
            bool wantsRanged = shoot > melee;
            if (d.IsRangedWeapon) return wantsRanged;
            if (d.IsMeleeWeapon) return !wantsRanged;
            return true;
        }

        /// <summary>取这件物品所属分类的社会化程度（0–1）。取不到时按 0 处理。</summary>
        private static float FreeDesireCurve(Map map, Thing thing, MapEconomyComponent comp)
        {
            if (map == null || thing == null || thing.def == null || comp == null) return 0f;
            EconomyCategoryDef cat = EconomyRegistry.CategoryOf(thing.def);
            if (cat == null) return 0f;
            return Mathf.Clamp01(comp.GetPublicization(cat) / 100f);
        }

        /// <summary>把购买欲望拆成明细（给界面看）。lines 里每行是"项 ×系数"，返回最终值。</summary>
        public static float DesireBreakdown(Pawn p, Map map, List<string> lines)
        {
            if (p == null || map == null) return 0f;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            float d = BaseImpulse;
            if (lines != null) lines.Add("基础冲动 " + (BaseImpulse * 100f).ToString("0") + "%");

            EconomyCategoryDef food = null;
            foreach (EconomyCategoryDef c in EconomyRegistry.Categories)
            {
                if (c != null && c.defName == "DshEco_Food") { food = c; break; }
            }
            float pub = (food != null && comp != null) ? comp.GetPublicization(food) : 50f;
            float pub01 = Mathf.Clamp01(pub / 100f);
            float pubF = 1f + (FreeBoost - 1f) * pub01 * pub01 * pub01;
            d *= pubF;
            if (lines != null) lines.Add("社会化 " + pub.ToString("0") + "% → ×" + pubF.ToString("0.00"));

            if (p.needs != null && p.needs.joy != null)
            {
                float joyF = 1f + (1f - p.needs.joy.CurLevelPercentage) * 0.9f;
                d *= joyF;
                if (lines != null) lines.Add("无聊（娱乐 " + (p.needs.joy.CurLevelPercentage * 100f).ToString("0") + "%）→ ×" + joyF.ToString("0.00"));
            }

            int cash = EconomyBank.PrivateSilver(map, p);
            int spendable = Mathf.Max(0, cash - EconomySavings.Of(p));
            float cashF = 1f + Mathf.Min(0.6f, spendable / 800f);
            d *= cashF;
            if (lines != null) lines.Add("可支配 " + spendable + " 银（私银 " + cash + " − 存款 " + EconomySavings.Of(p) + "）→ ×" + cashF.ToString("0.00"));

            if (EconomySavings.SavingForGoal(p, map))
            {
                d *= 0.6f;
                if (lines != null) lines.Add("正在攒钱（目标 " + EconomySavings.TargetOf(p, map) + " 银）→ ×0.60");
            }

            float final = Mathf.Clamp01(d);
            if (lines != null) lines.Add("= " + (final * 100f).ToString("0") + "%");
            return final;
        }

        /// <summary>当前购买欲望（0–1），只用于界面展示，不参与实际掷骰。</summary>
        public static float DesireOf(Pawn p, Map map)
        {
            if (p == null || map == null) return 0f;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            float d = BaseImpulse;

            EconomyCategoryDef food = null;
            foreach (EconomyCategoryDef c in EconomyRegistry.Categories)
            {
                if (c != null && c.defName == "DshEco_Food") { food = c; break; }
            }
            float pub01 = (food != null && comp != null) ? Mathf.Clamp01(comp.GetPublicization(food) / 100f) : 0.5f;
            d *= 1f + (FreeBoost - 1f) * pub01 * pub01 * pub01;

            if (p.needs != null && p.needs.joy != null)
                d *= 1f + (1f - p.needs.joy.CurLevelPercentage) * 0.9f;

            int spendable = Mathf.Max(0, EconomyBank.PrivateSilver(map, p) - EconomySavings.Of(p));
            d *= 1f + Mathf.Min(0.6f, spendable / 800f);

            if (EconomySavings.SavingForGoal(p, map)) d *= 0.6f;

            return Mathf.Clamp01(d);
        }

        private static int lastStatTick = -99999;

        private static void LogStatsIfDue()
        {
            int now = Find.TickManager.TicksGame;
            if (now - lastStatTick < 2500) return;
            lastStatTick = now;
            if (StatTry == 0 && StatBroke == 0 && StatNoSpace == 0 && StatJob == 0 && StatNoStock == 0) return;
            Log.Message("[CONSUME] 近一小时：找到货 " + StatTry + " 次，没找到货 " + StatNoStock
                        + "，缺钱 " + StatBroke + "，没地方 " + StatNoSpace + "，实际派活 " + StatJob);
            StatTry = StatBroke = StatNoSpace = StatJob = StatNoStock = 0;
            DecayUnmet();
        }

        private static void Buy(Map map, MapEconomyComponent comp, Pawn p)
        {
            if (p == null || p.Dead || p.Downed || p.inventory == null)
            {
                if (p != null && p.IsColonist)
                    DevLog.Msg("[TRACE] 买? " + p.LabelShort + " 跳过：死亡=" + p.Dead
                        + " 倒地=" + p.Downed + " 背包=" + (p.inventory != null));
                return;
            }
            LogStatsIfDue();

            // 自己还没有私有仓库 → 最高优先级买一个
            float freeRatio = EconomyPrivateStorageBehavior.FreeRatio(p, map, out _, out _);
            if (freeRatio < 0f)
            {
                DevLog.Msg("[TRACE] 买? " + p.LabelShort + " 走容器分支：没有私有仓库");
                TryBuyContainer(map, comp, p);
                return;
            }

            // 储物容量空余不足 10%，或者任何一格都放不下了 → 先补买容器
            if (freeRatio <= PrivateFreeFloor || !EconomyPrivateStorageBehavior.HasRoomForAny(p, map))
            {
                if (TryBuyContainer(map, comp, p)) return;

                DevLog.Msg("[TRACE] 买? " + p.LabelShort + " 走容器分支：空位不足 空位=" + freeRatio.ToString("0.00")
                    + " 也买不到容器");
                if (Rand.Chance(NoSpacePenaltyChance))
                {
                    EconomySatisfactionEvents.Add(p, "私有仓库放不下", -2f, 1);
                    EconomySatisfaction.Recompute(p);
                }
                return;
            }

            // 空位充足：不买容器，直接买消费品

            string wantCat = PreferredCategory(p);
            string goalCatNow = null;
            if (map != null)
            {
                goalCatNow = GoalCategory(p, map);
                if (goalCatNow != null) wantCat = goalCatNow;
            }

            // 自己买的食物还没吃完 → 先吃自己的，不重复买
            if (wantCat == "DshEco_Food" && HasPrivateFood(p, map))
            {
                DevLog.Msg("[TRACE] 不买 " + p.LabelShort + "：已有私有食物");
                return;
            }

            // 正在做饭：不要去买生食，等着吃刚做好的
            if (IsCooking(p))
            {
                DevLog.Msg("[TRACE] 不买 " + p.LabelShort + "：正在做饭");
                return;
            }

            // 无力支付：钱不够一份最基础的食物 → 不去捡生肉，直接领一份免费的基础食物
            if (wantCat == "DshEco_Food" && IsBrokeForBasicFood(map, p))
            {
                ReliefFood(map, comp, p);
                return;
            }

            // 市场摊位优先：市场有货就从市场拿；市场没有才退回普通仓库/地面
            Thing stock = EconomyMarket.PickFromMarket(map, p, wantCat, out float price);
            if (stock == null && wantCat == "DshEco_Food")
            {
                stock = PickFood(map, p, out ThingDef def, out price);
            }
            else if (stock == null)
            {
                stock = PickStock(map, wantCat, p, out ThingDef def2, out price);
                if (stock == null) stock = PickFood(map, p, out def2, out price);
            }
            // 想要的分类没货 → 依次退到其它日用分类，别直接放弃
            if (stock == null)
            {
                string[] fallback = { "DshEco_Food", "DshEco_Drugs", "DshEco_Goods", "DshEco_Medicine" };
                for (int fi = 0; fi < fallback.Length && stock == null; fi++)
                {
                    if (fallback[fi] == wantCat) continue;
                    if (fallback[fi] == "DshEco_Medicine" && !SelfTend(p) && !Whitelisted(null)) continue;
                    stock = EconomyMarket.PickFromMarket(map, p, fallback[fi], out price);
                    if (stock != null) break;
                    stock = PickStock(map, fallback[fi], p, out ThingDef fd, out price);
                    if (stock == null) stock = PickFood(map, p, out fd, out price);
                }
            }
            if (stock == null)
            {
                StatNoStock++;
                RecordUnmet(p);
                DevLog.Msg("[TRACE] 不买 " + p.LabelShort + "：没有可买的货（分类=" + wantCat + "）");
                return;
            }

            bool fromMarket = EconomyMarket.InMarket(stock);

            // 存款下限：多余现金先按比例存起来，目标是"想买的那件大件"
            EconomySavings.Accrue(p, map);

            bool free = price <= 0.05f;
            float impulse = BaseImpulse;

            // 购买欲随社会化平滑上升：0% = ×1，50% ≈ ×1.5，100% = ×5（三次曲线，无跳变）
            float pub01 = FreeDesireCurve(map, stock, comp);
            impulse *= 1f + (FreeBoost - 1f) * pub01 * pub01 * pub01;
            impulse *= 1f + Mathf.Max(0f, EconomyInflation.Pressure(map));

            // 消费欲：娱乐越缺越想花钱（无聊的人会乱买）
            if (p.needs != null && p.needs.joy != null)
                impulse *= 1f + (1f - p.needs.joy.CurLevelPercentage) * 0.9f;
            // 手里钱越多越舍得花（最多 +60%）
            int pocket = Mathf.Max(0, EconomyBank.PrivateSilver(map, p) - EconomySavings.Of(p));
            impulse *= 1f + Mathf.Min(0.6f, pocket / 800f);

            // 负担感：这件东西占我总财富的比例越大，越舍不得买
            {
                float wealth = TaxPopulation.PawnWealth(p);
                if (wealth > 1f && price > 0.01f)
                {
                    float burden = price / (wealth * 0.02f);
                    impulse *= Mathf.Clamp(1f / Mathf.Max(0.05f, burden), 0.25f, 1.5f);
                }
            }

            // CE 弹药：没对应武器不买；总量 10 组封顶；买得越多欲望越低
            if (CEAmmo.IsAmmoDef(stock.def))
            {
                if (!CEAmmo.OwnsWeaponFor(p, stock.def)) return;
                int owned = CEAmmo.OwnedStacks(p, stock.def, map);
                if (owned >= CEAmmo.MaxStacks) return;
                impulse *= 1f / (1f + owned * 0.25f);
            }

            // 攒够钱的目标大件不打折，直接买（否则永远"只攒不买"）
            if (goalCatNow != null) impulse = 1f;

            if (!Rand.Chance(Mathf.Clamp01(impulse)))
            {
                DevLog.Msg("[TRACE] 不买 " + p.LabelShort + "：购买欲没掷中（欲望=" + impulse.ToString("0.00") + "）");
                return;
            }

            // 冲动成立、真要买了，才检查放不放得下
            if (!HasSpace(p))
            {
                StatNoSpace++;
                if (Rand.Chance(NoSpacePenaltyChance))
                {
                    EconomySatisfactionEvents.Add(p, "想买但没地方放", -2f, 1);
                    EconomySatisfaction.Recompute(p);
                }
                return;
            }

            int buyCount = WantedAmount(p, stock.def, wantCat, map);
            if (buyCount <= 0) return;

            int spendable = EconomyBank.PrivateSilver(map, p) - EconomySavings.Of(p);

            // 目标大件：攒够就该买，动用存款（不然永远"只攒不买"）
            bool buyingGoal = false;
            if (map != null)
            {
                string gc = GoalCategory(p, map);
                if (gc != null && wantCat == gc)
                {
                    buyingGoal = true;
                    spendable = EconomyBank.PrivateSilver(map, p);
                }
            }

            // 真饿了就动用存款买吃的，不能为了攒钱饿死
            if (wantCat == "DshEco_Food" && p.needs != null && p.needs.food != null && p.needs.food.CurLevelPercentage < 0.35f)
                spendable = EconomyBank.PrivateSilver(map, p);

            // 份数按"现金 ÷ 单价"收口：一次最多花掉可支配现金的 30%，别把殖民地口粮一把买光
            if (price > 0.01f)
            {
                int affordable = Mathf.FloorToInt(spendable / price);
                int budgetCap = Mathf.Max(1, Mathf.FloorToInt(spendable * 0.3f / price));
                buyCount = Mathf.Min(buyCount, Mathf.Min(affordable, budgetCap));
            }
            if (buyCount <= 0)
            {
                StatBroke++;
                RecordUnmet(p);
                DevLog.Msg("[TRACE] 不买 " + p.LabelShort + "：份数不足（价格=" + price.ToString("0.00")
                    + " 可用=" + spendable + "）");
                return;
            }
            StatTry++;
            int costCheck = Mathf.CeilToInt(price * buyCount);
            if (costCheck > 0 && spendable < costCheck)
            {
                StatBroke++;
                RecordUnmet(p);
                DevLog.Msg("[TRACE] 不买 " + p.LabelShort + "：钱不够（要=" + costCheck + " 有=" + spendable + "）");
                return;
            }

            // 只派"去拿货"的 job；钱等货真正进背包时再扣（见 EconomyMarket.OnInventoryAdded）
            bool started = EconomyMarket.StartPurchase(map, p, stock, buyCount, free ? 0f : price);
            if (started)
            {
                StatJob++;
                if (buyingGoal) EconomySavings.Take(p, costCheck);
            }
            if (started && !fromMarket)
            {
                // 市场没货、只能去仓库/地上翻找 → 记一笔「繁复的寻找」，−2，可叠加
                EconomySatisfactionEvents.Add(p, "繁复的寻找（去仓库翻找货品）", -1f, 3);
                EconomySatisfaction.Recompute(p);
            }
        }


        // ---------------- 食物等级与恩格尔系数 ----------------
        // 0 = 生食、1 = 低级制成（简单/精致以下的烹饪）、2 = 高级制成（精致及以上）
        public const float EngelPoor = 0.5f;   // 恩格尔系数高于此：只买生食
        public const float EngelMid = 0.25f;   // 高于此：低级制成；低于此才考虑高级

        /// <summary>食物等级：看原版 preferability。</summary>
        public static int FoodTierOf(ThingDef def)
        {
            if (def == null || def.ingestible == null) return 0;
            FoodPreferability pr = def.ingestible.preferability;
            if (pr >= FoodPreferability.MealFine) return 2;
            if (pr >= FoodPreferability.MealAwful) return 1;
            return 0;
        }

        // ── 财富系数：相对殖民地中位私产的动态倍率 ──
        private static Map wfMap;
        private static int wfTick = -99999;
        private static float wfMedian = 1f;

        public static float WealthFactor(Pawn p, Map map)
        {
            if (p == null || map == null) return 1f;
            if (map != wfMap || GenTicks.TicksGame - wfTick > 2500)
            {
                wfMap = map; wfTick = GenTicks.TicksGame;
                List<float> vals = new List<float>();
                List<Pawn> crew = map.mapPawns.FreeColonists;
                for (int i = 0; i < crew.Count; i++)
                {
                    Pawn q = crew[i];
                    if (q == null || q.Dead) continue;
                    vals.Add(TaxPopulation.PawnWealth(q));
                }
                if (vals.Count == 0) wfMedian = 1f;
                else { vals.Sort(); wfMedian = vals[vals.Count / 2]; }
            }
            if (wfMedian <= 1f) return 1f;
            float ratio = TaxPopulation.PawnWealth(p) / wfMedian;
            return Mathf.Clamp(0.75f + ratio * 0.35f, 0.75f, 1.6f);
        }

        /// <summary>这个人当前买得起、也愿意买的最高食物等级。</summary>
        public static int PreferredFoodTier(Pawn p, Map map)
        {
            if (p == null || map == null) return 0;

            float income = EconomyLedger.GetIncome(p);
            if (income <= 0.01f) income = EconomyLedger.GetLastIncome(p);   // 本期还没发薪就用上期
            // 家底也算“购买力”：私产每 20 银折 1 银/期收入，富人即使这期没工资也吃得更好
            income = Mathf.Max(income, TaxPopulation.PawnWealth(p) / 20f);
            float price = CheapestFoodPrice(map, 0);
            // 一天两顿的基本伙食占本期收入的比例，就是这里的恩格尔系数
            float engel = income > 0.01f ? (price * 2f) / income : 1f;

            int tier = engel > EngelPoor ? 0 : (engel > EngelMid ? 1 : 2);

            // 现金决定"敢不敢吃贵的"：手里没钱时，恩格尔再低也只吃生食
            int silver = EconomyBank.PrivateSilver(map, p);
            int spendable = Mathf.Max(0, silver - EconomySavings.Of(p));
            int cashTier = spendable >= 100 ? 2 : (spendable >= 40 ? 1 : 0);
            if (tier > cashTier) tier = cashTier;

            // 买不起就往下退
            while (tier > 0)
            {
                float cost = CheapestFoodPrice(map, tier);
                if (cost > 0.01f && silver >= cost) break;
                tier--;
            }
            return tier;
        }


        // 地图上的"在售食物"缓存：每小时重建一次，避免每个小人每顿饭都全图扫一遍。
        // （原先每个小人每小时要全图扫 6 次并逐个 WillEat，几十人时就明显卡顿。）
        private static Map foodCacheMap;
        private static int foodCacheTick = -99999;
        private static readonly List<Thing> foodCache = new List<Thing>();

        private static List<Thing> FoodMarket(Map map)
        {
            int now = Find.TickManager.TicksGame;
            if (foodCacheMap == map && now - foodCacheTick < 2500) return foodCache;

            foodCache.Clear();
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.def == null || t.Destroyed) continue;
                if (IsCandidate(t, "DshEco_Food", null)) foodCache.Add(t);
            }
            foodCacheMap = map;
            foodCacheTick = now;
            return foodCache;
        }

        private static float CheapestFoodPrice(Map map, int tier)
        {
            float best = float.MaxValue;
            List<Thing> all = FoodMarket(map);
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (FoodTierOf(t.def) != tier) continue;
                float price = EconomyPrice.UnitPrice(t, map);
                if (price < best) best = price;
            }
            return best == float.MaxValue ? 0f : best;
        }



        /// <summary>
        /// 这个小人到底吃不吃这东西：原版 FoodUtility.WillEat 会考虑种族食谱（食肉/食草/特殊饮食）、
        /// 戒律、头衔、食物政策、mod 追加的禁忌，所以 mod 食物与 mod 种族自动兼容。
        /// </summary>
        private static bool Eatable(Pawn p, Thing t)
        {
            if (p == null || t == null || t.def == null) return true;
            if (t.def.ingestible == null) return false;
            try
            {
                // getter 传自己：拿的是"本人吃"的判定，不是"喂别人"
                return p.WillEat(t, p);
            }
            catch
            {
                return true;   // 判定异常时不要因此让人饿死，放行
            }
        }

        /// <summary>存放组的物主（堆放区看 zone、存储建筑看 building）。</summary>
        private static Pawn OwnerOfStorageGroup(SlotGroup g)
        {
            if (g == null) return null;
            Zone zone = g.parent as Zone;
            if (zone != null) return EconomyRealEstate.OwnerOf(zone);
            Building b = g.parent as Building;
            if (b != null) return EconomyRealEstate.OwnerOfBuilding(b);
            return null;
        }

        /// <summary>私有物里已经有吃得上的东西（营养合计 ≥ 0.3）就先吃掉，不再买。</summary>
        /// <summary>统计地图上料斗/营养膏机里的可食营养（公共食物，也算"有得吃"）。</summary>
        private static float HopperNutrition(Map map, Pawn p)
        {
            if (map == null) return 0f;
            float sum = 0f;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b == null || b.Destroyed) continue;
                if (b.def == null || b.def.defName != "Hopper") continue;
                IThingHolder holder = b as IThingHolder;
                if (holder == null) continue;
                ThingOwner owner = holder.GetDirectlyHeldThings();
                if (owner == null) continue;
                if (owner == null) continue;
                foreach (Thing t in owner)
                {
                    if (t == null || t.def == null) continue;
                    if (!t.def.IsNutritionGivingIngestible) continue;
                    if (p != null && !Eatable(p, t)) continue;
                    sum += t.GetStatValue(StatDefOf.Nutrition) * t.stackCount;
                }
            }
            return sum;
        }

        /// <summary>营养膏不属于"囤粮"：好吃程度太低，不计入库存判断，但仍可单独购买。</summary>
        public static bool IsPasteDef(ThingDef def)
        {
            if (def == null) return false;
            if (def == ThingDefOf.MealNutrientPaste) return true;
            string n = def.defName ?? string.Empty;
            return n.IndexOf("NutrientPaste", System.StringComparison.OrdinalIgnoreCase) >= 0
                || n.IndexOf("PasteMeal", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>存粮目标营养：基准 6 顿，粮越便宜囤得越多（3–12 顿），再按财富系数放大。</summary>
        public static float FoodTargetNutrition(Pawn p, Map map)
        {
            float price = 0f;
            if (map != null) price = CheapestFoodPrice(map, 0);
            float coef = price > 0.01f ? Mathf.Clamp(30f / price, 0.4f, 3f) : 1f;
            float meals = Mathf.Clamp(6f * (0.55f + 0.45f * coef), 3f, 12f);
            if (p != null && map != null && EconomySavings.SavingForGoal(p, map)) meals = Mathf.Min(meals, 3f);
            return meals * 0.9f * (p != null && map != null ? WealthFactor(p, map) : 1f);
        }

        /// <summary>
        /// 还要不要继续下单。
        /// 囤粮目标仍按"财富 × 粮价"放大（FoodTargetNutrition），
        /// 但目标不可能超过"他买得起的量"：只买得起一份的穷人，有一口就算有，
        /// 否则他永远凑不满 2.7 营养的储备门槛，每小时反复去买/去领。
        /// </summary>
        public static bool HasPrivateFood(Pawn p, Map map)
        {
            float have = StoredNutrition(p, map, false);
            if (have <= 0.05f) return false;

            float target = FoodTargetNutrition(p, map);

            // 用"买得起的份数"给储备目标封顶
            if (map != null)
            {
                float price = CheapestFoodPrice(map, 1);
                if (price > 0.05f)
                {
                    int affordable = Mathf.FloorToInt(EconomyBank.PrivateSilver(map, p) / price);
                    if (affordable <= 1) return true;          // 只买得起一份：有一口就够
                    float cap = affordable * 0.9f;
                    if (cap < target) target = cap;
                }
            }

            return have >= target;
        }

        /// <summary>私人存粮的营养总量；includePaste 决定营养膏算不算存粮。</summary>
        public static float StoredNutrition(Pawn p, Map map, bool includePaste)
        {
            if (p == null) return 0f;
            float nutrition = 0f;

            if (p.inventory != null)
            {
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (t == null || t.def == null) continue;
                    if (EconomyPrivate.OwnerOf(t) != p) continue;
                    if (t.def.IsNutritionGivingIngestible && (includePaste || !IsPasteDef(t.def)) && Eatable(p, t)) nutrition += t.GetStatValue(StatDefOf.Nutrition) * t.stackCount;
                }
            }

            if (map != null && map.haulDestinationManager != null)
            {
                foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                {
                    if (g == null || g.CellsList == null) continue;
                    if (OwnerOfStorageGroup(g) != p) continue;   // 只看自己的仓库
                    for (int i = 0; i < g.CellsList.Count; i++)
                    {
                        List<Thing> here = g.CellsList[i].GetThingList(map);
                        for (int k = 0; k < here.Count; k++)
                        {
                            Thing t = here[k];
                            if (t == null || t.def == null) continue;
                            if (EconomyPrivate.OwnerOf(t) != p) continue;
                            if (t.def.IsNutritionGivingIngestible && (includePaste || !IsPasteDef(t.def)) && Eatable(p, t)) nutrition += t.GetStatValue(StatDefOf.Nutrition) * t.stackCount;
                        }
                    }
                    if (nutrition >= 5.4f) break;
                }
            }
            // 营养膏机的料斗不是存储建筑，但里面的食物同样"能吃"：按同权计入已有食物
            if (nutrition < 5.4f) nutrition += HopperNutrition(map, p);

            return nutrition;
        }

        /// <summary>
        /// 挑食物：在自己买得起的等级里取最高档；同档内按原版 FoodOptimality 加权随机，
        /// 这样种族/戒律/口味偏好（mod 追加的也算在内）会自然被优先选中。
        /// </summary>
        private static Thing PickFood(Map map, Pawn p, out ThingDef def, out float price)
        {
            def = null;
            price = 0f;
            int maxTier = PreferredFoodTier(p, map);
            // 有营养膏机就别买生食了（现成的免费/半价饭在那儿），只买做好的饭
            int minTier = EconomyPasteDispenser.HasDispenser(map) ? 1 : 0;
            if (maxTier < minTier) return null;
            int silver = EconomyBank.PrivateSilver(map, p);
            List<Thing> all = FoodMarket(map);

            List<Thing> pool = new List<Thing>();
            List<float> opts = new List<float>();

            for (int tier = maxTier; tier >= 0; tier--)
            {
                pool.Clear();
                opts.Clear();
                float minOpt = float.MaxValue;

                for (int i = 0; i < all.Count; i++)
                {
                    Thing t = all[i];
                    if (t == null || t.Destroyed || !t.Spawned || t.stackCount <= 0) continue;
                    if (EconomyPrivate.IsPrivate(t)) continue;
                    if (!Eatable(p, t)) continue;
                    if (FoodTierOf(t.def) != tier || tier < minTier) continue;
                    if (!Affordable(t, p, map, silver)) continue;
                    if (!p.CanReserve(t)) continue;

                    float opt = 0f;
                    try { opt = FoodUtility.FoodOptimality(p, t, t.def, 0f); }
                    catch { opt = 0f; }

                    pool.Add(t);
                    opts.Add(opt);
                    if (opt < minOpt) minOpt = opt;
                }

                if (pool.Count == 0) continue;

                // 最优的那份权重最高；同档里"更爱吃"的出现概率更大，但不是必定
                float total = 0f;
                for (int i = 0; i < pool.Count; i++)
                {
                    float w = Mathf.Max(0.05f, opts[i] - minOpt + 0.1f);
                    opts[i] = w;
                    total += w;
                }

                float roll = Rand.Value * total;
                Thing chosen = pool[pool.Count - 1];
                for (int i = 0; i < pool.Count; i++)
                {
                    roll -= opts[i];
                    if (roll <= 0f) { chosen = pool[i]; break; }
                }

                def = chosen.def;
                price = EconomyPrice.UnitPrice(chosen, map);
                return chosen;
            }
            return null;
        }

        private static bool Affordable(Thing t, Pawn p, Map map, int silver)
        {
            float unit = EconomyPrice.UnitPrice(t, map);
            if (unit <= 0.05f) return true;
            int n = WantedAmount(p, t.def, "DshEco_Food", map);
            return silver >= Mathf.CeilToInt(unit * n);
        }

            public static bool IsBrokeForBasicFood(Map map, Pawn p)
        {
            int silver = EconomyBank.PrivateSilver(map, p);
            float cheapest = CheapestFoodPrice(map, 1);

            // 诊断：私产为什么是 0
            int owned = EconomyPrivate.OwnedItems(p, map).Count;
            string bag = "";
            if (p.inventory != null && p.inventory.innerContainer != null)
            {
                IList<Thing> inner = p.inventory.innerContainer;
                for (int i = 0; i < inner.Count; i++)
                {
                    Thing t = inner[i];
                    if (t.def != ThingDefOf.Silver) continue;
                    bag += "[" + t.stackCount + "银 组件=" + (EconomyPrivate.TagOf(t) != null)
                        + " 归属=" + (EconomyPrivate.OwnerOf(t) == null
                            ? "无" : EconomyPrivate.OwnerOf(t).LabelShort) + "]";
                }
            }
            DevLog.Msg("[DSH-ECONOMY] 私产诊断 " + p.LabelShort + " 私银=" + silver
                + " 私有物=" + owned + " 身上银=" + (bag.Length == 0 ? "无" : bag));

            if (cheapest <= 0.05f) return false;
            return silver < Mathf.CeilToInt(cheapest);
        }
        
        /// <summary>
        /// 领一份免费的基础食物。
        /// 排序：普通食物（档次越低越优先）→ 厌恶食品 → 生肉（全图都没做好的饭时才拿）。
        /// "厌恶"不自己判断，直接用原版 FoodOptimality —— 虫肉、人肉、戒律禁忌、种族食谱
        /// 全在里面，mod 追加的禁忌也自动算进去。
        /// </summary>
        private static bool ReliefFood(Map map, MapEconomyComponent comp, Pawn p)
        {
            int prefTier = PreferredFoodTier(p, map);

            Thing best = null;
            float bestScore = float.MinValue;
            List<Thing> all = FoodMarket(map);
            bool anyCooked = false;

            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed || !t.Spawned || t.stackCount <= 0) continue;
                if (EconomyPrivate.IsPrivate(t)) continue;
                if (!Eatable(p, t)) continue;

                int tier = FoodTierOf(t.def);
                float opt;
                try { opt = FoodUtility.FoodOptimality(p, t, t.def, 0f); }
                catch { opt = 0f; }

                // 生肉：只在地图上根本没有做好的饭时，留作最后一手
                if (tier < 1)
                {
                    if (!anyCooked) best = t;
                    continue;
                }
                anyCooked = true;

                // 普通食物随档次降权（档次越高越不该吃，因为要花钱）；
                // 厌恶食品的原版评分本来就是负的，自然被压到普通食物下面。
                float score = opt - tier * 3f;
                if (score > bestScore) { bestScore = score; best = t; }
            }

            if (best == null) { StatBroke++; RecordUnmet(p); return false; }

            int actualTier = FoodTierOf(best.def);
            int n = Mathf.Clamp(WantedAmount(p, best.def, "DshEco_Food", map), 1, best.stackCount);
            if (!EconomyMarket.StartPurchase(map, p, best, n, 0f, true)) 
            {
                StatBroke++;
                RecordUnmet(p);
                return false;
            }

            StatJob++;

            // 扣分 = 1 + 2 × 落差档位。落差是他"平时吃得起"与"实际吃到"的差，
            // 富人掉到基础食物扣得多，本来就吃基础食物的人扣得少。
            float gap = Mathf.Max(0f, prefTier - actualTier);
            float pen = -(1f + 2f * gap);
            string why;
            // 标签统一成一条：带食物名/档位的话，每换一种食物就是另一个事件，永远叠不起来
            if (actualTier < 1) pen -= 2f;
            why = "道德指责（无力支付）";
            EconomySatisfactionEvents.Add(p, why, pen, 3);
            EconomySatisfaction.Recompute(p);
            return true;
        }

        /// <summary>从地图库存里挑一件"消费品"：优先食物、成瘾品、药品，其次其它已归类物品。</summary>
        // 小人自己私有仓库的空余格子低于 10% 时，停止一切购买
        public const float PrivateFreeFloor = 0.1f;

        /// <summary>买容器：把一座公共储物建筑的所有权买下来。公共剩余不足 10% 时停止。</summary>


        /// <summary>是否"真的没地方放"——这时候才买容器；有空余就不买新的。</summary>
        public static bool NeedsContainer(Pawn p, Map map)
        {
            if (p == null || map == null) return false;
            float free = EconomyPrivateStorageBehavior.FreeRatio(p, map, out _, out _);
            if (free < 0f) return true;
            if (free <= PrivateFreeFloor) return true;
            return !EconomyPrivateStorageBehavior.HasRoomForAny(p, map);
        }

        /// <summary>地图上最便宜的在售容器（买不起也算，用于攒钱目标）；没有则 null。</summary>
        public static ThingDef CheapestForSaleContainer(Map map)
        {
            if (map == null || map.listerBuildings == null) return null;
            ThingDef best = null;
            float bestPrice = float.MaxValue;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                Building_Storage st = b as Building_Storage;
                if (st == null || st is Building_Market) continue;
                if (EconomyRealEstate.OwnerOfBuilding(st) != null) continue;
                if (!EconomyRealEstate.IsForSale(st)) continue;
                float price = EconomyPrice.UnitPrice(st.def, map);
                if (price < bestPrice) { bestPrice = price; best = st.def; }
            }
            return best;
        }

        private static bool TryBuyContainer(Map map, MapEconomyComponent comp, Pawn p)
        {
            if (map.listerBuildings == null) return false;
            List<Building> all = new List<Building>();
            foreach (Building b in map.listerBuildings.allBuildingsColonist) all.Add(b);
            var pub = new List<Building_Storage>();
            int total = 0;
            for (int i = 0; i < all.Count; i++)
            {
                Building_Storage st = all[i] as Building_Storage;
                if (st == null) continue;
                if (st is Building_Market) continue;   // 市场是商店，不是可以买走的私有仓库
                total++;
                if (EconomyRealEstate.OwnerOfBuilding(st) == null && EconomyRealEstate.IsForSale(st)) pub.Add(st);
            }
            if (total == 0 || pub.Count == 0) return false;

            // 先挑买得起的，再挑便宜的
            Building_Storage pick = null;
            float best = float.MaxValue;
            for (int i = 0; i < pub.Count; i++)
            {
                float price = EconomyPrice.UnitPrice(pub[i].def, map);
                if (price > best) continue;
                int cost = Mathf.CeilToInt(price);
                if (cost > 0 && EconomyBank.PrivateSilver(map, p) < cost) continue;
                best = price;
                pick = pub[i];
            }
            if (pick == null) return false;

            int finalCost = Mathf.CeilToInt(EconomyPrice.UnitPrice(pick.def, map));
            if (finalCost > 0)
            {
                // TakePrivateSilver 只是把银币的"私有"标签摘掉，钱留在原地变成公共银，
                // 等于付给了殖民地；再调用 GiveColonySilver 会凭空多出一份钱（通胀），所以不能加。
                if (EconomyBank.TakePrivateSilver(map, p, finalCost) < finalCost) return false;
            }

            EconomyRealEstate.SetBuildingOwner(pick, p);
            comp.consumptionBought++;
            Log.Message("[CONSUME] " + p.LabelShort + " 买下容器 " + pick.def.label
                + "（" + finalCost + " 银），公共容器剩余 " + (pub.Count - 1) + "/" + total);
            return true;
        }

        /// <summary>这个人有没有私有仓库；有则顺便返回格子总数与已占用数。</summary>
        private static bool PrivateCells(Pawn p, Map map, out int cells, out int used)
        {
            cells = 0;
            used = 0;
            if (p == null || map == null || map.listerBuildings == null) return false;

            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                Building_Storage st = b as Building_Storage;
                if (st == null) continue;
                if (EconomyRealEstate.OwnerOfBuilding(st) != p) continue;

                SlotGroup g = st.GetSlotGroup();
                if (g == null || g.CellsList == null) continue;
                for (int c = 0; c < g.CellsList.Count; c++)
                {
                    cells++;
                    IntVec3 cell = g.CellsList[c];
                    List<Thing> things = map.thingGrid.ThingsListAt(cell);
                    for (int t = 0; t < things.Count; t++)
                    {
                        if (things[t].def.category == ThingCategory.Item) { used++; break; }
                    }
                }
            }
            return cells > 0;
        }

        /// <summary>这次要买多少：食物按营养算一天的量，其它按 1–2 件。</summary>
        private static int WantedAmount(Pawn p, ThingDef def, string category, Map map = null)
        {
            if (def == null) return 1;
            // 衣物：先补齐"内衣/中衣/外衣"三层，每层至少一件；层齐了才按价格囤
            if (category == "DshEco_Apparel" && p != null && !EconomyItemStock.LayerCovered(p, def))
                return 1;

            if (category == "DshEco_Food")
            {
                float nutrition = def.GetStatValueAbstract(StatDefOf.Nutrition);
                if (nutrition <= 0.01f) return 1;
                // 按"还差几顿"决定买多少：存粮（含营养膏）够 6 顿就不买
                if (map != null && p != null)
                {
                    float target = FoodTargetNutrition(p, map);
                    float deficit = target - StoredNutrition(p, map, true);
                    if (deficit <= 0.01f) return 0;
                    int need = Mathf.CeilToInt(deficit / nutrition);

                    // 全局上限：手里现金的一半是"口粮封顶"，超过就一份都不再买
                    // （不然这一小时买 2 份、下一小时接着买，最后还是一个把市场吃空）
                    float unit = EconomyPrice.UnitPrice(def, map);
                    if (unit > 0.01f)
                    {
                        int cash = EconomyBank.PrivateSilver(map, p) - EconomySavings.Of(p);
                        int allowMeals = Mathf.FloorToInt(Mathf.Max(0f, cash * 0.5f) / unit);
                        int haveMeals = Mathf.RoundToInt(StoredNutrition(p, map, true) / nutrition);
                        need = Mathf.Min(need, allowMeals - haveMeals);
                    }

                    // 单次购买上限：食物按"份"算（别把殖民地口粮一把抓光），
                    // 弹药/耗材按堆叠量算——CE 的弹药一堆上百，只买 4 发等于愚公移山。
                    int cap;
                    if (IsPasteDef(def)) cap = 6;
                    else if (def != null && def.ingestible != null) cap = 4;
                    else cap = Mathf.Clamp(def != null ? def.stackLimit : 75, 1, 150);
                    return Mathf.Clamp(need, 0, cap);
                }
                int n = Mathf.CeilToInt(0.9f / nutrition);
                return Mathf.Clamp(n, 1, 10);
            }
            if (CEAmmo.IsAmmoDef(def) && p != null && map != null)
            {
                int owned = CEAmmo.OwnedStacks(p, def, map);
                if (owned >= CEAmmo.MaxStacks) return 0;
                return 1;   // 一次买一组，慢慢补到预设
            }
            if (map != null && p != null)
            {
                int need = EconomyItemStock.Needed(p, def, map);
                if (need <= 0) return 0;
                // 与食物同样的全局封顶：这件东西最多占有可支配现金的一半
                float unit = EconomyPrice.UnitPrice(def, map);
                if (unit > 0.01f)
                {
                    int cash = EconomyBank.PrivateSilver(map, p) - EconomySavings.Of(p);
                    int affordable = Mathf.FloorToInt(Mathf.Max(0f, cash * 0.5f) / unit);
                    need = Mathf.Min(need, affordable);
                }
                return Mathf.Clamp(need, 0, EconomyItemStock.MaxCount(def));
            }
            if (category == "DshEco_Medicine") return 2;
            if (category == "DshEco_Drugs") return 2;
            return 1;
        }

        /// <summary>按需求决定这次想买哪一类：饿→食物，娱乐低→成瘾品，受伤→医药，平时→食物/制成品。</summary>
        /// <summary>存款够目标价时，优先把这件大件买下来。</summary>
        public static string GoalCategory(Pawn p, Map map)
        {
            ThingDef g = EconomySavings.Goal(p);
            if (g == null || map == null) return null;
            int price = EconomySavings.GoalPrice(g, map);
            if (price <= 0) return null;
            if (EconomyBank.PrivateSilver(map, p) < price) return null;
            EconomyCategoryDef cat = EconomyRegistry.CategoryOf(g);
            return cat != null ? cat.defName : null;
        }

        private static string PreferredCategory(Pawn p)
        {
            if (p.needs != null)
            {
                if (p.needs.food != null && p.needs.food.CurLevel < 0.55f) return "DshEco_Food";
                if (p.needs.joy != null && p.needs.joy.CurLevel < 0.4f) return "DshEco_Drugs";
            }
            if (p.health != null && p.health.hediffSet != null && p.health.hediffSet.hediffs != null)
            {
                List<Hediff> hediffs = p.health.hediffSet.hediffs;
                for (int i = 0; i < hediffs.Count; i++)
                {
                    if (hediffs[i] is Hediff_Injury) return "DshEco_Medicine";
                }
            }
            // CE 装备预设优先：先补预设里缺的件
            Dictionary<ThingDef, int> loadout = CEAmmo.LoadoutMap(p);
            if (loadout.Count > 0)
            {
                foreach (KeyValuePair<ThingDef, int> kv in loadout)
                {
                    if (EconomyItemStock.CountOwned(p, kv.Key) >= kv.Value) continue;
                    EconomyCategoryDef lc = EconomyRegistry.CategoryOf(kv.Key);
                    if (lc != null) return lc.defName;
                }
            }

            // 没武器先补武器；衣物不够再补衣物（都按价格决定要几件）
            if (p.equipment != null && p.equipment.Primary == null) return "DshEco_Weapon";
            if (p.apparel != null && EconomyItemStock.MissingLayers(p) > 0) return "DshEco_Apparel";
            return Rand.Chance(0.7f) ? "DshEco_Food" : "DshEco_Goods";
        }

        /// <summary>正在做饭（含灶台 bill 烹饪）：这时候别去买生食，等着吃成品。</summary>
        public static bool IsCooking(Pawn p)
        {
            if (p == null || p.jobs == null || p.jobs.curJob == null) return false;
            Job job = p.jobs.curJob;
            if (job.def == null) return false;
            if (job.def.defName == "Cook" || job.def.defName == "DoBill") return true;
            if (job.bill != null && job.bill.recipe != null && job.bill.recipe.products != null)
            {
                for (int i = 0; i < job.bill.recipe.products.Count; i++)
                {
                    ThingDef prod = job.bill.recipe.products[i].thingDef;
                    if (prod != null && prod.IsIngestible) return true;
                }
            }
            return false;
        }

        /// <summary>背包还有没有空间：按实时重量算（装备 + 背包总重 vs 负重上限）。</summary>
        private static bool HasSpace(Pawn p)
        {
            if (p == null) return false;
            float cap = MassUtility.Capacity(p);
            if (cap <= 0f) return false;
            float used = MassUtility.GearAndInventoryMass(p);
            return used < cap * HoardFillRatio;
        }

        /// <summary>私有物的总重量（不算货币）。</summary>
        private static float PrivateMass(Pawn p)
        {
            if (p == null || p.inventory == null) return 0f;
            float m = 0f;
            foreach (Thing t in p.inventory.innerContainer)
            {
                if (t == null || t.def == null) continue;
                if (EconomyRegistry.IsCurrency(t.def)) continue;
                if (!EconomyPrivate.IsPrivate(t)) continue;
                m += t.def.GetStatValueAbstract(StatDefOf.Mass) * t.stackCount;
            }
            return m;
        }

        /// <summary>是否开了自我治疗：没开的人不许买医药类（白名单除外）。</summary>
        public static bool SelfTend(Pawn p)
        {
            return p != null && p.playerSettings != null && p.playerSettings.selfTend;
        }

        public static bool Whitelisted(ThingDef def)
        {
            DshEconomySettings s = DshEconomyMod.Settings;
            return def != null && s != null && s.medicalWhitelist != null
                && s.medicalWhitelist.Contains(def.defName);
        }

        private static Thing PickStock(Map map, string categoryDefName, Pawn p, out ThingDef def, out float price)
        {
            def = null;
            price = 0f;

            List<Thing> all = map.listerThings.AllThings;
            int count = 0;
            for (int i = 0; i < all.Count; i++)
            {
                if (!IsCandidate(all[i], categoryDefName, p)) continue;
                count++;
            }
            if (count == 0) return null;

            // 衣物：缺的层优先，其次冷天挑保暖值高的
            if (categoryDefName == "DshEco_Apparel" && p != null)
            {
                bool cold = map.mapTemperature != null && map.mapTemperature.OutdoorTemp < 0f;
                Thing best = null;
                float bestScore = float.MinValue;
                for (int i = 0; i < all.Count; i++)
                {
                    Thing t = all[i];
                    if (!IsCandidate(t, categoryDefName, p)) continue;
                    float score = cold ? t.def.GetStatValueAbstract(StatDefOf.Insulation_Cold) : 0f;
                    if (!EconomyItemStock.LayerCovered(p, t.def)) score += 1000f;   // 先补缺的层
                    score += Rand.Value;                                            // 同分随机
                    if (score > bestScore) { bestScore = score; best = t; }
                }
                if (best != null)
                {
                    def = best.def;
                    price = EconomyPrice.UnitPrice(best, map);
                    return best;
                }
                return null;
            }

            int pick = Rand.Range(0, count);
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (!IsCandidate(t, categoryDefName, p)) continue;
                if (pick-- > 0) continue;
                def = t.def;
                price = EconomyPrice.UnitPrice(t, map);
                return t;
            }
            return null;
        }


        /// <summary>原版判定：这件衣服这个小人能不能穿（部件、性别、体型）。</summary>
        public static bool CanWear(Pawn p, ThingDef def)
        {
            if (p == null || def == null || def.apparel == null) return true;
            try
            {
                if (!ApparelUtility.HasPartsToWear(p, def)) return false;
                if (!def.apparel.PawnCanWear(p)) return false;
                return true;
            }
            catch { return true; }
        }

        /// <summary>玩家设定的服装策略（原版 Outfit）：不在策略里的衣服不买。</summary>
        public static bool AllowedByOutfit(Pawn p, ThingDef def)
        {
            if (p == null || def == null) return true;
            try
            {
                if (p.outfits == null) return true;
                ApparelPolicy policy = p.outfits.CurrentApparelPolicy;
                if (policy == null || policy.filter == null) return true;
                // 完全没限制（Anything）时一律放行
                if (policy.filter.AllowedThingDefs == null) return true;
                return policy.filter.Allows(def);
            }
            catch { return true; }
        }

        /// <summary>原版判定：这件武器/装备这个小人能不能用（种族、体型、部件）。</summary>
        public static bool CanEquip(Pawn p, Thing t)
        {
            if (p == null || t == null) return true;
            try
            {
                string reason;
                return EquipmentUtility.CanEquip(t, p, out reason);
            }
            catch { return true; }
        }

        /// <summary>器官、仿生体、血清、基因这类"植入物"，不参与日常消费购买。</summary>
        public static bool IsImplantLike(ThingDef def)
        {
            if (def == null) return false;
            EconomyCategoryDef cat = EconomyRegistry.CategoryOf(def);
            if (cat != null && cat.defName == "DshEco_Augment") return true;
            if (def.isTechHediff) return true;                       // 仿生体/植入物
            if (def.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    if (def.comps[i].GetType().Name == "CompProperties_UseEffectInstallHediff") return true;
                }
            }

            if (def.thingCategories != null)
            {
                for (int i = 0; i < def.thingCategories.Count; i++)
                {
                    ThingCategoryDef tc = def.thingCategories[i];
                    if (tc == null || tc.defName == null) continue;
                    string n = tc.defName;
                    if (n.StartsWith("BodyParts") || n == "Genes" || n == "Serums") return true;
                }
            }

            string dn = def.defName ?? string.Empty;
            return dn.IndexOf("Bionic", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Archotech", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Prosthetic", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Organ", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Serum", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Gene", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsCandidate(Thing t, string categoryDefName, Pawn p)
        {
            if (t == null || t.def == null || t.stackCount <= 0) return false;
            if (t.def.category != ThingCategory.Item) return false;
            if (EconomyRegistry.IsCurrency(t.def)) return false;
            if (EconomyPrivate.IsPrivate(t)) return false;

            string dn = t.def.defName;
            if (dn.IndexOf("Waste", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Pollution", System.StringComparison.OrdinalIgnoreCase) >= 0
                || dn.IndexOf("Toxic", System.StringComparison.OrdinalIgnoreCase) >= 0) return false;

            // 器官/仿生体/血清/基因不是"消费品"：只有这个小人确实缺对应部位时才算候选
            if (IsImplantLike(t.def)
                && (p == null || !EconomyImplantDemand.Wanted(p, t.def))) return false;

            EconomyCategoryDef cat = EconomyRegistry.CategoryOf(t.def);
            if (cat == null || cat.defName != categoryDefName) return false;

            // 医药类：没开自我治疗的人只能买白名单里的
            if (categoryDefName == "DshEco_Medicine" && !SelfTend(p) && !Whitelisted(t.def)) return false;

            // 衣物/武器/装备：用原版判定，种族、体型、性别、部件不符的直接排除
            if (p != null)
            {
                if (categoryDefName == "DshEco_Apparel")
                {
                    if (!CanWear(p, t.def)) return false;
                    // 玩家在「方案 → 服装」里设的套装就是购买预设：不在套装里的衣服不买
                    if (!AllowedByOutfit(p, t.def)) return false;
                }
                if ((categoryDefName == "DshEco_Weapon" || categoryDefName == "DshEco_Goods") && !CanEquip(p, t)) return false;
                if (categoryDefName == "DshEco_Weapon" && !WeaponFitsSkill(p, t.def)) return false;

                // CE 装备预设：预设里点了这类东西，就只买预设里的
                if (categoryDefName == "DshEco_Weapon" || categoryDefName == "DshEco_Goods" || categoryDefName == "DshEco_Apparel")
                {
                    Dictionary<ThingDef, int> lo = CEAmmo.LoadoutMap(p);
                    if (lo.Count > 0 && CEAmmo.LoadoutHasCategory(p, categoryDefName) && !lo.ContainsKey(t.def))
                        return false;
                }
            }
            return true;
        }

        /// <summary>囤太多就浪费：随机毁掉一件私有物品，并记账。</summary>
        private static void Waste(Map map, MapEconomyComponent comp, Pawn p)
        {
            if (p == null || p.inventory == null) return;
            List<Thing> hoard = new List<Thing>();
            foreach (Thing t in p.inventory.innerContainer)
            {
                if (t == null || t.def == null) continue;
                if (EconomyRegistry.IsCurrency(t.def)) continue;
                if (!EconomyPrivate.IsPrivate(t)) continue;
                hoard.Add(t);
            }
            float cap = MassUtility.Capacity(p);
            float mass = PrivateMass(p);
            if (cap <= 0f || mass < cap * WasteFillRatio) return;
            if (!Rand.Chance(Mathf.Clamp(WasteChance * WealthFactor(p, map), 0.15f, 0.7f))) return;

            if (hoard.Count == 0) return;
            Thing victim = hoard[Rand.Range(0, hoard.Count)];
            int n = victim.stackCount;
            victim.Destroy(DestroyMode.Vanish);
            comp.consumptionWasted += n;
            Log.Message("[CONSUME] " + p.LabelShort + " 囤积浪费 " + n + " 件");
        }
    }
}
