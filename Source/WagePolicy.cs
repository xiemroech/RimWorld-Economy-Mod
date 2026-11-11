using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 工资政策。
    ///   比例工资 = 劳动(人日) × 劳动日价值 × 工资比例
    ///   实发工资 = min(比例工资, 人日 × 日薪上限)
    ///   剩余价值 = 创造价值 − 实发工资
    ///   相对剩余 = 被日薪上限"截留"的部分
    ///   绝对剩余 = 剩余价值 − 相对剩余
    /// </summary>
    public class WagePolicy : IExposable
    {
        public bool enabled = true;

        // 一个"标准人日"的产出价值（白银）
        public float laborDayValue = 150f;

        // 其中分给劳动者本人的比例，0-1
        public float wageShare = 0.6f;

        // 每人每天最高工资（银/日）。0 = 不限。
        // 一期实发上限 = 本值 × 这一期的小时数 ÷ 24（24 小时就是本值）。
        public float wageCapPerDay = 150f;

        // 个人上限（旧数据，保留兼容）：名单上的人用自己的值，不在名单上的用上面的全局值。
        public List<Pawn> capPawns = new List<Pawn>();
        public List<float> capValues = new List<float>();

        // 个人工资参数：每人可以单独设劳动日价值 / 比例 / 上限 / 标准工时 / 加班，-1 = 用全局。
        public List<WageOverride> overrides = new List<WageOverride>();

        // 标准工作日（人日）。超出这部分才算"延长劳动时间"，产生绝对剩余。
        public float standardWorkday = 1f;

        // 加班工资：可选。开启后超出标准工作日的部分按倍率计薪。
        public bool overtimeEnabled;
        public float overtimeMultiplier = 1.5f;

        // 日薪上限从 20 抬到 150 的一次性迁移标记（不存档字段本身，存的是 dsh_capMigrated150）
        private bool capMigrated150;

        public void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", true);
            Scribe_Values.Look(ref laborDayValue, "laborDayValue", 150f);
            Scribe_Values.Look(ref wageShare, "wageShare", 0.6f);
            Scribe_Values.Look(ref wageCapPerDay, "wageCapPerDay", 150f);
            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                // 老存档里上限还是旧的 20（默认值），一次性抬到 150；玩家自己改过的值不动。
                bool migrated = false;
                Scribe_Values.Look(ref migrated, "dsh_capMigrated150", false);
                if (!migrated && wageCapPerDay > 0f && wageCapPerDay <= 20.01f) wageCapPerDay = 150f;
                capMigrated150 = true;
            }
            else if (Scribe.mode == LoadSaveMode.Saving)
            {
                bool flag = capMigrated150;
                Scribe_Values.Look(ref flag, "dsh_capMigrated150", true);
            }
            Scribe_Values.Look(ref standardWorkday, "standardWorkday", 1f);
            Scribe_Values.Look(ref overtimeEnabled, "overtimeEnabled", false);
            Scribe_Values.Look(ref overtimeMultiplier, "overtimeMultiplier", 1.5f);
            Scribe_Collections.Look(ref capPawns, "dsh_capPawns", LookMode.Reference);
            Scribe_Collections.Look(ref capValues, "dsh_capValues", LookMode.Value);
            if (capPawns == null) capPawns = new List<Pawn>();
            if (capValues == null) capValues = new List<float>();

            Scribe_Collections.Look(ref overrides, "dsh_wageOverrides", LookMode.Deep);
            if (overrides == null) overrides = new List<WageOverride>();
            for (int i = overrides.Count - 1; i >= 0; i--)
            {
                if (overrides[i] == null || overrides[i].pawn == null) overrides.RemoveAt(i);
            }
        }

        /// <summary>取这个人的个人设置；create=false 时没有就返回 null。</summary>
        public WageOverride OverrideOf(Pawn pawn, bool create = false)
        {
            if (pawn == null) return null;
            if (overrides != null)
            {
                for (int i = 0; i < overrides.Count; i++)
                {
                    WageOverride o = overrides[i];
                    if (o != null && o.pawn == pawn) return o;
                }
            }
            if (!create) return null;
            WageOverride n = new WageOverride();
            n.pawn = pawn;
            if (overrides == null) overrides = new List<WageOverride>();
            overrides.Add(n);
            return n;
        }

        public bool HasOverride(Pawn pawn)
        {
            WageOverride o = OverrideOf(pawn);
            return o != null && o.AnyCustom;
        }

        public void ClearOverride(Pawn pawn)
        {
            WageOverride o = OverrideOf(pawn);
            if (o != null) overrides.Remove(o);
        }

        /// <summary>通胀期的工资跟随倍率：由 EconomyInflation 逐段推高，不是存档字段。</summary>
        public float inflationMultiplier = 1f;

        public float LaborDayValueFor(Pawn pawn)
        {
            float inflate = Mathf.Max(1f, inflationMultiplier);
            WageOverride o = OverrideOf(pawn);
            if (o != null && o.laborDayValue >= 0f) return o.laborDayValue * inflate;
            return laborDayValue * inflate;
        }

        public float ShareFor(Pawn pawn)
        {
            WageOverride o = OverrideOf(pawn);
            if (o != null && o.wageShare >= 0f) return o.wageShare;
            return wageShare;
        }

        public float StandardWorkdayFor(Pawn pawn)
        {
            WageOverride o = OverrideOf(pawn);
            if (o != null && o.standardWorkday >= 0f) return o.standardWorkday;
            return standardWorkday;
        }

        public bool OvertimeOnFor(Pawn pawn)
        {
            WageOverride o = OverrideOf(pawn);
            if (o != null && o.overtimeEnabled >= 0) return o.overtimeEnabled == 1;
            return overtimeEnabled;
        }

        public float OvertimeMultFor(Pawn pawn)
        {
            WageOverride o = OverrideOf(pawn);
            if (o != null && o.overtimeMultiplier >= 0f) return o.overtimeMultiplier;
            return overtimeMultiplier;
        }

        /// <summary>这个人的日薪上限：有个人设置就用个人的，否则用全局。</summary>
        public float CapFor(Pawn pawn)
        {
            WageOverride o = OverrideOf(pawn);
            if (o != null && o.capPerDay >= 0f) return o.capPerDay;
            if (pawn != null && capPawns != null)
            {
                for (int i = 0; i < capPawns.Count; i++)
                {
                    if (capPawns[i] == pawn) return capValues[i];
                }
            }
            return wageCapPerDay;
        }

        public bool HasCustomCap(Pawn pawn)
        {
            return IndexOfCap(pawn) >= 0;
        }

        private int IndexOfCap(Pawn pawn)
        {
            if (pawn == null || capPawns == null) return -1;
            for (int i = 0; i < capPawns.Count; i++)
            {
                if (capPawns[i] == pawn) return i;
            }
            return -1;
        }

        public void SetCap(Pawn pawn, float value)
        {
            if (pawn == null) return;
            int i = IndexOfCap(pawn);
            value = Mathf.Clamp(value, 0f, 999f);
            if (i >= 0)
            {
                capValues[i] = value;
                return;
            }
            capPawns.Add(pawn);
            capValues.Add(value);
        }

        /// <summary>取消个人设置，回到全局值。</summary>
        public void ClearCap(Pawn pawn)
        {
            int i = IndexOfCap(pawn);
            if (i < 0) return;
            capPawns.RemoveAt(i);
            capValues.RemoveAt(i);
        }

        public float LaborDays(float laborTicks)
        {
            return laborTicks / GenDate.TicksPerDay;
        }

        /// <summary>这段劳动量创造的总价值（未分配）。pawn 非空时用这个人的劳动日价值。</summary>
        public float ProducedValue(float laborTicks, Pawn pawn = null)
        {
            return LaborDays(laborTicks) * Mathf.Max(0f, LaborDayValueFor(pawn));
        }

        /// <summary>按比例算、未受上限约束时应得的工资（含加班部分）。</summary>
        public float UncappedWage(float laborTicks, Pawn pawn = null)
        {
            float days = LaborDays(laborTicks);
            if (days <= 0f) return 0f;

            float p = Mathf.Max(0f, LaborDayValueFor(pawn));
            float s = Mathf.Clamp01(ShareFor(pawn));

            if (!OvertimeOnFor(pawn))
            {
                return days * p * s;
            }

            float standard = Mathf.Max(0f, StandardWorkdayFor(pawn));
            float baseDays = Mathf.Min(days, standard);
            float otDays = Mathf.Max(0f, days - standard);
            return baseDays * p * s + otDays * p * s * Mathf.Max(1f, OvertimeMultFor(pawn));
        }

        /// <summary>
        /// 实际应发工资。
        /// 日薪上限只约束标准工时以内的部分；加班费不受上限限制，可以超过上限。
        ///
        /// 注意：上限按**真实工作日**算（rawTicks），工资按**加权工作日**算。
        /// 所以高倍率工种（艺术 2.0）拿同样的钟点时间，期望工资更高、但上限不变，
        /// 差额就落在"日薪上限截留"里——这就是工种差距在满意度上的体现。
        /// </summary>
        public float WageFor(float laborTicks, Pawn pawn = null, float rawTicks = -1f)
        {
            float days = LaborDays(laborTicks);
            if (days <= 0f) return 0f;

            float p = Mathf.Max(0f, LaborDayValueFor(pawn));
            float s = Mathf.Clamp01(ShareFor(pawn));

            // 上限按"这一期发多少小时"折算：24 小时 ×1，12 小时 ×0.5，168 小时 ×7。
            // 语义是"赚多少封顶多少"，不做人日乘法：赚 300、上限 200 → 发 200，截留 100。
            float cap = WageCapAmountFor(pawn);

            if (!OvertimeOnFor(pawn))
            {
                return Mathf.Min(days * p * s, cap);
            }

            float standard = Mathf.Max(0f, StandardWorkdayFor(pawn));
            float baseDays = Mathf.Min(days, standard);
            float otDays = Mathf.Max(0f, days - standard);

            float baseWage = Mathf.Min(baseDays * p * s, cap);
            float otWage = otDays * p * s * Mathf.Max(1f, OvertimeMultFor(pawn));
            return baseWage + otWage;
        }

        /// <summary>这一期实际生效的工资上限（银）：日上限 × 这一期的小时数 ÷ 24；日上限为 0 时返回 float.MaxValue（不限）。</summary>
        public float WageCapAmountFor(Pawn pawn)
        {
            float perDay = Mathf.Max(0f, CapFor(pawn));
            if (perDay <= 0f) return float.MaxValue;
            DshEconomySettings st = DshEconomyMod.Settings;
            int everyHours = st != null ? Mathf.Clamp(st.payIntervalHours, 1, 720) : 24;
            return perDay * (everyHours / 24f);
        }

        /// <summary>殖民地留存的剩余价值。</summary>
        public float SurplusFor(float laborTicks, Pawn pawn = null, float rawTicks = -1f)
        {
            return Mathf.Max(0f, ProducedValue(laborTicks, pawn) - WageFor(laborTicks, pawn, rawTicks));
        }

        /// <summary>
        /// 绝对剩余：由"超出标准工作日的劳动时间"创造的剩余。
        /// 开了加班费时，超额的工资成本会抵掉一部分，所以这里按倍率折算。
        /// </summary>
        public float AbsoluteSurplus(float laborTicks, Pawn pawn = null, float rawTicks = -1f)
        {
            float days = LaborDays(laborTicks);
            float excess = Mathf.Max(0f, days - Mathf.Max(0f, standardWorkday));
            if (excess <= 0f) return 0f;

            float p = Mathf.Max(0f, LaborDayValueFor(pawn));
            float s = Mathf.Clamp01(ShareFor(pawn));
            float mult = OvertimeOnFor(pawn) ? Mathf.Max(1f, OvertimeMultFor(pawn)) : 1f;
            float raw = excess * p * (1f - s * mult);
            return Mathf.Clamp(raw, 0f, SurplusFor(laborTicks, pawn, rawTicks));
        }

        /// <summary>相对剩余：标准工作日以内产生的剩余（工资比例 + 上限截留都在这里）。</summary>
        public float RelativeSurplus(float laborTicks, Pawn pawn = null, float rawTicks = -1f)
        {
            return Mathf.Max(0f, SurplusFor(laborTicks, pawn, rawTicks) - AbsoluteSurplus(laborTicks, pawn, rawTicks));
        }
    }

    public static class EconomyWages
    {
        /// <summary>小人是否在干活。用当前工作粗略估计，睡眠/发呆/吃饭/娱乐不算。</summary>
        public static bool IsWorking(Pawn p)
        {
            if (p == null || p.Dead || p.Downed || !p.Spawned) return false;
            if (p.InMentalState || p.Drafted) return false;
            if (EconomyUnrest.IsStriking(p)) return false;   // 罢工期间不算劳动
            Job job = p.CurJob;
            if (job == null || job.def == null) return false;
            if (p.jobs == null || p.jobs.curDriver == null) return false;

            JobDef def = job.def;
            if (def.joyKind != null) return false;
            if (def == JobDefOf.LayDown || def == JobDefOf.Goto || def == JobDefOf.Wait
                || def == JobDefOf.Wait_Wander || def == JobDefOf.Wait_MaintainPosture || def == JobDefOf.Ingest)
            {
                return false;
            }

            // 兜底：按 JobDriver 类型名排除娱乐类工作（很多 mod 的娱乐 Job 没设 joyKind）
            // 类型名判断结果按 Type 缓存，避免每次切换任务都分配字符串
            if (IsJoyDriver(p.jobs.curDriver)) return false;
            return true;
        }

        private static readonly Dictionary<System.Type, bool> joyDriverCache = new Dictionary<System.Type, bool>();

        private static bool IsJoyDriver(JobDriver driver)
        {
            if (driver == null) return false;
            System.Type type = driver.GetType();
            bool cached;
            if (joyDriverCache.TryGetValue(type, out cached)) return cached;

            string name = type.Name;
            bool joy = name.Contains("Joy") || name.Contains("Meditat") || name.Contains("Relax")
                || name.Contains("Social") || name.Contains("Play") || name.Contains("Leisure");
            joyDriverCache[type] = joy;
            return joy;
        }

        public static float WorkRate(Pawn p)
        {
            if (p == null) return 1f;
            float rate = p.GetStatValue(StatDefOf.WorkSpeedGlobal);
            return rate > 0.01f ? rate : 0.01f;
        }

        /// <summary>当前这一 tick 的劳动属于哪个工作类型。</summary>
        public static WorkTypeDef WorkTypeOf(Pawn p)
        {
            return EconomyWorkTypes.Of(p);
        }

        /// <summary>当前劳动量的工资权重（艺术最高、养育次之、手工再次）。</summary>
        public static float WageWeightOf(Pawn p)
        {
            DshEconomySettings s = DshEconomyMod.Settings;
            if (s == null) return 1f;
            return s.GetWorkMult(WorkTypeOf(p));
        }

        /// <summary>给一个人结算工资：按劳动量算钱，从公共库存出，返回实付。</summary>
        public static int PayOne(Pawn pawn, Map map, WagePolicy policy)
        {
            if (pawn == null || map == null || policy == null || !policy.enabled) return 0;

            MapEconomyComponent mcomp = map.GetComponent<MapEconomyComponent>();
            // 军人：固定数额工资，不看劳动量
            if (mcomp != null && mcomp.soldierWageEnabled && EconomyMilitary.IsSoldier(pawn))
            {
                return PaySoldierFixed(pawn, map, mcomp);
            }

            MapEconomyComponent mcomp0 = map.GetComponent<MapEconomyComponent>();
            // 工资口径 = 界面上那份工时表（按工种加权），不再单独读账本：
            // 以前两套数并存，界面上有、发薪却是 0，读档后更容易对不上。
            float labor = mcomp0 != null ? mcomp0.LaborOf(pawn) : EconomyLedger.GetLabor(pawn);
            float rawLabor = mcomp0 != null ? mcomp0.RawLaborOf(pawn) : EconomyLedger.GetRawLabor(pawn);
            if (labor <= 0f) return 0;

            // 把这一期的工资拆成三笔，分别记账（满意度按这三笔各自扣分）：
            //   创造价值(100%) → 按工资比例该给他的部分 → 受日薪上限约束后的应发 → 实发
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            float produced = comp != null ? comp.ProducedValueOf(pawn, policy) : policy.ProducedValue(labor, pawn);
            float shareWage = comp != null ? comp.ExpectedWageOf(pawn, policy) : policy.UncappedWage(labor);
            float capLimitWage = policy.WageFor(labor, pawn, rawLabor);

            EconomyLedger.SetLastExpected(pawn, shareWage);
            float shareCut = Mathf.Max(0f, produced - shareWage);          // 比例抽走的剩余价值
            float capCut = Mathf.Max(0f, shareWage - capLimitWage);        // 日薪上限截留

            float dueFloat = capLimitWage;
            int due = Mathf.RoundToInt(dueFloat);
            // 有劳动但不足 1 银时也给 1，否则小额工资永远发不出去
            if (due <= 0 && dueFloat > 0f) due = 1;
            DevLog.Msg("[WAGE] " + pawn.LabelShort
                + " 劳动=" + labor.ToString("0") + "tick(" + (labor / GenDate.TicksPerDay).ToString("0.000") + "日)"
                + " 实际=" + EconomyLedger.GetRawLabor(pawn).ToString("0") + "tick(" + (EconomyLedger.GetRawLabor(pawn) / GenDate.TicksPerDay).ToString("0.000") + "日)"
                + " 日值=" + policy.LaborDayValueFor(pawn).ToString("0.#")
                + " 比例=" + policy.ShareFor(pawn).ToString("0.00")
                + " 创造=" + produced.ToString("0.#")
                + " 应得=" + shareWage.ToString("0.#")
                + " 上限=" + policy.CapFor(pawn).ToString("0.#") + "/日 本题上限=" + policy.WageCapAmountFor(pawn).ToString("0.#")
                + " 封顶后=" + capLimitWage.ToString("0.#")
                + " 截留=" + capCut.ToString("0.#")
                + " 实发=" + due);
            if (due <= 0)
            {
                EconomyLedger.SetPayCuts(pawn, shareCut, capCut, 0f);
                return 0;
            }

            int available = EconomyBank.ColonySilver(map);
            int pay = Mathf.Min(due, Mathf.Max(0, available));
            int taken = pay > 0 ? EconomyBank.SplitSilverToPrivate(map, pawn, pay) : 0;

            // 应发但没发出去的部分（国库空）单独记一笔
            EconomyLedger.SetPayCuts(pawn, shareCut, capCut, Mathf.Max(0f, due - taken));

            if (taken <= 0) return 0;

            float consumed = labor * ((float)taken / due);
            EconomyLedger.ConsumeLabor(pawn, consumed);
            // 实际工时按同样比例结算，保证奖金排名和工资用的是同一段工作
            EconomyLedger.ConsumeRawLabor(pawn, EconomyLedger.GetRawLabor(pawn) * ((float)taken / due));
            EconomyLedger.AddIncome(pawn, taken);
            if (comp != null) comp.ClearWorkHours(pawn);
            return taken;
        }

        /// <summary>军人固定工资：每期固定数额，从国库出，不受劳动量与日薪上限影响。</summary>
        private static int PaySoldierFixed(Pawn pawn, Map map, MapEconomyComponent comp)
        {
            int due = Mathf.Max(0, comp.soldierWage);
            if (due <= 0) return 0;
            int available = EconomyBank.ColonySilver(map);
            int pay = Mathf.Min(due, Mathf.Max(0, available));
            int taken = pay > 0 ? EconomyBank.SplitSilverToPrivate(map, pawn, pay) : 0;
            EconomyLedger.SetPayCuts(pawn, 0f, 0f, Mathf.Max(0f, due - taken));
            if (taken > 0)
            {
                EconomyLedger.AddIncome(pawn, taken);
                comp.ClearWorkHours(pawn);
            }
            return taken;
        }

        public static int PayAll(Map map, WagePolicy policy)
        {
            if (map == null || policy == null || !policy.enabled) return 0;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            int total = 0;
            for (int i = 0; i < colonists.Count; i++)
            {
                total += PayOne(colonists[i], map, policy);
            }
            return total;
        }

        /// <summary>征用某人名下的私银：撕掉标签变回公共，物品留在原处（含背包内）。</summary>
        public static int Confiscate(Pawn pawn, Map map)
        {
            if (pawn == null || map == null) return 0;

            int amount = 0;
            List<Thing> stacks = EconomyBank.AllSilverStacks(map);
            for (int i = 0; i < stacks.Count; i++)
            {
                Thing t = stacks[i];
                if (t == null || t.Destroyed) continue;
                if (EconomyPrivate.OwnerOf(t) != pawn) continue;
                amount += t.stackCount;
                EconomyPrivate.SetPrivate(t, null);
            }

            if (amount > 0 && pawn.needs != null && pawn.needs.mood != null)
            {
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(EconomyPrivate.ConfiscateThought);
                if (thought != null) pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
            }
            return amount;
        }
    }
}
