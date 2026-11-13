using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>一次劳动会话：从开始干某个活到它结束/被换掉。</summary>
    public class LaborSession
    {
        public Pawn pawn;
        public Job job;            // 用 Job 实例判断"有没有换活"，比 patch 更便宜
        public JobDef def;
        public WorkTypeDef workType;
        public int startTick;
        public string target;
        public Thing targetThing;
    }

    /// <summary>
    /// 劳动计量（事件驱动 + 低频对账）。
    ///
    /// 第一版是 Harmony patch StartJob/EndCurrentJob，但那是**全场所有 Pawn 共用**的热路径
    /// （动物、访客、敌人全都在调），加载和大地图上开销明显。改成每 250 tick 只对殖民者
    /// 做一次"当前 Job 和记录的是不是同一个"的比对：
    ///   - 换了活 → 结算上一段、开新一段
    ///   - 没换活 → 什么都不做
    /// 成本从"每次全场任务切换"降到"每人每分钟 0.24 次"，而且完全不碰别人的热路径。
    /// </summary>
    public static class EconomyLaborSessions
    {
        private const int MaxSessionTicks = 60000;

        // 滚动结算步长：1 游戏小时。一段长活每小时结一次，账本/面板/工资都不会再"卡在 0"。
        private const int RollTicks = 2500;

        private static readonly Dictionary<Pawn, LaborSession> open = new Dictionary<Pawn, LaborSession>();

        public static bool Active = true;

        // 诊断计数：用来判断"记账这一层到底有没有跑"
        public static int OpenedTotal;
        public static int ClosedTotal;
        public static string LastClosedInfo = "（还没有）";
        public static string LastOpenInfo = "（还没有）";

        /// <summary>这个人现在有没有一段还没结算的会话（诊断用）。</summary>
        public static bool HasOpen(Pawn p)
        {
            return p != null && open.ContainsKey(p);
        }

        public static int OpenStartTick(Pawn p)
        {
            LaborSession s;
            if (p == null || !open.TryGetValue(p, out s) || s == null) return -1;
            return s.startTick;
        }

        /// <summary>
        /// 这个人当前这段"还没结算"的劳动折算成多少 tick（已乘工作速度）。
        /// 劳动账本是"一件活干完才记一笔"，所以正在干的那段如果不算进来，
        /// 面板上就会一直显示"今日 0.5 小时"这种偏小的数。
        /// </summary>
        public static float PendingWorkTicks(Pawn p)
        {
            if (p == null) return 0f;
            LaborSession s;
            if (!open.TryGetValue(p, out s) || s == null) return 0f;
            int d = Now - s.startTick;
            if (d <= 0) return 0f;
            if (d > MaxSessionTicks) d = MaxSessionTicks;
            float raw = d * EconomyWages.WorkRate(p);
            return raw > 0f ? raw : 0f;
        }

        /// <summary>每 250 tick 调一次。force=true 时把进行中的都结一段（发薪前用）。</summary>
        public static void Reconcile(Map map, bool force = false)
        {
            if (map == null) return;
            int now = Now;

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead || p.Destroyed)
                {
                    open.Remove(p);
                    continue;
                }

                LaborSession s;
                open.TryGetValue(p, out s);

                bool working = EconomyWages.IsWorking(p);
                Job cur = working ? p.CurJob : null;

                if (s != null && (force || !working || s.job != cur || now - s.startTick >= MaxSessionTicks))
                {
                    Close(p, s, now);
                    s = null;
                    open.Remove(p);
                }

                // 滚动结算：一段活干很久（比如一直蹲在银矿上）时，账本要按小时往里滚，
                // 否则面板和工资都会一直是 0，直到这段活彻底结束。
                if (s != null && working && now - s.startTick >= RollTicks)
                {
                    Close(p, s, now);
                    s = null;
                    open.Remove(p);
                }

                if (s == null && working)
                {
                    Open(p, cur, now);
                }
            }
        }

        private static void Open(Pawn p, Job job, int now)
        {
            var s = new LaborSession
            {
                pawn = p,
                job = job,
                def = job != null ? job.def : null,
                workType = EconomyWages.WorkTypeOf(p),
                startTick = now,
                target = job != null && job.targetA.Thing != null ? job.targetA.Thing.LabelShort : null,
                targetThing = job != null ? job.targetA.Thing : null
            };
            open[p] = s;
            OpenedTotal++;
            LastOpenInfo = p.LabelShort + " 活=" + (s.def != null ? s.def.defName : "?")
                + " 工种=" + (s.workType != null ? s.workType.defName : "无") + " @tick " + now;
        }

        private static void Close(Pawn p, LaborSession s, int now)
        {
            int duration = now - s.startTick;
            if (duration <= 0) return;

            // 工作速度里已含满意度与奖金加成（StatPart 挂在 WorkSpeedGlobal 上），不要再乘满意度
            float raw = duration * EconomyWages.WorkRate(p);
            if (raw <= 0f) return;

            float weight = 1f;
            if (s.workType != null && DshEconomyMod.Settings != null)
            {
                weight = DshEconomyMod.Settings.GetWorkMult(s.workType);
            }

            ClosedTotal++;
            LastClosedInfo = p.LabelShort + " 活=" + (s.def != null ? s.def.defName : "?")
                + " 工种=" + (s.workType != null ? s.workType.defName : "无")
                + " 时长=" + duration + "tick 原始=" + raw.ToString("0") + " 权重=" + weight.ToString("0.0")
                + " 计入=" + (raw * weight).ToString("0");

            EconomyLedger.AddRawLabor(p, raw);
            EconomyLedger.AddLabor(p, raw * weight);

            Map map = p.MapHeld ?? p.Map;
            if (map != null)
            {
                MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
                if (comp != null)
                {
                    comp.AddWorkHours(p, s.workType, raw);
                    comp.AddDailyWork(p, raw);

                    // 工作类型是从 WorkGiver 产出 Job 的那一刻记下来的。但原版很多工作
                    // （挖矿、钓鱼）重写的是 JobOnThing，补丁挂在基类上抓不到，
                    // 于是 workType 是 null、工时不进 Mining 那一栏。
                    // 这里按 JobDef 兜底再记一次，只喂"定价用的长期账本"，不动工资口径。
                    if (s.def != null)
                    {
                        bool workTypeSaysMining = s.workType != null && s.workType.defName == "Mining";
                        string dn = s.def.defName;
                        if (!workTypeSaysMining && dn == "Mine")
                        {
                            EconomyProductionStats.NoteMiningLabor(raw);
                        }
                        else if ((dn == "Fish" || dn == "Fishing" || dn == "FishBite")
                                 && (s.workType == null || s.workType.defName != "Fishing"))
                        {
                            EconomyProductionStats.NoteFishingLabor(raw);
                        }
                    }
                }

                // 采矿：会话结束时矿已经没了，按矿石产出记一笔（没有工单，只能这样归属）
                EconomyBillLedger ledger = map.GetComponent<EconomyBillLedger>();
                if (ledger != null && s.def != null && s.def.defName == "Mine")
                {
                    Mineable mineable = s.targetThing as Mineable;
                    if (mineable != null && (mineable.Destroyed || !mineable.Spawned))
                    {
                        ThingDef yield = mineable.def.building != null ? mineable.def.building.mineableThing : null;
                        if (yield != null)
                        {
                            float amount = mineable.def.building.mineableYield;
                            ledger.RecordActivity("采矿·" + yield.LabelCap,
                                ledger.UnitValue(yield) * amount, duration / GenDate.TicksPerDay);
                        }
                    }
                }
            }
        }

        public static void ClearAll()
        {
            open.Clear();
        }

        public static int OpenCount
        {
            get { return open.Count; }
        }

        // ---------------- 存档 ----------------
        // open 是静态字典，游戏读档不会自动带上它，
        // 于是"今天干了多久"每次进档都从 0 开始。这里把它写进全局存档。

        private static List<Pawn> scPawns = new List<Pawn>();
        private static List<int> scStart = new List<int>();
        private static List<JobDef> scDef = new List<JobDef>();
        private static List<WorkTypeDef> scType = new List<WorkTypeDef>();

        /// <summary>由 <see cref="EconomyLaborSave"/> 在存档时调用。</summary>
        public static void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                scPawns.Clear();
                scStart.Clear();
                scDef.Clear();
                scType.Clear();
                foreach (KeyValuePair<Pawn, LaborSession> kv in open)
                {
                    Pawn p = kv.Key;
                    LaborSession s = kv.Value;
                    if (p == null || s == null) continue;
                    scPawns.Add(p);
                    scStart.Add(s.startTick);
                    scDef.Add(s.def);
                    scType.Add(s.workType);
                }
            }

            Scribe_Collections.Look(ref scPawns, "dsh_laborPawns", LookMode.Reference);
            Scribe_Collections.Look(ref scStart, "dsh_laborStart", LookMode.Value);
            Scribe_Collections.Look(ref scDef, "dsh_laborDef", LookMode.Def);
            Scribe_Collections.Look(ref scType, "dsh_laborType", LookMode.Def);

            if (scPawns == null) scPawns = new List<Pawn>();
            if (scStart == null) scStart = new List<int>();
            if (scDef == null) scDef = new List<JobDef>();
            if (scType == null) scType = new List<WorkTypeDef>();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                open.Clear();
                int n = scPawns.Count;
                for (int i = 0; i < n; i++)
                {
                    Pawn p = scPawns[i];
                    if (p == null) continue;
                    if (i >= scStart.Count) break;
                    open[p] = new LaborSession
                    {
                        pawn = p,
                        job = null,                       // Job 实例不可序列化，读档后按"换了活"结算上一段
                        def = i < scDef.Count ? scDef[i] : null,
                        workType = i < scType.Count ? scType[i] : null,
                        startTick = scStart[i]
                    };
                }
            }
        }

        private static int Now
        {
            get { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; }
        }
    }

    /// <summary>
    /// 全局存档载体：把"正在进行的劳动会话"写进游戏存档（GameComponent 是全局的，
    /// 不随地图销毁，读档后还能把每人的 startTick 还原回去）。
    /// </summary>
    public class EconomyLaborSave : GameComponent
    {
        public EconomyLaborSave(Game game) { }

        public override void ExposeData()
        {
            base.ExposeData();
            EconomyLaborSessions.ExposeData();
            EconomyWorkTypes.ExposeData();
            EconomyPurchaseLog.ExposeData();
        }
    }
}
