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

        private static readonly Dictionary<Pawn, LaborSession> open = new Dictionary<Pawn, LaborSession>();

        public static bool Active = true;

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

        private static int Now
        {
            get { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; }
        }
    }
}
