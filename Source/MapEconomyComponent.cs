using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 每张地图一份的经济状态：分类公共化程度、商品税、税收政策、工资政策、医疗服务费。
    /// 只存"被玩家手动改过"的分类，其余继承 Def 默认值，这样后续加分类不会污染存档。
    /// </summary>
    public class MapEconomyComponent : MapComponent
    {
        /// <summary>军人制度：勾选后，被标记的军人脱产、抗动乱。</summary>
        public bool militaryEnabled;
        public List<Pawn> soldiers = new List<Pawn>();

        /// <summary>搬运白名单：勾选后只搬这里列出的物品。</summary>
        public List<HaulFilter> haulFilters = new List<HaulFilter>();

        /// <summary>开发者：欠薪。开着时下一期不发工资，次数用完后自动关闭。</summary>
        public bool arrearsEnabled;
        public int arrearsRemaining;
        public int arrearsCount;
        public int consecutiveOnTimePays;
        public int arrearsAutoTimes = 1;

        /// <summary>军人固定工资：开启后军人每期拿固定数额，不按劳动量算。</summary>
        public bool soldierWageEnabled = true;
        public int soldierWage = 150;

        private Dictionary<EconomyCategoryDef, float> overrides = new Dictionary<EconomyCategoryDef, float>();

        // 商品税栏位，长度跟随模组设置里的栏位数量
        public List<TaxSlot> taxSlots = new List<TaxSlot>();

        // 所得税政策
        public TaxPolicy incomeTax = new TaxPolicy();

        // 工资政策
        public WagePolicy wages = new WagePolicy();

        // 医疗服务收费政策
        public MedicalPolicy medical = new MedicalPolicy();

        // 学费政策
        public EducationPolicy education = new EducationPolicy();

        // 累计税收（用于界面显示，方便核对）
        public int totalTaxCollected;

        // 营养膏机收费（每张地图一份，由营养膏机上的按钮调整）
        public bool pasteChargeEnabled = true;
        public float pastePrice = 5f;
    // 外部贸易限制：原版价格不动，按本次访问累计成交额加惩罚
    public bool tradeLimitEnabled = true;
    public bool depreciationEnabled = true;
    public float depreciationMtbDays = 20f;
    public float tradeLimitValue = 5000f;
    public float tradeBuyMult = 2f;
    public float tradeSellMult = 0.5f;

    /// <summary>涨价线随商品总值浮动：max(下限, 商品总值 × 该比例)</summary>
    public float tradeLimitRatio = 0.05f;

    /// <summary>收购线 = 商人白银 × 该比例（默认 50%：他花掉一半现金后开始压价）。</summary>
    public float tradeSellRatio = 0.5f;

    private bool tradeSellRatioMigrated;
    private bool tradeSellMultMigrated;

    private bool tradeLimitMigrated1500;

        // 奖金政策
        public BonusPolicy bonus = new BonusPolicy();

        // 发薪按"全局时间的整周期"算（TicksGame / 周期），与存档/读档时刻无关
        private long lastPayPeriod = -1;
        private int lastLaborTick = -1;
        private int lastSatTick = -1;
        private int lastSatSync = -1;
        private int lastLaborFlush = -1;

        // 本期按工作类型分开记的工时：key = 小人ID|工作类型defName，value = 原始 tick。
        // 工资期望 = Σ(各类型工时 × 该类型权重)，这样"一个人干好几种活"时每段都按自己的乘数算。
        private Dictionary<string, float> workHours = new Dictionary<string, float>();
        private List<string> workHourKeys = new List<string>();
        private List<float> workHourValues = new List<float>();

        // 奖金末位的效率惩罚：Pawn → 乘数（-0.01 = 慢 1%），生效到下一次发奖金
        private Dictionary<Pawn, float> bonusSpeed = new Dictionary<Pawn, float>();
        private List<Pawn> bonusSpeedPawns = new List<Pawn>();
        private List<float> bonusSpeedValues = new List<float>();

        public void SetBonusSpeed(Pawn p, float value)
        {
            if (p == null) return;
            bonusSpeed[p] = value;
        }

        public void ResetBonusSpeed()
        {
            bonusSpeed.Clear();
        }

        public float GetBonusSpeed(Pawn p)
        {
            if (p == null || bonusSpeed == null) return 0f;
            float v;
            return bonusSpeed.TryGetValue(p, out v) ? v : 0f;
        }

        // 当日劳动 tick（按天清零），用来算"超过 8 小时"的满意度惩罚
        private int dailyWorkDay = -1;
        private Dictionary<Pawn, float> dailyWork = new Dictionary<Pawn, float>();

        private static int DayIndex
        {
            get { return Find.TickManager != null ? Find.TickManager.TicksGame / GenDate.TicksPerDay : 0; }
        }

        public void AddDailyWork(Pawn p, float ticks)
        {
            if (p == null || ticks <= 0f) return;
            int day = DayIndex;
            if (day != dailyWorkDay)
            {
                dailyWork.Clear();
                dailyWorkDay = day;
            }
            float cur;
            dailyWork.TryGetValue(p, out cur);
            dailyWork[p] = cur + ticks;
        }

        /// <summary>今天这个人干了多少小时（1 小时 = 2500 tick）。</summary>
        public float DailyWorkHours(Pawn p)
        {
            if (p == null || dailyWork == null || dailyWorkDay != DayIndex) return 0f;
            float t;
            return dailyWork.TryGetValue(p, out t) ? t / 2500f : 0f;
        }

        public void ClearDailyWork()
        {
            if (dailyWork != null) dailyWork.Clear();
            dailyWorkDay = DayIndex;
        }

        private int lastFoodGuardTick = -1;
        private readonly Dictionary<int, int> foodBlockedAt = new Dictionary<int, int>();

        private int silverCached;
        private int silverCachedTick = -99999;
        private float indexCached = 1f;

        // 商品总价值（c+v+s 合计），每 600 tick 重算，用于 MV=PQ 的 Q
        private float goodsValue;
        private int goodsValueTick = -99999;

        // 通胀压力：>0 表示物价在涨，工资会跟着涨（正反馈）
        public float InflationPressure;

        // 消费统计：本期买走多少件、囤积浪费多少件
        public int consumptionBought;
        public int consumptionWasted;

        public float GoodsValue
        {
            get { return goodsValue; }
        }

        public void RefreshGoodsValue()
        {
            RefreshStock();
        }

        // 库存缓存：defName → 地图上该物品的总堆叠数，每 250 tick 重扫一次。
        // 商品价格随库存浮动靠它，避免每次询价都扫全图。
        private Dictionary<ThingDef, int> stockCounts = new Dictionary<ThingDef, int>();
        private int stockTick = -99999;

        /// <summary>缓存中的殖民地商品总值（EconomyInflation 每 2500 tick 刷新）。</summary>
    public float GoodsValueCached()
    {
        if (goodsValue <= 1f) goodsValue = EconomyInflation.GoodsValue(map);
        return goodsValue;
    }

    public int StockOf(ThingDef def)
        {
            if (def == null) return 0;
            RefreshStock();
            int n;
            return stockCounts.TryGetValue(def, out n) ? n : 0;
        }

        /// <summary>把"当前仓库里真的有货"的物品塞进集合（供短缺巡检用）。</summary>
        public void AppendStockedDefs(HashSet<ThingDef> into)
        {
            if (into == null) return;
            RefreshStock();
            if (stockCounts == null) return;
            foreach (KeyValuePair<ThingDef, int> kv in stockCounts)
            {
                if (kv.Key != null && kv.Value > 0) into.Add(kv.Key);
            }
        }

        /// <summary>增量维护库存表（物品生成/销毁时调用）。</summary>
        public void AddStock(ThingDef def, int delta)
        {
            if (def == null || delta == 0) return;
            if (stockCounts == null) stockCounts = new Dictionary<ThingDef, int>();
            int n;
            stockCounts.TryGetValue(def, out n);
            n += delta;
            if (n <= 0) stockCounts.Remove(def);
            else stockCounts[def] = n;
        }

        private void RefreshStock()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            DshEconomySettings set = DshEconomyMod.Settings;
            // 增量库存开（默认）：1 分钟扫一次，省 CPU；
            // 关掉：每 250 tick 扫一次，库存与价格更实时，但大基地更吃性能。
            bool incremental = set == null || set.incrementalStock;
            int interval = incremental ? 2500 : 250;
            if (stockTick >= 0 && now - stockTick >= 0 && now - stockTick < interval) return;
            stockTick = now;
            if (stockCounts == null) stockCounts = new Dictionary<ThingDef, int>();
            stockCounts.Clear();

            List<Thing> all = map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver);
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.def == null) continue;
                if (t.def.category != ThingCategory.Item) continue;
                int n;
                stockCounts.TryGetValue(t.def, out n);
                stockCounts[t.def] = n + t.stackCount;
            }

            // 商品总价值直接从库存表算（几百项），不用再扫一遍全图
            float total = 0f;
            foreach (KeyValuePair<ThingDef, int> kv in stockCounts)
            {
                total += EconomyValue.Of(kv.Key, map) * kv.Value;
            }
            goodsValue = total;
            goodsValueTick = now;
        }

        public MapEconomyComponent(Map map) : base(map)
        {
        }

        // ───────────── 公共化程度 ─────────────

        /// <summary>所有经济分类的社会化平均值（0=全私有，100=全公有）。</summary>
        public float AveragePublicization()
        {
            List<EconomyCategoryDef> cats = DefDatabase<EconomyCategoryDef>.AllDefsListForReading;
            if (cats == null || cats.Count == 0) return 50f;
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < cats.Count; i++)
            {
                EconomyCategoryDef c = cats[i];
                if (c == null) continue;
                sum += GetPublicization(c);
                n++;
            }
            return n > 0 ? sum / n : 50f;
        }

        public int doctrineMode;   // 0=自动（默认私有）1=强制私有 2=强制公有

        public float GetPublicization(EconomyCategoryDef cat)
        {
            if (cat == null) return 0f;
            float v;
            if (overrides != null && overrides.TryGetValue(cat, out v)) return EconomyDoctrine.Cap(map, cat, v);
            return EconomyDoctrine.Cap(map, cat, cat.defaultPublicization);
        }

        public bool IsOverridden(EconomyCategoryDef cat)
        {
            return cat != null && overrides != null && overrides.ContainsKey(cat);
        }

        public void SetPublicization(EconomyCategoryDef cat, float value)
        {
            if (cat == null) return;
            if (overrides == null) overrides = new Dictionary<EconomyCategoryDef, float>();
            overrides[cat] = EconomyDoctrine.Cap(map, cat, value);
        }

        public void ResetAll()
        {
            if (overrides != null) overrides.Clear();
        }

        // ───────────── 商品税 ─────────────

        public void EnsureTaxSlots(int count)
        {
            if (taxSlots == null) taxSlots = new List<TaxSlot>();
            if (count < 0) count = 0;
            while (taxSlots.Count < count) taxSlots.Add(new TaxSlot());
            while (taxSlots.Count > count) taxSlots.RemoveAt(taxSlots.Count - 1);
            for (int i = 0; i < taxSlots.Count; i++)
            {
                if (taxSlots[i] == null) taxSlots[i] = new TaxSlot();
            }
        }

        public float TaxRateFor(ThingDef def)
        {
            if (def == null || taxSlots == null) return 0f;
            for (int i = 0; i < taxSlots.Count; i++)
            {
                TaxSlot slot = taxSlots[i];
                if (slot != null && slot.thing == def) return slot.rate;
            }
            return 0f;
        }

        public void ClearAllTaxes()
        {
            if (taxSlots == null) return;
            for (int i = 0; i < taxSlots.Count; i++)
            {
                if (taxSlots[i] == null) taxSlots[i] = new TaxSlot();
                taxSlots[i].thing = null;
                taxSlots[i].rate = 0f;
            }
        }

        // ───────────── 白银与银价指数 ─────────────

        public int SilverCount
        {
            get { Refresh(); return silverCached; }
        }

        public float SilverIndex
        {
            get { Refresh(); return indexCached; }
        }

        private void Refresh()
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (silverCachedTick >= 0 && now - silverCachedTick < 60) return;
            silverCachedTick = now;

            ThingDef currency = EconomyRegistry.PrimaryCurrency;
            int total = 0;
            if (currency != null && map != null && map.listerThings != null)
            {
                List<Thing> things = map.listerThings.ThingsOfDef(currency);
                for (int i = 0; i < things.Count; i++) total += things[i].stackCount;
            }
            silverCached = total;

            indexCached = EconomyInflation.Index(map);
        }

        // ───────────── 每 tick ─────────────

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            EconomyLaborSessions.ClearAll();
            // TextureDump.Once();  // 关闭：RenderTexture 在 tick 里取放会撞 1.7 引擎的 FreeReleasedTemporaries 崩溃，需要时用沙盒按钮手动导出
            EconomySilverGraphic.Apply();
            if (map != null && map.mapPawns != null)
            {
                List<Pawn> cols = map.mapPawns.FreeColonists;
                for (int i = 0; i < cols.Count; i++) EconomyPrivateStorageBehavior.MarkDirty(cols[i]);
            }
        }

        private bool silverReapplied;
        private int lastTagPrune = -99999;
        private int lastEstateCheck = -99999;
        private int lastSettingsSync = -99999;
        private int lastProductionDecay = -99999;
        private int pawnCursor;

        /// <summary>每次只处理一小片殖民者：紧急买食物 + 睡觉采样（约 1000 tick 轮完一轮）。</summary>
        private void SliceColonists(Map map)
        {
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            int n = colonists.Count;
            if (n <= 0) return;
            int slice = Mathf.Max(1, Mathf.CeilToInt(n / 4f));
            for (int k = 0; k < slice; k++)
            {
                Pawn cp = colonists[(pawnCursor + k) % n];
                if (cp == null || cp.Dead) continue;
                // 只有真的饿了（饱食度 ≤35%）才进入购买扫描，否则一次属性读取就结束
                if (cp.needs != null && cp.needs.food != null && cp.needs.food.CurLevelPercentage <= 0.35f)
                    EconomyConsumption.UrgentFood(map, cp);
                EconomySatisfaction.NoticeSleep(cp);
            }
            pawnCursor = (pawnCursor + slice) % n;
        }


        /// <summary>贸易限制参数统一放在模组设置里，这里每 tick 同步一份给旧代码用。</summary>
        private void ApplyTradeSettings()
        {
            DshEconomySettings s = DshEconomyMod.Settings;
            if (s == null) return;
            tradeLimitEnabled = s.tradeLimitEnabled;
            tradeLimitValue = s.tradeLimitValue;
            tradeLimitRatio = s.tradeLimitRatio;
            tradeBuyMult = s.tradeBuyMult;
            tradeSellMult = s.tradeSellMult;
            tradeSellRatio = s.tradeSellRatio;
        }

        public override void MapComponentTick()
        {
            if (!silverReapplied)
            {
                silverReapplied = true;
                EconomySilverGraphic.Apply();
            }
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;

            // 这两件事以前每 tick 无条件跑；改成每 250 tick 同步一次，省掉 tick 的固定开销
            if (now - lastSettingsSync >= 250)
            {
                lastSettingsSync = now;
                Refresh();
                ApplyTradeSettings();
                // 产地统计的衰减：15 天减半一次，让"最近怎么来的"占主导
                if (now - lastProductionDecay >= EconomyProductionStats.DecayIntervalTicks)
                {
                    lastProductionDecay = now;
                    EconomyProductionStats.Decay();
                }
                // 成交量：满一期（1 天）就把本期翻成上期
                EconomyTradeVolume.Roll(now);
                // 结构性短缺：连续几天库存过低就汇总发一封信（内部有冷却）
                EconomyShortageWatch.Tick(map);
            }

            // 劳动计量：每 250 tick 对一次账（换活才结算），不再 patch 全场热路径
            if (!EconomyLaborSessions.Active) AccumulateLaborLegacy(now);
            else if (now - lastLaborFlush >= 250)
            {
                lastLaborFlush = now;
                EconomyLaborSessions.Reconcile(map);
                EconomyInflation.Tick(map, 250);
                EconomyConsumption.Tick(map);
            // 分片轮询：每次只处理 1/4 的人，约 1000 tick 覆盖全员，避免每 250 tick 全体扫描
            try
            {
                SliceColonists(map);
            }
            catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 分片轮询异常：" + e.Message); }
                EconomyPocketMoney.Tick(map);
                EconomyPrivateStorageBehavior.Tick(map);
                StorageEjectQueue.Tick(map);
                try { if (Find.TickManager.TicksGame % 250 == 0) EconomyPreceptForcer.EnsureOnce(map); } catch { }
                EconomyMarket.Tick();
                EconomyPasteDispenser.StockTick(map);
                try { EconomyPublicSilver.Tick(map);
                try { StorageSettingsOwnerIndex.EnsureFresh(map); } catch { } } catch (System.Exception e) { EcoLog.WarnOnce("publicsilver", e); }
                try { EconomyPublicGear.Tick(map); } catch (System.Exception e) { EcoLog.WarnOnce("pubgear", e); }
                try
                {
                    if (now - lastEstateCheck >= 2500)
                    {
                        lastEstateCheck = now;
                        EconomyRealEstate estate = Current.Game.GetComponent<EconomyRealEstate>();
                        if (estate != null) estate.Sanitize();
                try { PrivateStorageHaulPatch.TickLog(map); } catch { }
                if (EconomySelfCheck.PendingDump && Find.TickManager != null && Find.TickManager.TicksGame > 120)
                {
                    EconomySelfCheck.PendingDump = false;
                    try { EconomySelfCheck.Run(map); } catch { }
                    try { EconomyRealEstate.Diagnose(map); } catch { }
                    try { EconomyRealEstate.DumpStorages(map); } catch { }
                    try { EconomyRealEstate.CellOwnerAudit(map); } catch { }
                }
                    }
                }
                catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 产权清理失败：" + e.Message); }
                try
                {
                    if (now - lastTagPrune >= 2500)
                    {
                        lastTagPrune = now;
                        int cleaned = EconomyPrivate.PruneEmptyTags(map);
                        if (cleaned > 0) Log.Message("[DSH-ECONOMY] 组件表修复：重建 " + cleaned + " 个物品的 comps");
                    }
                }
                catch (System.Exception e) { EcoLog.WarnOnce("tagprune", e); }
            EconomyDepreciation.Tick(map);
            EconomyRestock.Tick(map);
                EconomyPrivateStorageBehavior.ValidatePrivatePlacement(map);
                if (lastSatSync < 0 || now - lastSatSync >= 2500)
                {
                    lastSatSync = now;
                try { EconomyRealEstate.Prune(map); } catch (System.Exception ex) { EcoLog.WarnOnce("EstatePrune", ex); }
                    EconomySatisfaction.RecomputeAll(map);
                    EconomyDoctrine.LogMemeDiagnostics(map);
                    try { EconomyEmotionSpread.Tick(map); }
                    catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 情绪传播异常：" + e.Message); }
                }
            }
            GuardPublicFood(now);
            PayWages(now);
            SatisfactionTick(now);
            EconomyUnrest.Tick(map, 60);
        }

        /// <summary>
        /// 满意度一天结算一次，独立于发薪——不开工资也要照常变化，
        /// 否则玩家关掉工资后满意度会永远卡在 50。
        /// </summary>
        private void SatisfactionTick(int now)
        {
            if (lastSatTick < 0)
            {
                lastSatTick = now;
                return;
            }
            if (now - lastSatTick < GenDate.TicksPerDay) return;
            lastSatTick = now;
            EconomySatisfaction.DailyUpdate(map);
        }

        private void AccumulateLaborLegacy(int now)
        {
            if (lastLaborTick < 0) lastLaborTick = now;
            if (now - lastLaborTick < 60) return;

            float dt = now - lastLaborTick;
            lastLaborTick = now;

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (EconomyWages.IsWorking(p))
                {
                    float raw = dt * EconomyWages.WorkRate(p);
                    EconomyLedger.AddRawLabor(p, raw);
                    EconomyLedger.AddLabor(p, raw * EconomyWages.WageWeightOf(p));
                    AddWorkHours(p, raw);
                }
            }
        }

        /// <summary>按"当前工作类型"把这段原始工时记到对应那一栏。</summary>
        private void AddWorkHours(Pawn p, float rawTicks)
        {
            AddWorkHours(p, EconomyWages.WorkTypeOf(p), rawTicks);
        }

        /// <summary>外部（劳动会话）直接指定工作类型记账。</summary>
        public void AddWorkHours(Pawn p, WorkTypeDef wt, float rawTicks)
        {
            if (p == null || rawTicks <= 0f) return;
            string type = wt != null ? wt.defName : "Other";
            string key = p.GetUniqueLoadID() + "|" + type;
            float old;
            workHours.TryGetValue(key, out old);
            workHours[key] = old + rawTicks;
            // 挖矿的工时另外存一份长期账：工资那边每期会清，这里不清，
            // 用来算"挖一件平均花多少人日"，从而给矿物定价。
            if (type == "Mining") EconomyProductionStats.NoteMiningLabor(rawTicks);
            // 钓鱼：原版/CE 都没有"钓一条要多久"的字段，只能像挖矿一样记账。
            if (type == "Fishing" || type == "Fish") EconomyProductionStats.NoteFishingLabor(rawTicks);
        }

        /// <summary>这个人本期各工作类型的原始工时（已按类型分好）。</summary>
        public List<KeyValuePair<string, float>> WorkHoursOf(Pawn p)
        {
            var result = new List<KeyValuePair<string, float>>();
            if (p == null) return result;
            string prefix = p.GetUniqueLoadID() + "|";
            foreach (KeyValuePair<string, float> kv in workHours)
            {
                if (kv.Key.StartsWith(prefix)) result.Add(new KeyValuePair<string, float>(kv.Key.Substring(prefix.Length), kv.Value));
            }
            result.Sort((a, b) => b.Value.CompareTo(a.Value));
            return result;
        }

        /// <summary>
        /// 按类型分别乘权重再加总，得到"本期应得工资"。
        /// 和旧的 labor × 权重 在数值上等价，区别是权重按各段自己的类型取，
        /// 中途改权重、或者一个人干多种活时都算得准。
        /// </summary>
        public float ExpectedWageOf(Pawn p, WagePolicy w)
        {
            if (p == null || w == null) return 0f;
            DshEconomySettings s = DshEconomyMod.Settings;
            float ticks = 0f;
            List<KeyValuePair<string, float>> list = WorkHoursOf(p);
            for (int i = 0; i < list.Count; i++)
            {
                string typeName = list[i].Key;
                float weight = 1f;
                if (s != null && typeName != "Other")
                {
                    WorkTypeDef wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(typeName);
                    if (wt != null) weight = s.GetWorkMult(wt);
                }
                ticks += list[i].Value * weight;
            }
            return w.UncappedWage(ticks, p);
        }

        /// <summary>按类型分别乘权重再加总得到"有效工时"，再换算成 100% 的创造价值。</summary>
        public float ProducedValueOf(Pawn p, WagePolicy w)
        {
            if (p == null || w == null) return 0f;
            DshEconomySettings s = DshEconomyMod.Settings;
            float ticks = 0f;
            List<KeyValuePair<string, float>> list = WorkHoursOf(p);
            for (int i = 0; i < list.Count; i++)
            {
                string typeName = list[i].Key;
                float weight = 1f;
                if (s != null && typeName != "Other")
                {
                    WorkTypeDef wt = DefDatabase<WorkTypeDef>.GetNamedSilentFail(typeName);
                    if (wt != null) weight = s.GetWorkMult(wt);
                }
                ticks += list[i].Value * weight;
            }
            return w.ProducedValue(ticks, p);
        }

        /// <summary>发完工资后清掉这个人的工时明细。</summary>
        public void ClearWorkHours(Pawn p)
        {
            if (p == null) return;
            string prefix = p.GetUniqueLoadID() + "|";
            var remove = new List<string>();
            foreach (string key in workHours.Keys)
            {
                if (key.StartsWith(prefix)) remove.Add(key);
            }
            for (int i = 0; i < remove.Count; i++) workHours.Remove(remove[i]);
        }

        private void PayWages(int now)
        {
            if (wages == null || !wages.enabled) return;
            DshEconomySettings s = DshEconomyMod.Settings;
            int everyHours = s != null ? Mathf.Clamp(s.payIntervalHours, 1, 720) : 24;
            long period = now / (long)(GenDate.TicksPerHour * everyHours);
            if (period == lastPayPeriod) return;

            // 上一个周期没结算过（刚读档/刚开局）：把当前周期记为已结算，等下一整周期
            if (lastPayPeriod < 0)
            {
                lastPayPeriod = period;
                return;
            }

            lastPayPeriod = period;

            Settle(everyHours);
        }

        /// <summary>完整结算一轮：工资 → 奖金 → 学费 → 所得税 → 信件。</summary>
        public int Settle(int everyHours)
        {
            if (wages == null || !wages.enabled) return 0;

            // 发薪前先把进行中的会话结算掉，否则正在干的活这段工时会整期丢失
            EconomyLaborSessions.Reconcile(map, true);

            DshEconomySettings s = DshEconomyMod.Settings;

            // 先算出本期的账面数字（发完之后劳动量会被扣掉，所以要在前面算）
            float totalProduced = 0f;
            float totalWage = 0f;
            int paidPeople = 0;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                float lab = EconomyLedger.GetLabor(colonists[i]);
                if (lab <= 0f) continue;
                totalProduced += wages.ProducedValue(lab, colonists[i]);
                totalWage += wages.WageFor(lab, colonists[i], EconomyLedger.GetRawLabor(colonists[i]));
                paidPeople++;
            }

            int silverBefore = EconomyBank.ColonySilver(map);

            int paid = 0;
            bool arrearsThisRound = false;
            if (arrearsEnabled && arrearsRemaining > 0)
            {
                arrearsThisRound = true;
                arrearsRemaining--;
                arrearsCount++;
                consecutiveOnTimePays = 0;
                if (arrearsRemaining <= 0)
                {
                    arrearsEnabled = false;
                    arrearsRemaining = 0;
                }
                Log.Message("[DSH-ECONOMY] 欠薪：本期不发工资。累计欠薪 " + arrearsCount
                    + " 次，剩余自动次数 " + arrearsRemaining);
            }
            else
            {
                paid = EconomyWages.PayAll(map, wages);
                if (paid > 0) consecutiveOnTimePays = Mathf.Min(999, consecutiveOnTimePays + 1);
            }
            if (paid > 0) Log.Message("[DSH-ECONOMY] 发薪（每 " + everyHours + " 小时）：" + paid + " 白银已发放到个人背包");

            int bonusPaid = 0;
            if (bonus != null && bonus.enabled && bonus.autoPay)
            {
                bonusPaid = EconomyBonus.Pay(map, bonus);
            }

            int tuition = education != null ? EconomyEducation.Charge(map, education) : 0;
            int tax = EconomyTax.CollectIncomeTax(map, this);
            totalTaxCollected += tax;

            // 发完钱立刻重算满意度，让"三笔缺口"当场反映到进度条上
            EconomySatisfaction.RecomputeAll(map);

            if (s != null && s.sendPayLetter && (paid > 0 || bonusPaid > 0))
            {
                int silverAfter = EconomyBank.ColonySilver(map);
                int income = tax + tuition;
                int spent = paid + bonusPaid;
                int net = silverAfter - silverBefore;

                string body = (arrearsThisRound ? "本期欠薪（累计 " + arrearsCount + " 次），未发放工资。\n\n"
                    : "本期发薪完成（间隔 " + everyHours + " 小时）。\n\n")
                    + "领薪人数：" + paidPeople + "\n"
                    + "创造价值：" + totalProduced.ToString("0.#") + " 白银\n\n"
                    + "—— 支出 ——\n"
                    + "工资支出：" + paid + " 白银\n"
                    + "奖金支出：" + bonusPaid + " 白银\n"
                    + "支出合计：" + spent + " 白银\n\n"
                    + "—— 收入 ——\n"
                    + (tuition > 0 ? "学费收入：" + tuition + " 白银\n" : "")
                    + (tax > 0 ? "所得税收入：" + tax + " 白银\n" : "")
                    + "收入合计：" + income + " 白银\n\n"
                    + "—— 总结 ——\n"
                    + "国库变化：" + silverBefore + " → " + silverAfter + " 白银\n"
                    + (net >= 0 ? "本期净赚：" + net + " 白银\n" : "本期净亏：" + (-net) + " 白银\n")
                    + "剩余留存：" + Mathf.Max(0f, totalProduced - totalWage).ToString("0.#") + " 白银";
                Find.LetterStack.ReceiveLetter("发薪日", body, LetterDefOf.NeutralEvent, null, null, null, null, null);
            }
            return paid;
        }

        /// <summary>
        /// 禁止小人在搬运途中吃掉"背在身上的公共物资"。
        /// 只中止这一种具体的进食任务，不修改其他 mod 的进食逻辑。
        /// </summary>
        private void GuardPublicFood(int now)
        {
            if (lastFoodGuardTick >= 0 && now - lastFoodGuardTick < 30) return;
            lastFoodGuardTick = now;

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                Job job = p.CurJob;
                if (job == null || job.def != JobDefOf.Ingest) continue;

                Thing food = job.targetA.Thing;
                if (food == null) continue;
                if (p.inventory == null || p.inventory.innerContainer == null) continue;
                if (!p.inventory.innerContainer.Contains(food)) continue;
                if (!EconomyPrivate.IsPrivate(food)) continue;

                int lastAt;
                if (foodBlockedAt.TryGetValue(p.thingIDNumber, out lastAt) && now - lastAt < 600) continue;
                foodBlockedAt[p.thingIDNumber] = now;
                // 无界字典：定期清掉一小时前的记录，避免长局无限增长
                if (foodBlockedAt.Count > 256) PruneFoodBlocked(now);

                p.jobs.EndCurrentJob(JobCondition.Incompletable, true);
                Messages.Message(p.LabelShort + " 不能把搬运中的公共食物当自己的吃掉", p, MessageTypeDefOf.RejectInput, false);
            }
        }

        private void PruneFoodBlocked(int now)
        {
            List<int> dead = null;
            foreach (KeyValuePair<int, int> kv in foodBlockedAt)
            {
                if (now - kv.Value > 2500)
                {
                    if (dead == null) dead = new List<int>();
                    dead.Add(kv.Key);
                }
            }
            if (dead == null) return;
            for (int i = 0; i < dead.Count; i++) foodBlockedAt.Remove(dead[i]);
        }

        /// <summary>立刻完整结算一轮（界面按钮、调试用），返回工资支出。</summary>
        public int PayWagesNow()
        {
            DshEconomySettings s = DshEconomyMod.Settings;
            int everyHours = s != null ? Mathf.Clamp(s.payIntervalHours, 1, 720) : 24;
            lastPayPeriod = Find.TickManager.TicksGame / (long)(GenDate.TicksPerHour * everyHours);
            return Settle(everyHours);
        }

        // ───────────── 存档 ─────────────

        public override void ExposeData()
        {
            base.ExposeData();

            Scribe_Collections.Look(ref overrides, "dsh_pubOverrides", LookMode.Def, LookMode.Value);
            Scribe_Values.Look(ref doctrineMode, "dsh_doctrineMode", 0);
            if (overrides == null) overrides = new Dictionary<EconomyCategoryDef, float>();

            Scribe_Values.Look(ref militaryEnabled, "dsh_militaryEnabled", false);
            Scribe_Collections.Look(ref soldiers, "dsh_soldiers", LookMode.Reference);
            if (soldiers == null) soldiers = new List<Pawn>();

            Scribe_Values.Look(ref arrearsEnabled, "dsh_arrearsEnabled", false);
            Scribe_Values.Look(ref arrearsRemaining, "dsh_arrearsRemaining", 0);
            Scribe_Values.Look(ref arrearsCount, "dsh_arrearsCount", 0);
            Scribe_Values.Look(ref consecutiveOnTimePays, "dsh_onTimePays", 0);
            Scribe_Values.Look(ref arrearsAutoTimes, "dsh_arrearsAutoTimes", 1);
            Scribe_Values.Look(ref soldierWageEnabled, "dsh_soldierWageEnabled", true);
            Scribe_Values.Look(ref soldierWage, "dsh_soldierWage", 150);
            Scribe_Collections.Look(ref haulFilters, "dsh_haulFilters", LookMode.Deep);
            if (haulFilters == null) haulFilters = new List<HaulFilter>();

            Scribe_Collections.Look(ref taxSlots, "dsh_taxSlots", LookMode.Deep);
            if (taxSlots == null) taxSlots = new List<TaxSlot>();

            Scribe_Deep.Look<TaxPolicy>(ref incomeTax, "dsh_incomeTax");
            if (incomeTax == null) incomeTax = new TaxPolicy();

            Scribe_Deep.Look<WagePolicy>(ref wages, "dsh_wages");
            if (wages == null) wages = new WagePolicy();

            Scribe_Deep.Look<MedicalPolicy>(ref medical, "dsh_medical");
            if (medical == null) medical = new MedicalPolicy();

            Scribe_Deep.Look<EducationPolicy>(ref education, "dsh_education");
            if (education == null) education = new EducationPolicy();

            Scribe_Values.Look(ref totalTaxCollected, "dsh_totalTax", 0);
            Scribe_Values.Look(ref pasteChargeEnabled, "dsh_pasteCharge", true);
            bool chargeMigrated = false;
            Scribe_Values.Look(ref chargeMigrated, "dsh_pasteChargeMigrated", false);
            if (!chargeMigrated)
            {
                pasteChargeEnabled = true;   // 老存档统一改为收费
                chargeMigrated = true;
            }
            Scribe_Values.Look(ref pastePrice, "dsh_pastePrice", 5f);
        Scribe_Values.Look(ref tradeLimitEnabled, "dsh_tradeLimitEnabled", true);
        Scribe_Values.Look(ref tradeLimitValue, "dsh_tradeLimitValue", 5000f);
        Scribe_Values.Look(ref tradeBuyMult, "dsh_tradeBuyMult", 2f);
        Scribe_Values.Look(ref tradeSellMult, "dsh_tradeSellMult", 0.5f);
        Scribe_Values.Look(ref tradeLimitRatio, "dsh_tradeLimitRatio", 0.05f);
        Scribe_Values.Look(ref depreciationEnabled, "dsh_depreciationEnabled", true);
        Scribe_Values.Look(ref depreciationMtbDays, "dsh_depreciationMtbDays", 20f);
        Scribe_Values.Look(ref tradeSellRatio, "dsh_tradeSellRatio", 0.5f);
        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            Scribe_Values.Look(ref tradeSellRatioMigrated, "dsh_tradeSellRatioMigrated", false);
            if (!tradeSellRatioMigrated && tradeSellRatio >= 0.999f) tradeSellRatio = 0.5f;
            tradeSellRatioMigrated = true;
            Scribe_Values.Look(ref tradeSellMultMigrated, "dsh_tradeSellMultMigrated", false);
            if (!tradeSellMultMigrated && tradeSellMult >= 0.79f && tradeSellMult <= 0.81f) tradeSellMult = 0.5f;
            if (!tradeSellMultMigrated && tradeSellMult <= 0.36f) tradeSellMult = 0.5f;
        }

        if (Scribe.mode == LoadSaveMode.LoadingVars)
        {
            Scribe_Values.Look(ref tradeLimitMigrated1500, "dsh_tradeLimitMigrated1500", false);
        }
        if (Scribe.mode == LoadSaveMode.PostLoadInit && !tradeLimitMigrated1500)
        {
            if (tradeLimitValue > 1499f && tradeLimitValue < 3001f) tradeLimitValue = 5000f;
            if (tradeLimitValue < 100f) tradeLimitValue = 5000f;
            tradeLimitMigrated1500 = true;
        }
        if (Scribe.mode == LoadSaveMode.Saving)
        {
            Scribe_Values.Look(ref tradeLimitMigrated1500, "dsh_tradeLimitMigrated1500", false);
        }

            Scribe_Deep.Look<BonusPolicy>(ref bonus, "dsh_bonus");
            if (bonus == null) bonus = new BonusPolicy();

            Scribe_Values.Look(ref lastPayPeriod, "dsh_lastPayPeriod", -1L);
            Scribe_Values.Look(ref lastLaborTick, "dsh_lastLaborTick", -1);
            Scribe_Values.Look(ref lastSatTick, "dsh_lastSatTick", -1);

            // 按工作类型的工时明细：字典不能直接序列化，拆成两个平行列表存
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                workHourKeys.Clear();
                workHourValues.Clear();
                foreach (KeyValuePair<string, float> kv in workHours)
                {
                    workHourKeys.Add(kv.Key);
                    workHourValues.Add(kv.Value);
                }
            }
            Scribe_Collections.Look(ref workHourKeys, "dsh_workHourKeys", LookMode.Value);
            Scribe_Collections.Look(ref workHourValues, "dsh_workHourValues", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (workHourKeys == null) workHourKeys = new List<string>();
                if (workHourValues == null) workHourValues = new List<float>();
                workHours = new Dictionary<string, float>();
                for (int i = 0; i < workHourKeys.Count && i < workHourValues.Count; i++)
                {
                    workHours[workHourKeys[i]] = workHourValues[i];
                }
            }

            // 奖金末位效率惩罚：同样是字典，平行列表存
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                bonusSpeedPawns.Clear();
                bonusSpeedValues.Clear();
                foreach (KeyValuePair<Pawn, float> kv in bonusSpeed)
                {
                    bonusSpeedPawns.Add(kv.Key);
                    bonusSpeedValues.Add(kv.Value);
                }
            }
            Scribe_Collections.Look(ref bonusSpeedPawns, "dsh_bonusSpeedPawns", LookMode.Reference);
            Scribe_Collections.Look(ref bonusSpeedValues, "dsh_bonusSpeedValues", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                if (bonusSpeedPawns == null) bonusSpeedPawns = new List<Pawn>();
                if (bonusSpeedValues == null) bonusSpeedValues = new List<float>();
                bonusSpeed = new Dictionary<Pawn, float>();
                for (int i = 0; i < bonusSpeedPawns.Count && i < bonusSpeedValues.Count; i++)
                {
                    if (bonusSpeedPawns[i] != null) bonusSpeed[bonusSpeedPawns[i]] = bonusSpeedValues[i];
                }
            }
        }
    }
}
