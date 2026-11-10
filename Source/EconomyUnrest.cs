using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 低满意度引发的三种状态：
    ///   罢工  —— 拒绝工作，会传染
    ///   暴动  —— 进入原版狂暴（攻击身边人、破坏），会传染
    ///   革命  —— 极强传染，狂暴但不逃跑
    /// </summary>
    public static class EconomyUnrest
    {
        public const float StrikeThreshold = 20f;
        public const float RiotThreshold = 10f;
        public const float RevolutionThreshold = 4f;

        private static readonly Dictionary<Pawn, int> striking = new Dictionary<Pawn, int>();
        // 已经决定逃跑的人：不能再被 Tick/Spread 重新拉回暴动。
        private static readonly HashSet<Pawn> fleeing = new HashSet<Pawn>();

        public static bool IsFleeing(Pawn p)
        {
            return p != null && fleeing.Contains(p);
        }

        public static bool IsUnrestPawn(Pawn p)
        {
            return p != null && unrest.Contains(p);
        }

        public static Pawn LockedTargetOf(Pawn p)
        {
            Pawn t;
            if (p == null || !unrestTarget.TryGetValue(p, out t)) return null;
            if (IsFleeing(p)) return null;
            if (t == null || t.Dead || t.Downed || !t.Spawned) return null;
            if (!t.IsColonist) return null;                                                                   // 只打殖民者，不打动物/访客/建筑
            if (t.InMentalState || IsUnrestPawn(t) || IsFleeing(t)) return null;                              // 不打自己人/逃跑的人
            if (t.Map != p.Map) return null;
            // 不再用 CanReach/Distance 在这里否决目标：
            // 远程 AI 不需要走到目标身边；CanReach 还会递归触发 ForceHostileTo，
            // 之前正是它把非 Pawn 目标送进 patch 导致 NullReferenceException。
            return t;
        }

        private static readonly HashSet<Pawn> unrest = new HashSet<Pawn>();
        private static readonly Dictionary<Pawn, Pawn> unrestTarget = new Dictionary<Pawn, Pawn>();

        public static Pawn RawTargetOf(Pawn p)
        {
            Pawn t;
            return p != null && unrestTarget.TryGetValue(p, out t) ? t : null;
        }

        public static readonly Dictionary<Pawn, HostilityResponseMode> DriverBackup = new Dictionary<Pawn, HostilityResponseMode>();

        public static void RestoreDriver(Pawn p, bool fallbackToFlee = false)
        {
            if (p == null || p.playerSettings == null) return;
            HostilityResponseMode m;
            if (DriverBackup.TryGetValue(p, out m))
            {
                p.playerSettings.hostilityResponse = m;
                DriverBackup.Remove(p);
            }
            else if (fallbackToFlee)
            {
                // 旧存档/旧精神状态可能没有备份；逃跑时必须脱离 Attack。
                p.playerSettings.hostilityResponse = HostilityResponseMode.Flee;
            }
        }

        public static void ClearRuntimeState()
        {
            lastDraftDiag.Clear();
            striking.Clear();
            DriverBackup.Clear();
            unrest.Clear();
            unrestTarget.Clear();
            fleeing.Clear();
            lastTick = 0;
            lastSharedTarget = null;
        }

        public static bool IsStriking(Pawn p)
        {
            int until;
            if (p == null || !striking.TryGetValue(p, out until)) return false;
            if (Find.TickManager.TicksGame >= until)
            {
                striking.Remove(p);
                if (p.InMentalState && p.MentalStateDef == StrikeDef)
                    p.mindState.mentalStateHandler.Reset();
                return false;
            }
            return true;
        }

        public static int StrikerCount(Map map)
        {
            if (map == null) return 0;
            int n = 0;
            foreach (var kv in striking)
            {
                if (kv.Key != null && kv.Key.Map == map) n++;
            }
            return n;
        }

        // ── 调试用：强制触发某种状态 ──

        public static int ForceStrike(Map map, float satisfaction)
        {
            int n = 0;
            foreach (Pawn p in Colonists(map))
            {
                EconomyLedger.AddSatisfaction(p, satisfaction - EconomyLedger.GetSatisfaction(p));
                StartStrike(p, 120000);
                n++;
            }
            return n;
        }

        public static int ForceRiot(Map map, float satisfaction)
        {
            int n = 0;
            List<Pawn> picked = new List<Pawn>(Colonists(map));
            picked.SortByDescending(p => TaxPopulation.PawnPrivateWealth(p));
            if (picked.Count > 3) picked.RemoveRange(3, picked.Count - 3);
            foreach (Pawn p in picked)
            {
                EconomyLedger.AddSatisfaction(p, satisfaction - EconomyLedger.GetSatisfaction(p));
                StartBerserk(p, false);
                if (p.InMentalState) n++;
            }
            return n;
        }

        public static int ForceRevolution(Map map, float satisfaction)
        {
            int n = 0;
            List<Pawn> picked = new List<Pawn>(Colonists(map));
            picked.SortBy(p => EconomyLedger.GetSatisfaction(p));
            if (picked.Count > 3) picked.RemoveRange(3, picked.Count - 3);
            foreach (Pawn p in picked)
            {
                EconomyLedger.AddSatisfaction(p, satisfaction - EconomyLedger.GetSatisfaction(p));
                StartBerserk(p, true);
                if (p.InMentalState) n++;
            }
            return n;
        }

        private static List<Pawn> Colonists(Map map)
        {
            return map != null && map.mapPawns != null
                ? new List<Pawn>(map.mapPawns.FreeColonists)
                : new List<Pawn>();
        }

        private static int lastTick;
        private static int lastUnrestRoll;
        private static Pawn lastSharedTarget;
        private static readonly Dictionary<int, string> lastDraftDiag = new Dictionary<int, string>();

        public static void Tick(Map map, int interval)
        {
            bool fast = DshEconomyMod.Settings != null && DshEconomyMod.Settings.debugMode && DshEconomyMod.Settings.devFastSpread;
            bool debug = DshEconomyMod.Settings != null && DshEconomyMod.Settings.debugMode;
            int gate = fast ? Mathf.Min(interval, 15) : interval;
            if (gate > 0 && Find.TickManager.TicksGame - lastTick < gate) return;
            lastTick = Find.TickManager.TicksGame;
            if (map == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            bool groupHasCombat = HasGroupCombatTarget(colonists);
            Pawn sharedTarget = ChooseSharedTarget(colonists);
            if (sharedTarget != null)
            {
                // 围殴：每 tick 先把所有暴动/革命者的目标强制统一，
                // 避免各人保留上一 tick 的不同目标。
                for (int si = 0; si < colonists.Count; si++)
                {
                    Pawn sp = colonists[si];
                    if (sp == null || sp.Dead || sp.Downed || IsFleeing(sp)) continue;
                    MentalState sms = sp.mindState != null ? sp.mindState.mentalStateHandler.CurState : null;
                    if (!IsCombatMentalState(sms)) continue;
                    unrestTarget[sp] = sharedTarget;
                    if (sp.mindState != null) sp.mindState.enemyTarget = sharedTarget;
                }
            }

            if (debug)
            {
                for (int di = 0; di < colonists.Count; di++)
                {
                    Pawn dp = colonists[di];
                    if (dp == null || !dp.IsColonist || !dp.Drafted || dp.InMentalState) continue;
                    Job dj = dp.CurJob;
                    if (dj == null) continue;
                    string dg = dj.jobGiver != null ? dj.jobGiver.GetType().Name : "null";
                    string dk = dj.def.defName + "|" + dg;
                    string old;
                    if (lastDraftDiag.TryGetValue(dp.thingIDNumber, out old) && old == dk) continue;
                    lastDraftDiag[dp.thingIDNumber] = dk;
                    DevLog.Msg("[DRAFT] " + dp.LabelShort + " job=" + dj.def.defName
                        + " giver=" + dg
                        + " hostile=" + (dp.playerSettings != null ? dp.playerSettings.hostilityResponse.ToString() : "?"));
                }
            }

            // 每秒刷新锁定目标：目标死了/跑远/没钱就换人
            List<Pawn> all = new List<Pawn>(unrest);
            for (int ui = 0; ui < all.Count; ui++)
            {
                Pawn up = all[ui];
                if (up == null || up.Dead || !up.Spawned || up.Map != map || !up.InMentalState)
                {
                    unrest.Remove(up);
                    unrestTarget.Remove(up);
                    continue;
                }
                Pawn rawTarget = RawTargetOf(up);
                Verb atkVerb = up.TryGetAttackVerb(null);
                bool reach = rawTarget != null && up.CanReach(rawTarget, PathEndMode.Touch, Danger.Some, true, false);
                if (debug)
                {
                    DevLog.Msg("[GRE] " + up.LabelShort
                        + " state=" + (up.MentalStateDef != null ? up.MentalStateDef.defName : "null")
                        + " target=" + (rawTarget != null ? rawTarget.LabelShort : "null")
                        + " dist=" + (rawTarget != null ? up.Position.DistanceTo(rawTarget.Position).ToString("0") : "-")
                        + " reach=" + reach
                        + " tMental=" + (rawTarget != null && rawTarget.InMentalState)
                        + " verb=" + (atkVerb != null ? atkVerb.ToString() : "null")
                        + " violent=" + up.WorkTagIsDisabled(WorkTags.Violent)
                        + " locked=" + (LockedTargetOf(up) != null ? LockedTargetOf(up).LabelShort : "null")
                        + " job=" + (up.CurJob != null ? up.CurJob.def.defName : "null")
                        + " jobTarget=" + (up.CurJob != null && up.CurJob.targetA.Thing != null ? up.CurJob.targetA.Thing.LabelShort : "null")
                        + " enemy=" + (up.mindState != null && up.mindState.enemyTarget != null ? up.mindState.enemyTarget.LabelShort : "null"));
                }

                Pawn locked = LockedTargetOf(up);
                if (locked != null)
                {
                    // 只指定"盯着谁"，怎么打交给 AI（原版 / CE 自己决策）
                    continue;
                }

                Pawn best = null;
                int bestSilver = -1;
                int bestDist = 9999;
                for (int ci = 0; ci < colonists.Count; ci++)
                {
                    Pawn c = colonists[ci];
                    if (c == null || c == up || c.Dead || c.Downed) continue;
                    if (c.InMentalState || IsUnrestPawn(c) || IsFleeing(c)) continue;
                    int silver = EconomyBank.PrivateSilver(up.Map, c);
                    int dist = (int)up.Position.DistanceTo(c.Position);
                    if (dist < bestDist - 2)   // 距离差 >2 格就换更近的
                    {
                        bestDist = dist; best = c;
                    }
                    if (silver > bestSilver && dist <= bestDist + 8)
                    {
                        bestSilver = silver; bestDist = dist; best = c;
                    }
                }
                if (best != null)
                {
                    unrestTarget[up] = best;
                    DevLog.Msg("[DSH-ECONOMY] 锁定目标：" + up.LabelShort + " → " + best.LabelShort + "（私银 " + bestSilver + "）");
                }
            }

            // 逃跑的人：持续往地图边缘走，到了边缘就真正离开地图。
            // 不再经过暴动/革命的 Tick，否则会被重新拉回来。
            List<Pawn> fleeList = new List<Pawn>(fleeing);
            for (int fi = 0; fi < fleeList.Count; fi++)
            {
                Pawn fp = fleeList[fi];
                if (fp == null || fp.Dead || !fp.Spawned || fp.Map != map || fp.Downed)
                {
                    fleeing.Remove(fp);
                    continue;
                }
                if (fp.IsPrisoner)
                {
                    fleeing.Remove(fp);
                    continue;
                }
                if (fp.InMentalState)
                {
                    // GiveUpExit 会自己用 JobGiver_ExitMapRandom 离场，不能再清掉 fleeing 或改派 Goto。
                    if (fp.MentalStateDef != null && fp.MentalStateDef.defName == "GiveUpExit") continue;
                    fleeing.Remove(fp);
                    continue;
                }

                int dx = Mathf.Min(fp.Position.x, map.Size.x - 1 - fp.Position.x);
                int dz = Mathf.Min(fp.Position.z, map.Size.z - 1 - fp.Position.z);
                if (Mathf.Min(dx, dz) <= 2)
                {
                    fleeing.Remove(fp);
                    fp.ExitMap(false, Rot4.Random);
                    DevLog.Msg("[DSH-ECONOMY] 暴动：" + fp.LabelShort + " 已离开殖民地");
                    continue;
                }

                Job fj = fp.CurJob;
                if (fj == null || fj.def != JobDefOf.Goto)
                {
                    IntVec3 edge = CellFinder.RandomEdgeCell(map);
                    fp.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, edge), JobCondition.InterruptForced);
                }
            }

            // 处于暴动/革命的人：如果还在干普通活，强行停掉（有些 mod 会重新派活）
            // 只做清理，不再强制派任务：交给 JobGiver_Berserk 自己决策
            for (int mi = 0; mi < colonists.Count; mi++)
            {
                Pawn mp = colonists[mi];
                if (mp == null || mp.Dead || mp.Downed) continue;
                MentalState ms = mp.mindState != null ? mp.mindState.mentalStateHandler.CurState : null;
                if (ms == null || ms.def == null) continue;
                if (!IsCombatMentalState(ms)) continue;

                Job mj = mp.CurJob;
                if (mj == null) continue;

                string dn = mj.def.defName;
                bool attacking = dn == "AttackMelee" || dn == "AttackStatic";

                Pawn t = LockedTargetOf(mp);
                if (sharedTarget != null && t != sharedTarget)
                {
                    // 群体集火：所有人优先打同一个目标，不再各挑各的。
                    unrestTarget[mp] = sharedTarget;
                    if (mp.mindState != null) mp.mindState.enemyTarget = sharedTarget;
                    if (attacking) mp.jobs.StopAll();
                    t = sharedTarget;
                }
                if (t == null)
                {
                    // 目标失效时连闲逛/等待也停掉，让 ThinkTree 下一 tick 重新决策；
                    // 否则会永远卡在 GotoWander，JobGiver 根本没机会运行。
                    if (attacking || dn == "GotoWander" || dn == "Wait_Wander" || dn == "Wait_Combat")
                        mp.jobs.StopAll();

                    // 没有可打的人：如果群体里还有人在打，先不单独逃离；
                    // 全群体都没有目标时，直接进入 GiveUpExit 逃离殖民地。
                    // 已移除放火逻辑。
                    if (groupHasCombat) continue;
                    StartFlee(mp, ms.def.label);
                    continue;
                }

                if (attacking)
                {
                    if (mj.targetA.Thing == t) continue;  // 正在打正确目标
                    mp.jobs.StopAll();                    // 打错人 → 停掉让 AI 重选
                    continue;
                }

                // CE 换弹、原版站定射击、走到射击位：都不能打断。
                // Wait_Combat 的 jobTarget 可能为 null，由 JobDriver_Wait 自己索敌开火；
                // 之前这里无条件 StopAll，导致每 tick 举枪又放下，看起来就是“卡枪”。
                if (dn == "ReloadWeapon" || dn == "CE_Reload"
                    || dn == "Wait_Combat" || dn == "Goto") continue;

                // 闲逛/普通工作：停掉，让 ThinkTree 下一 tick 重新派战斗 job
                mp.jobs.StopAll();
            }

            // 罢工：状态本身是自定义 DshEco_Strike，ThinkTree 套用原版茫然徘徊；
            // 这里只负责把周期钉死为两天，并在状态提前自然结束时重新接上。
            int nowTicks = Find.TickManager.TicksGame;
            for (int si = 0; si < colonists.Count; si++)
            {
                Pawn sp = colonists[si];
                if (sp == null || sp.Dead || sp.Downed || sp.Drafted)
                {
                    striking.Remove(sp);
                    continue;
                }

                int until;
                if (!striking.TryGetValue(sp, out until)) continue;

                if (nowTicks >= until)
                {
                    striking.Remove(sp);
                    if (sp.InMentalState && sp.MentalStateDef == StrikeDef)
                        sp.mindState.mentalStateHandler.Reset();
                    continue;
                }

                if (!sp.InMentalState && StrikeDef != null)
                {
                    sp.mindState.mentalStateHandler.TryStartMentalState(
                        StrikeDef, "罢工", true, true, false, null, false, false, false);
                }
            }

            // 触发判定按「游戏内小时」掷一次骰，而不是每 60 tick 掷一次——
            // 后者一小时掷 40 次 15%，等于必触发，完全没有"概率"可言。
            bool rollNow = fast || nowTicks - lastUnrestRoll >= GenDate.TicksPerHour;
            if (rollNow) lastUnrestRoll = nowTicks;

            for (int i = 0; rollNow && i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead || p.Downed) continue;

                if (p.InMentalState || IsFleeing(p)) continue;      // 已经处在精神状态/正在逃跑，不要重新拉回暴动
                float sat = EconomyLedger.GetSatisfaction(p);
                if (sat > StrikeThreshold) continue;
                float triggerChance = fast ? 0.75f : 0.15f;
                if (Rand.Chance(triggerChance) == false) continue;

                // 革命 > 暴动 > 罢工：满意度越低越激烈
                if (sat <= RevolutionThreshold)
                {
                    StartBerserk(p, true);
                    Spread(map, p, 0.55f, true);
                }
                else if (sat <= RiotThreshold)
                {
                    StartBerserk(p, false);
                    Spread(map, p, 0.30f, false);
                }
                else
                {
                    StartStrike(p, 120000);
                    Spread(map, p, 0.20f, false);
                }
            }
        }

        public static MentalStateDef StrikeDef
        {
            // 用自定义“罢工”def，行为套用原版茫然徘徊的 ThinkTree。
            get { return DefDatabase<MentalStateDef>.GetNamedSilentFail("DshEco_Strike"); }
        }

        private static void StartStrike(Pawn p, int durationTicks)
        {
            if (EconomyMilitary.IsSoldier(p))
            {
                // 军人不会主动挑起动乱
                return;
            }

            if (p == null || p.Dead || p.Downed || p.Drafted) return;
            if (IsStriking(p)) return;
            striking[p] = Find.TickManager.TicksGame + durationTicks;

            MentalStateDef strikeDef = StrikeDef;
            if (strikeDef != null)
            {
                p.mindState.mentalStateHandler.TryStartMentalState(
                    strikeDef, "罢工", true, true, false, null, false, false, false);
            }

            Messages.Message(p.LabelShort + " 开始罢工（满意度 " + EconomyLedger.GetSatisfaction(p).ToString("0") + "）",
                p, MessageTypeDefOf.NegativeEvent, false);
        }

        private static MentalStateDef RiotDef
        {
            get { return DefDatabase<MentalStateDef>.GetNamedSilentFail("DshEco_Riot"); }
        }

        private static MentalStateDef RevolutionDef
        {
            get { return DefDatabase<MentalStateDef>.GetNamedSilentFail("DshEco_Revolution"); }
        }

        /// <summary>进入暴动/革命时：优先攻击最富的人。</summary>
        public static void OnRiotStart(Pawn pawn, bool revolution)
        {
            if (pawn == null || pawn.Map == null) return;

            Pawn richest = null;
            float best = -1f;
            List<Pawn> colonists = pawn.Map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn t = colonists[i];
                if (t == null || t == pawn || t.Dead || t.Downed) continue;
                if (t.InMentalState || IsUnrestPawn(t) || IsFleeing(t)) continue;
                float w = TaxPopulation.PawnPrivateWealth(t);
                if (w > best) { best = w; richest = t; }
            }
            if (richest == null) return;

            // 财富为 0 也可以成为目标；革命/暴动不能因为没有“富人”就无限逛街。
            unrestTarget[pawn] = richest;
            DevLog.Msg("[DSH-ECONOMY] " + (revolution ? "革命" : "暴动") + "：" + pawn.LabelShort
                + " 冲向最富的 " + richest.LabelShort + "（私有财富 " + best.ToString("0") + "）");
            ApplySharedTarget(pawn, revolution ? "革命" : "暴动");
        }

        /// <summary>每 tick：暴动打不过就逃；两者都会偶尔纵火。</summary>
        public static void RiotTick(Pawn pawn, bool revolution)
        {
            if (pawn == null || pawn.Dead || pawn.Map == null) return;
            if (pawn.Downed) return;

            float health = pawn.health != null ? pawn.health.summaryHealth.SummaryHealthPercent : 1f;

            // 暴动：血量低于 50% 就放弃、逃离殖民地（非战斗状态，可被拘捕）
            if (!revolution && health <= 0.5f)
            {
                StartFlee(pawn, "暴动：" + pawn.LabelShort + " 健康低于50%，逃离殖民地");
                return;
            }

            // 暴动和革命使用同一套战斗目标逻辑：群体围殴、统一 enemyTarget。
            ApplySharedTarget(pawn, revolution ? "革命" : "暴动");

            // 攻击交给原版精神状态 AI（Berserk 会自动用枪/近战找人打），
            // 我们只负责"打不过就逃"和传染。
            bool fastSpread = DshEconomyMod.Settings != null && DshEconomyMod.Settings.debugMode && DshEconomyMod.Settings.devFastSpread;
            float spreadChance = (revolution ? 0.35f : 0.15f) * (fastSpread ? 5f : 1f);
            Spread(pawn.Map, pawn, Mathf.Clamp01(spreadChance), revolution);
        }
        private static Verb GetRangedVerb(Pawn attacker)
        {
            ThingWithComps eq = attacker != null && attacker.equipment != null ? attacker.equipment.Primary : null;
            CompEquippable ce = eq != null ? eq.GetComp<CompEquippable>() : null;
            Verb verb = ce != null ? ce.PrimaryVerb : null;
            if (verb == null || verb.verbProps == null) return null;
            if (verb.verbProps.IsMeleeAttack || verb.verbProps.range <= 2f) return null;
            return verb;
        }

        public static bool CombatExtendedActive
        {
            get
            {
                try
                {
                    foreach (ModContentPack m in LoadedModManager.RunningModsListForReading)
                    {
                        if (m != null && m.Name != null && m.Name.Contains("Combat Extended")) return true;
                    }
                }
                catch (System.Exception ex) { EcoLog.WarnOnce("EconomyUnrest", ex); }
                return false;
            }
        }

        private static Job MakeAttackJob(Pawn attacker, Pawn victim)
        {
            // CE 会接管射击任务，交给它反而卡住；CE 下统一用近战追击
            if (CombatExtendedActive) return JobMaker.MakeJob(JobDefOf.AttackMelee, victim);

            Verb v = GetRangedVerb(attacker);
            if (v != null && attacker.Position.DistanceTo(victim.Position) <= v.verbProps.range - 1f)
            {
                return JobMaker.MakeJob(JobDefOf.AttackStatic, victim);   // 射程内：开枪
            }
            return JobMaker.MakeJob(JobDefOf.AttackMelee, victim);        // 射程外：追过去
        }

        /// <summary>
        /// 让暴动/革命共用同一套群体目标：所有 DshEco_ 状态者锁定同一个人。
        /// 不区分暴动和革命，避免两套逻辑越走越偏。
        /// </summary>
        private static void ApplySharedTarget(Pawn anchor, string label)
        {
            if (anchor == null || anchor.Map == null) return;
            List<Pawn> colonists = anchor.Map.mapPawns.FreeColonists;
            Pawn shared = ChooseSharedTarget(colonists);
            if (shared == null) return;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead || p.Downed || IsFleeing(p)) continue;
                MentalState ms = p.mindState != null ? p.mindState.mentalStateHandler.CurState : null;
                if (!IsCombatMentalState(ms)) continue;
                unrestTarget[p] = shared;
                if (p.mindState != null) p.mindState.enemyTarget = shared;
            }

            if (lastSharedTarget != shared)
            {
                lastSharedTarget = shared;
                DevLog.Msg("[SHARED] " + label + " 围殴目标：" + shared.LabelShort);
            }
        }

        /// <summary>选一个全群体共享的目标：优先当前正在打的人，其次最富的非革命者。</summary>
        private static Pawn ChooseSharedTarget(List<Pawn> colonists)
        {
            if (colonists == null) return null;

            Pawn anchor = null;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead || p.Downed || IsFleeing(p)) continue;
                MentalState ms = p.mindState != null ? p.mindState.mentalStateHandler.CurState : null;
                if (!IsCombatMentalState(ms)) continue;
                anchor = p;
                break;
            }
            if (anchor == null) return null;

            // 1) 已经有人锁定了目标：全群体沿用这个目标。
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead || p.Downed || IsFleeing(p)) continue;
                MentalState ms = p.mindState != null ? p.mindState.mentalStateHandler.CurState : null;
                if (!IsCombatMentalState(ms)) continue;
                Pawn locked = LockedTargetOf(p);
                if (locked != null && !locked.Dead && !locked.Destroyed && locked.Spawned && locked.Map == anchor.Map)
                    return locked;
            }

            // 2) 有人正在攻击一个合法目标：沿用该目标。
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead || p.Downed || IsFleeing(p)) continue;
                MentalState ms = p.mindState != null ? p.mindState.mentalStateHandler.CurState : null;
                if (!IsCombatMentalState(ms)) continue;
                Job j = p.CurJob;
                if (j == null) continue;
                if (j.def != JobDefOf.AttackMelee && j.def != JobDefOf.AttackStatic) continue;
                Pawn jt = j.targetA.Thing as Pawn;
                if (jt != null && jt.IsColonist && !jt.Dead && !jt.Downed && jt.Spawned
                    && !jt.InMentalState && !IsUnrestPawn(jt) && !IsFleeing(jt))
                    return jt;
            }

            // 3) 还没有目标：以固定 anchor 选一次，保证所有人选中同一个人。
            return FindRichestNear(anchor);
        }


        private static bool IsCombatMentalState(MentalState ms)
        {
            return ms != null && ms.def != null
                && (ms.def.defName == "DshEco_Riot" || ms.def.defName == "DshEco_Revolution");
        }

        /// <summary>是否还有任何暴动/革命者在战斗、放火或持有有效目标。</summary>
        private static bool HasGroupCombatTarget(List<Pawn> colonists)
        {
            if (colonists == null) return false;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead || p.Downed) continue;
                MentalState ms = p.mindState != null ? p.mindState.mentalStateHandler.CurState : null;
                if (ms == null || ms.def == null) continue;
                if (!IsCombatMentalState(ms)) continue;

                if (LockedTargetOf(p) != null) return true;

                Job j = p.CurJob;
                if (j != null && j.targetA.Thing != null && !j.targetA.Thing.Destroyed)
                {
                    if (j.def == JobDefOf.AttackMelee || j.def == JobDefOf.AttackStatic || j.def == JobDefOf.Ignite)
                        return true;
                }

                if (p.mindState != null)
                {
                    Thing et = p.mindState.enemyTarget;
                    if (et != null && !et.Destroyed && et.Spawned && et.Map == p.Map) return true;
                }
            }
            return false;
        }


        /// <summary>
        /// 真正进入原版 GiveUpExit（MentalStateCritical -> JobGiver_ExitMapRandom），
        /// 不再只是 Reset 后手动 Goto 边缘；失败时才退回旧逻辑。
        /// </summary>
        private static void StartFlee(Pawn p, string reason)
        {
            if (p == null || p.Map == null || p.Dead || p.Downed) return;

            fleeing.Add(p);
            unrest.Remove(p);
            unrestTarget.Remove(p);
            RestoreDriver(p, true);

            MentalStateDef giveUp = DefDatabase<MentalStateDef>.GetNamedSilentFail("GiveUpExit");
            if (giveUp != null)
            {
                try
                {
                    if (p.mindState.mentalStateHandler.TryStartMentalState(
                        giveUp, reason, true, true, false, null, false, false, false))
                    {
                        // GiveUpExit 的 AI 不需要 hostilityResponse；但保持 Flee 可避免
                        // 状态结束后又回到 Attack。
                        if (p.playerSettings != null)
                            p.playerSettings.hostilityResponse = HostilityResponseMode.Flee;
                        DevLog.Msg("[DSH-ECONOMY] " + reason + "（GiveUpExit）");
                        return;
                    }
                }
                catch (System.Exception e)
                {
                    Log.Warning("[DSH-ECONOMY] 启动 GiveUpExit 失败：" + e.Message);
                }
            }

            // 兜底：没有 GiveUpExit def 或启动失败，仍走原来的手动离场。
            p.mindState.mentalStateHandler.Reset();
            IntVec3 edge = CellFinder.RandomEdgeCell(p.Map);
            p.jobs.StartJob(JobMaker.MakeJob(JobDefOf.Goto, edge), JobCondition.InterruptForced);
            DevLog.Msg("[DSH-ECONOMY] " + reason + "（Goto 兜底）");
        }


        private static bool IsAnyUnrestTarget(Pawn t)
        {
            if (t == null) return false;
            foreach (KeyValuePair<Pawn, Pawn> kv in unrestTarget)
            {
                if (kv.Value == t) return true;
            }
            return false;
        }

        private static Pawn FindRichestNear(Pawn self)
        {
            if (self == null || self.Map == null) return null;
            Pawn best = null;
            float bestWealth = -1f;
            List<Pawn> colonists = self.Map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn t = colonists[i];
                if (t == null || t == self || t.Dead || t.Downed) continue;
                if (t.InMentalState || IsUnrestPawn(t) || IsFleeing(t)) continue;
                float w = TaxPopulation.PawnPrivateWealth(t);
                if (w > bestWealth) { bestWealth = w; best = t; }
            }
            return best;   // 没有富人时也会选一个合法殖民者，避免革命者因无目标闲逛
        }

        private static void StartBerserk(Pawn p, bool revolution)
        {
            if (EconomyMilitary.IsSoldier(p))
            {
                // 军人不会主动挑起动乱
                return;
            }

            if (p == null || p.Dead || p.Downed || p.InMentalState || IsFleeing(p)) return;
            if (IsAnyUnrestTarget(p)) return;   // 正在被别人当目标打的人不能被传播成革命者

            if (revolution && p.Map != null && !unrest.Contains(p))
            {
                int tot = p.Map.mapPawns.FreeColonists.Count;
                if (unrest.Count >= Mathf.Max(1, tot / 2)) return;   // 全局上限
            }
            // 原版狂暴的 AI 认的是 "def == Berserk"，自定义 def 会退化成只会走路，
            // 所以状态一律用原版 Berserk，目标靠 enemyTarget 指定。
            // 用带名字的自定义 def，但 stateClass 指向原版 MentalState_Berserk，
            // 这样 AI / CE 认得出"这是狂暴行为"，同时头顶显示"暴动/革命"。
            MentalStateDef def = revolution ? RevolutionDef : RiotDef;
            if (def == null) def = MentalStateDefOf.Berserk;
            if (def == null) return;
            // 先尝试进入精神状态，成功后再登记，避免"没进状态却占着 unrest"的脏状态
            bool ok = false;
            try { ok = p.mindState.mentalStateHandler.TryStartMentalState(def, revolution ? "革命" : "暴动", true, false, false, null, false, false, false); }
            catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 启动精神状态失败：" + e.Message); }
            if (!ok)
            {
                Log.Warning("[DSH-ECONOMY] 精神状态未启动，跳过登记：" + p.LabelShort);
                return;
            }
            unrest.Add(p);
            Pawn pickTarget = FindRichestNear(p);
            if (pickTarget != null) unrestTarget[p] = pickTarget;
        }

        /// <summary>向周围满意度同样偏低的人传播。</summary>
        private static void Spread(Map map, Pawn source, float chance, bool revolution)
        {
            bool fast = DshEconomyMod.Settings != null && DshEconomyMod.Settings.debugMode && DshEconomyMod.Settings.devFastSpread;

            // 高传染也留一半非革命者，不然全员革命后没有目标，只会闲逛
            int total = map != null && map.mapPawns != null ? map.mapPawns.FreeColonists.Count : 0;
            if (fast && total > 0 && unrest.Count >= Mathf.Max(1, total / 2)) return;

            int cap = Mathf.Max(1, total / 2);
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                if (fast && unrest.Count >= cap) break;      // 每次传染后都重新检查上限
                Pawn t = colonists[i];
                if (t == null || t == source || t.Dead || t.Downed) continue;
                if (fast && TaxPopulation.PawnPrivateWealth(t) > 0f) continue;   // 留富人当目标
                float maxDist = fast ? 60f : 20f;
                if (t.Position.DistanceTo(source.Position) > maxDist) continue;
                if (t.InMentalState || IsUnrestPawn(t) || IsFleeing(t)) continue;
                if (IsAnyUnrestTarget(t)) continue;   // 保留一个可攻击目标，避免传播把目标也变成自己人
                if (!fast && EconomyLedger.GetSatisfaction(t) > StrikeThreshold + 10f) continue;

                // 军人：几乎免疫传染；满意度掉到 15 以下时免疫指数衰减（可能变负数 → 更容易被传染）
                float ch = fast ? 0.9f : chance;
                if (EconomyMilitary.IsSoldier(t))
                {
                    ch *= EconomyMilitary.ContagionMultiplier(t);
                    if (ch <= 0f) continue;
                }
                if (!Rand.Chance(ch)) continue;

                DevLog.Msg("[SPREAD] " + source.LabelShort + " -> " + t.LabelShort
                    + " sat=" + EconomyLedger.GetSatisfaction(t).ToString("0")
                    + " dist=" + t.Position.DistanceTo(source.Position).ToString("0"));

                if (revolution) StartBerserk(t, true);
                else StartStrike(t, 120000);
            }
        }
    }
}

