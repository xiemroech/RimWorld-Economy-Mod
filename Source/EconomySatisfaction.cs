using System;
using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 工作满意度（E 节）。
    ///
    /// 模型改成和原版心情一样是"即时结算"：当前值 = 50（基准）+ 当前各项之和，
    /// 不再每天往存量里累加。好处是提示里的算式能真的对得上，
    /// 玩家加一条加薪事件、清掉一条欠薪，进度条当场就动。
    /// </summary>
    public static class EconomySatisfaction
    {
        // 一个"体面生活"的每日收入基准（白银）
        public const float LivingCostPerDay = 30f;

        // 所有人的基准满意度
        public const float Baseline = 50f;

        // 每天正常工作多少小时；超过就扣满意度（劳动时间惩罚）
        public const float StandardHoursPerDay = 8f;
        public const float OvertimePenaltyPerHour = 0.75f;
        public const float MaxOvertimePenalty = 6f;

        // 计划经济的工资占国民收入比例大致在 55%~65%，取 60% 作为「应有线」：
        // 低于它算抽成（扣分），高于它算超额分配（加分）。
        public const float WageShareReference = 0.5f;

        // 加班费倍率每高出 1.0 一倍，换多少满意度
        public const float OvertimeSatisfactionPerMultiplier = 4f;

        // 休息低于这个比例才开始掉效率
        public const float RestDecayThreshold = 0.4f;

        // 掉到阈值以下后：休息每低 1 个点乘 exp(-2.0 × 差值)；工时只做很小的追加惩罚
        public const float HoursMinorWeight = 0.05f;
        public const float RestDepthDecay = 2f;
        public const float FatigueMinFactor = 0.15f;

        /// <summary>把某人的满意度重新按当前条件算一遍并写回账本。</summary>
        public static void Recompute(Pawn p)
        {
            if (p == null) return;
            float target = Target(p, null);
            EconomyLedger.AddSatisfaction(p, target - EconomyLedger.GetSatisfaction(p));
        }

        /// <summary>多人宿舍的满意度惩罚（每天）。</summary>
        public const float BunkPenalty = -2f;
        /// <summary>没有床、直接睡在地上的满意度惩罚（每天）。</summary>
        public const float GroundSleepPenalty = -4f;
        // 健康：营养不良 / 极度疲劳 / 伤痛
        public const float MalnutritionLight = -3f;
        public const float MalnutritionMid = -6f;
        public const float MalnutritionHeavy = -12f;
        public const float ExhaustionPenalty = -8f;
        public const float InjuryPenaltyPer = -2f;
        public const float MaxInjuryPenalty = -10f;
        public const float PainPenaltyPerPct = -0.12f;
        public const float MaxPainPenalty = -6f;

        // 谁最近在集体宿舍（RoomRoleDefOf.Barracks）睡过 —— 原版就是按房间角色判定的
        private static readonly Dictionary<Pawn, int> lastBarracksTick = new Dictionary<Pawn, int>();
        // 谁最近没有床、直接睡在地上（含草地/泥地/普通地板）
        private static readonly Dictionary<Pawn, int> lastGroundSleepTick = new Dictionary<Pawn, int>();

        public static void ClearBarracksRuntime()
        {
            lastBarracksTick.Clear();
            lastGroundSleepTick.Clear();
        }

        /// <summary>每 250 tick 由地图组件调用：在宿舍床上记一笔；没床却躺着也算睡地。</summary>
        public static void NoticeSleep(Pawn p)
        {
            if (p == null || !p.Spawned) return;
            int now = Find.TickManager.TicksGame;

            if (!p.InBed())
            {
                // 没床还在"躺下"状态，就是露宿（草地、泥地、普通地板都算）
                if (p.CurJobDef == JobDefOf.LayDown && p.jobs != null && p.jobs.curDriver is JobDriver_LayDown)
                {
                    lastGroundSleepTick[p] = now;
                }
                return;
            }

            Building_Bed bed = p.CurrentBed();
            if (bed == null) return;
            Room room = bed.GetRoom();
            if (room == null) return;
            if (room.Role == RoomRoleDefOf.Barracks)
            {
                lastBarracksTick[p] = now;
            }
        }

        public static bool SleptOnGroundRecently(Pawn p)
        {
            if (p == null) return false;
            int t;
            if (!lastGroundSleepTick.TryGetValue(p, out t)) return false;
            return Find.TickManager.TicksGame - t < GenDate.TicksPerDay;
        }

        public static bool SleptInBarracksRecently(Pawn p)
        {
            if (p == null) return false;
            int t;
            if (!lastBarracksTick.TryGetValue(p, out t)) return false;
            return Find.TickManager.TicksGame - t < GenDate.TicksPerDay;
        }

        /// <summary>
        /// 目标满意度 = 50 + 各项之和。entries 不为空时顺便把明细填进去（给提示用）。
        /// </summary>
        public static float Target(Pawn p, List<(string label, float value)> entries)
        {
            if (p == null) return Baseline;
            float total = 0f;

            // 1. 工资的三笔扣减分开算，各自惩罚、可以叠加：
            //    ① 工资比例抽走的剩余价值（制度性，越大越不满）
            //    ② 日薪上限截留（相对剩余）
            //    ③ 应发但国库没钱没发出去
            if (WagesEnabled(p.Map))
            {
                float shareWage = EconomyLedger.GetLastExpected(p);
                float shareCut = EconomyLedger.GetShareCut(p);
                float capCut = EconomyLedger.GetCapCut(p);
                float unpaid = EconomyLedger.GetUnpaidCut(p);
                float produced = shareWage + shareCut;

                // 收入太小（不到 1 银）时不显示也不扣分，否则会出现"抽走 40%，0 银"这种自相矛盾的行
                if (produced >= 1f)
                {
                    // ① 工资占比：以计划经济时期的工资占比 WageShareReference 为"应有线"
                    //   低于它 = 抽成（扣分，扣满 −8）；高于它 = 预期之外的分配（加分，最多 +4）
                    float payRatio = Mathf.Clamp01((produced - shareCut) / produced);
                    if (payRatio < WageShareReference - 0.001f)
                    {
                        float shortage = (WageShareReference - payRatio) / WageShareReference;
                        float pen = -Mathf.Round(shortage * 8f);
                        if (pen < 0f)
                        {
                            total += pen;
                            Add(entries, "剩余价值抽成（工资占比 " + (payRatio * 100f).ToString("0")
                                + "%，低于应有线 " + (WageShareReference * 100f).ToString("0") + "%）", pen);
                        }
                    }
                    else if (payRatio > WageShareReference + 0.001f)
                    {
                        float extra = (payRatio - WageShareReference) / Mathf.Max(0.01f, 1f - WageShareReference);
                        float bonus = Mathf.Min(8f, Mathf.Round(extra * 8f));
                        if (bonus > 0f)
                        {
                            total += bonus;
                            Add(entries, "超额分配（工资占比 " + (payRatio * 100f).ToString("0")
                                + "%，高于应有线 " + (WageShareReference * 100f).ToString("0") + "%）", bonus);
                        }
                    }

                    // ② 日薪上限截留：按"该给他的比例工资"里被截掉的比例算，最多 −6
                    if (shareWage > 0.01f && capCut > 0.01f)
                    {
                        float capRatio = Mathf.Clamp01(capCut / shareWage);
                        float pen = -capRatio * 6f;
                        total += pen;
                        Add(entries, "日薪上限截留（截掉 " + (capRatio * 100f).ToString("0") + "%，"
                            + capCut.ToString("0.#") + " 银）", pen);
                    }

                    // ③ 该发没发（国库空）
                    float payable = Mathf.Max(0f, shareWage - capCut);
                    if (payable > 0.01f && unpaid > 0.01f)
                    {
                        float unpaidRatio = Mathf.Clamp01(unpaid / payable);
                        float pen = -unpaidRatio * 6f;
                        total += pen;
                        Add(entries, "工资欠发（欠 " + (unpaidRatio * 100f).ToString("0") + "%，"
                            + unpaid.ToString("0.#") + " 银）", pen);
                    }
                }
            }

            // 2. 工作条件：正在工作时，室内 +0.5、露天 −0.5
            WorkTypeDef wt = EconomyWages.WorkTypeOf(p);
            if (wt != null)
            {
                bool roofed = p.Spawned && p.Position.Roofed(p.Map);
                total += roofed ? 0.5f : -0.5f;
                Add(entries, roofed ? "工作条件（室内）" : "工作条件（露天）", roofed ? 0.5f : -0.5f);
            }

            // 2.5 多人宿舍：原版心情惩罚已撤销，改记在工作满意度上
            if (SleptInBarracksRecently(p))
            {
                Add(entries, "多人宿舍（昨夜睡在集体宿舍）", BunkPenalty);
                total += BunkPenalty;
            }

            // 2.51 露宿：没床直接睡在地上（草地/泥地/地板），比集体宿舍更难受
            if (SleptOnGroundRecently(p))
            {
                Add(entries, "露宿（昨夜没有床，直接睡在地上）", GroundSleepPenalty);
                total += GroundSleepPenalty;
            }

            // 2.52 贫富差距：最富的人是你 X 倍以上时扣分；越接近公有制越容易触发
            if (EconomyWealthGap.Enabled(p))
            {
                float ratio = EconomyWealthGap.RatioOf(p);
                float threshold = EconomyWealthGap.ThresholdOf(p);
                if (ratio > threshold)
                {
                    float pen = -Mathf.Min(12f, (ratio / Mathf.Max(1f, threshold) - 1f) * 4f);
                    Add(entries, "贫富差距（最富是你的 " + ratio.ToString("0.0") + " 倍，阈值 " + threshold.ToString("0.0")
                + " 倍，财富基尼 " + EconomyWealthGap.Gini.ToString("0.00") + "）", pen);
                    total += pen;
                }
            }

            // 2.55 欠薪：每多欠一次，惩罚按指数放大（第1次 -2，第2次 -4，第3次 -8 … 封顶 -40）
            MapEconomyComponent ac = (p.MapHeld ?? p.Map)?.GetComponent<MapEconomyComponent>();
            int arrears = ac != null ? ac.arrearsCount : 0;
            if (arrears > 0)
            {
                float pen = -Mathf.Min(40f, 2f * Mathf.Pow(1.9f, arrears - 1));
                Add(entries, "欠薪（累计 " + arrears + " 次）", pen);
                total += pen;
            }

            // 2.6 健康：营养不良 / 极度疲劳 / 伤痛
            float mal = MalnutritionSeverity(p);
            if (mal > 0f)
            {
                float pen = mal >= 0.5f ? MalnutritionHeavy : (mal >= 0.25f ? MalnutritionMid : MalnutritionLight);
                string tier = mal >= 0.5f ? "重度" : (mal >= 0.25f ? "中度" : "轻度");
                Add(entries, "营养不良（" + tier + "，" + (mal * 100f).ToString("0") + "%）", pen);
                total += pen;
            }
            if (HasExhaustion(p))
            {
                Add(entries, "极度疲劳（力竭）", ExhaustionPenalty);
                total += ExhaustionPenalty;
            }
            int injuries = InjuryCount(p);
            if (injuries > 0)
            {
                float pen = Mathf.Max(MaxInjuryPenalty, InjuryPenaltyPer * injuries);
                Add(entries, "伤痛（" + injuries + " 处伤）", pen);
                total += pen;
            }
            float pain = p.health != null ? p.health.hediffSet.PainTotal : 0f;
            if (pain > 0.15f)
            {
                float pen = Mathf.Max(MaxPainPenalty, PainPenaltyPerPct * pain * 100f);
                Add(entries, "疼痛（" + (pain * 100f).ToString("0") + "%）", pen);
                total += pen;
            }

            // 2.7 正向渠道（工资足额 / 居住环境 / 工作间 / 私有财产 / 心情·精力·娱乐）
            Map mapPos = p.MapHeld ?? p.Map;
            MapEconomyComponent mpos = mapPos != null ? mapPos.GetComponent<MapEconomyComponent>() : null;

            // ① 工资足额发放：实发 / 该发（比例工资）≥ 98% 就算"没被扣"；超过 100%（加班费补上）再加
            if (WagesEnabled(mapPos))
            {
                float expected = EconomyLedger.GetLastExpected(p);
                if (expected >= 1f)
                {
                    float paid = Mathf.Max(0f, expected - EconomyLedger.GetCapCut(p) - EconomyLedger.GetUnpaidCut(p));
                    float ratio = paid / expected;
                    if (ratio >= 0.98f)
                    {
                        float bonus = ratio > 1.001f
                            ? Mathf.Min(6f, 3f + Mathf.Round((ratio - 1f) * 10f))
                            : 3f;
                        Add(entries, "工资足额（实发 " + paid.ToString("0") + " / 该发 "
                            + expected.ToString("0") + "）", bonus);
                        total += bonus;
                    }
                }
            }

            // ② 居住环境等级（原版房间观感）：按自己床铺所在房间算，没床就用当前房间
            if (p.ownership != null && p.ownership.OwnedBed != null)
            {
                Room bedRoom = p.ownership.OwnedBed.GetRoom();
                float imp = RoomImpressiveness(bedRoom);
                float bonus = ImpressivenessBonus(imp, 4f);
                if (bonus > 0.01f)
                {
                    Add(entries, "居住环境（观感 " + imp.ToString("0") + "）", bonus);
                    total += bonus;
                }
            }

            // ③ 工作间等级：正在工作时，按当前所在房间的观感加
            if (wt != null && p.Spawned)
            {
                float imp = RoomImpressiveness(p.GetRoom());
                float bonus = ImpressivenessBonus(imp, 3f);
                if (bonus > 0.01f)
                {
                    Add(entries, "工作间等级（观感 " + imp.ToString("0") + "）", bonus);
                    total += bonus;
                }
            }

            // ④ 私有财产：有一定私产说明"过日子有奔头"
            if (mapPos != null)
            {
                float wealth = TaxPopulation.PawnPrivateWealth(p, EconomyBank.PrivateSilver(mapPos, p));
                float bonus = wealth >= 10000f ? 4f : (wealth >= 4000f ? 3f : (wealth >= 1500f ? 2f : (wealth >= 500f ? 1f : 0f)));
                if (bonus > 0.01f)
                {
                    Add(entries, "私有财产（" + wealth.ToString("0") + " 银）", bonus);
                    total += bonus;
                }
            }

            // ⑤ 心情 / 精力 / 娱乐 都在 80% 以上，各 +1
            if (p.needs != null)
            {
                if (p.needs.mood != null && p.needs.mood.CurLevelPercentage > 0.8f)
                {
                    Add(entries, "心情舒畅（心情 > 80%）", 2f);
                    total += 2f;
                }
                if (p.needs.rest != null && p.needs.rest.CurLevelPercentage > 0.8f)
                {
                    Add(entries, "精力充足（睡眠 > 80%）", 2f);
                    total += 2f;
                }
                if (p.needs.joy != null && p.needs.joy.CurLevelPercentage > 0.8f)
                {
                    Add(entries, "娱乐满足（娱乐 > 80%）", 2f);
                    total += 2f;
                }
            }

            // 2.75 长期工作保证：连续按时发薪的期数越多，越"有保障"
            if (mpos != null && WagesEnabled(mapPos) && mpos.consecutiveOnTimePays > 0)
            {
                int n = mpos.consecutiveOnTimePays;
                float bonus = n >= 30 ? 4f : (n >= 20 ? 3f : (n >= 10 ? 2f : (n >= 3 ? 1f : 0f)));
                if (bonus > 0f)
                {
                    Add(entries, "长期工作保证（连续按时发薪 " + n + " 期）", bonus);
                    total += bonus;
                }
            }

            // 2.76 医疗保障：免费医疗给满额；收费医疗看"付得起几次"
            if (mpos != null && mpos.medical != null)
            {
                int fee = mpos.medical.enabled ? Mathf.Max(0, Mathf.RoundToInt(mpos.medical.baseFee)) : 0;
                if (fee <= 0)
                {
                    Add(entries, "医疗保障（医疗免费）", 2f);
                    total += 2f;
                }
                else
                {
                    int have = EconomyBank.PrivateSilver(mapPos, p);
                    if (have >= fee * 3) { Add(entries, "医疗保障（付得起多次诊疗，每次 " + fee + " 银）", 2f); total += 2f; }
                    else if (have >= fee) { Add(entries, "医疗保障（付得起一次诊疗，" + fee + " 银）", 1f); total += 1f; }
                }
            }

            // 2.77 工作与兴趣匹配：当前工作类型对应的技能越高越满意
            if (wt != null)
            {
                SkillDef sk = SkillForWorkType(wt);
                if (sk != null && p.skills != null)
                {
                    int lv = p.skills.GetSkill(sk).Level;
                    if (lv >= 12) { Add(entries, "工作与兴趣匹配（" + wt.label + " " + lv + " 级）", 2f); total += 2f; }
                    else if (lv >= 8) { Add(entries, "工作与兴趣匹配（" + wt.label + " " + lv + " 级）", 1f); total += 1f; }
                }
            }

            // 2.78 亲友同在：配偶在同一张图 +2，其它高好感关系 +1
            if (p.relations != null && p.Spawned)
            {
                Pawn spouse = p.relations.GetFirstDirectRelationPawn(PawnRelationDefOf.Spouse);
                if (spouse != null && spouse.Spawned && spouse.Map == p.Map)
                {
                    Add(entries, "配偶在身边", 2f);
                    total += 2f;
                }
                else
                {
                    foreach (Pawn other in p.relations.PotentiallyRelatedPawns)
                    {
                        if (other == null || !other.Spawned || other.Map != p.Map) continue;
                        if (p.relations.OpinionOf(other) < 40) continue;
                        Add(entries, "亲友在身边（" + other.LabelShort + "）", 1f);
                        total += 1f;
                        break;
                    }
                }
            }

            // 2.78 税收负担：按"这个人所在档位"的实际税率算，累进制下富人税高、感受更差
            if (mpos != null && mpos.incomeTax != null && !mpos.incomeTax.IsZero)
            {
                WealthTier taxTier = WealthTier.Mid;
                var tiers = TaxPopulation.RankTiers(mapPos);
                if (tiers == null || !tiers.TryGetValue(p, out taxTier)) taxTier = WealthTier.Mid;

                float effRate = mpos.incomeTax.RateFor(taxTier);
                float taxVal;
                if (effRate >= 0f) taxVal = -Mathf.Min(7f, Mathf.Round(effRate * 0.3f));
                else taxVal = Mathf.Min(3f, Mathf.Round(-effRate * 0.3f));

                if (Mathf.Abs(taxVal) >= 0.5f)
                {
                    string modeName = mpos.incomeTax.mode == TaxMode.Progressive ? "累进"
                        : mpos.incomeTax.mode == TaxMode.Regressive ? "累退" : "比例";
                    string tierName = taxTier == WealthTier.Low ? "低收入" : taxTier == WealthTier.High ? "高收入" : "中等";
                    string head = effRate >= 0f ? "税收负担" : "税收补贴";
                    Add(entries, head + "（" + tierName + "档 " + effRate.ToString("0") + "%，" + modeName + "税）", taxVal);
                    total += taxVal;
                }
            }

            // 2.79 公有制福利：食物/医药/成瘾品/植入物的社会化越高，"从摇篮到坟墓"的保障感越强
            if (mpos != null)
            {
                // 只有"最近没买成"的次数很少，说明长期目标确实被满足，福利才算数
                bool targetsMet = EconomyConsumption.UnmetCount(p) <= 3;
                float welfare = BasicWelfare(mpos);

                // 公共福利只从 75% 起算：75% 以下没有"福利感"，100% 才有满额
                int bonus = welfare >= 99.5f ? 0 : Mathf.RoundToInt(Mathf.Clamp01((welfare - 75f) / 25f) * 10f);
                if (bonus > 0 && targetsMet)
                {
                    Add(entries, "公共福利（基础消费品社会化 " + welfare.ToString("0") + "%）", bonus);
                    total += bonus;
                }
                else if (bonus > 0)
                {
                    Add(entries, "公共福利受阻（社会化 " + welfare.ToString("0") + "%，但最近有 "
                        + EconomyConsumption.UnmetCount(p) + " 次想买没买到）", 0f);
                }

                // 工资制度整体关闭时，等于"集体直接供给"，但同样要看需求是否真的被满足
                if (!WagesEnabled(mapPos) && targetsMet)
                {
                    Add(entries, "集体供给（工资制度关闭）", 8f);
                    total += 8f;
                }
            }

            // 2.8 殖民地治理水平：储备 / 温度 / 教育 / 食堂 / 衣着 / 认可 / 信仰
            if (mapPos != null)
            {
                // ① 食物储备：按全员能吃几天算
                try
                {
                    float nutrition = mapPos.resourceCounter.TotalHumanEdibleNutrition;
                    float perDay = Mathf.Max(1f, mapPos.mapPawns.FreeColonistsCount * 1.6f);
                    float days = nutrition / perDay;
                    if (days >= 10f) { Add(entries, "食物储备充足（约 " + days.ToString("0") + " 天）", 2f); total += 2f; }
                    else if (days >= 5f) { Add(entries, "食物储备尚可（约 " + days.ToString("0") + " 天）", 1f); total += 1f; }
                }
                catch { }

                // ② 舒适温度
                if (p.Spawned)
                {
                    float temp = p.AmbientTemperature;
                    if (temp >= 16f && temp <= 26f)
                    {
                        Add(entries, "温度舒适（" + temp.ToString("0") + "℃）", 1f);
                        total += 1f;
                    }
                }

                // ③ 免费教育（有孩子且在读，学费关闭）
                if (mpos.education != null && !mpos.education.enabled)
                {
                    bool hasChild = false;
                    List<Pawn> cols = mapPos.mapPawns.FreeColonists;
                    for (int i = 0; i < cols.Count; i++)
                    {
                        if (cols[i].ageTracker != null && cols[i].ageTracker.AgeBiologicalYears < 13) { hasChild = true; break; }
                    }
                    if (hasChild)
                    {
                        Add(entries, "免费教育（殖民地有儿童）", 1f);
                        total += 1f;
                    }
                }

                // ④ 公共食堂：正在吃饭且坐在餐厅里
                if (p.CurJobDef == JobDefOf.Ingest && p.GetRoom() != null && p.GetRoom().Role != null && p.GetRoom().Role.defName == "DiningRoom")
                {
                    Add(entries, "公共食堂（在餐厅用餐）", 1f);
                    total += 1f;
                }

                // ⑤ 服装整洁：全身耐久都在 70% 以上
                if (p.apparel != null && p.apparel.WornApparelCount > 0)
                {
                    bool tidy = true;
                    List<Apparel> worn = p.apparel.WornApparel;
                    for (int i = 0; i < worn.Count; i++)
                    {
                        if (worn[i].MaxHitPoints > 0 && (float)worn[i].HitPoints / worn[i].MaxHitPoints < 0.7f) { tidy = false; break; }
                    }
                    if (tidy)
                    {
                        Add(entries, "服装整洁", 1f);
                        total += 1f;
                    }
                }

                // ⑥ 工作被认可：本期劳动量不低于殖民地平均
                List<Pawn> all = mapPos.mapPawns.FreeColonists;
                if (all.Count > 1 && MapEconomyComponent.LaborOnMap(p) > 0.01f)
                {
                    float sum = 0f;
                    for (int i = 0; i < all.Count; i++) sum += MapEconomyComponent.LaborOnMap(all[i]);
                    float avg = sum / all.Count;
                    if (avg > 0.01f && MapEconomyComponent.LaborOnMap(p) >= avg)
                    {
                        Add(entries, "工作被认可（劳动量高于平均）", 1f);
                        total += 1f;
                    }
                }

                // ⑦ 信仰一致：同信仰人数占比越高越满意（+1 ~ +4）
                if (p.Ideo != null && all.Count > 0)
                {
                    int same = 0;
                    for (int i = 0; i < all.Count; i++) if (all[i].Ideo == p.Ideo) same++;
                    float ratio = (float)same / all.Count;
                    int bonus = Mathf.Clamp(Mathf.RoundToInt(ratio * 4f), 1, 4);
                    if (ratio >= 0.25f)
                    {
                        Add(entries, "信仰一致（同信仰 " + same + "/" + all.Count + "）", bonus);
                        total += bonus;
                    }
                }
            }

            // 2.84 较为平均：基尼 0.80 → 0，0.20 → +4（两套制度都能拿）
            float giniAll = EconomyWealthGap.Gini;
            if (giniAll < 0.8f)
            {
                int evenBonus = Mathf.Clamp(Mathf.RoundToInt((0.8f - giniAll) / 0.6f * 4f), 0, 4);
                if (evenBonus > 0)
                {
                    Add(entries, "较为平均（财富基尼 " + giniAll.ToString("0.00") + "）", evenBonus);
                    total += evenBonus;
                }
            }

            // 2.85 公有制的"大后期"专属：按需分配 / 无阶级差别 / 劳动光荣
            if (mpos != null && EconomyDoctrine.IsPublic(mapPos))
            {
                bool met = EconomyConsumption.UnmetCount(p) <= 3;
                float welfare = BasicWelfare(mpos);

                // ①.4 人人有饭吃：食物 100% 社会化，且全殖民地没人营养不良（按原版 Malnutrition 判定）
                EconomyCategoryDef foodCat = DefDatabase<EconomyCategoryDef>.GetNamedSilentFail("DshEco_Food");
                if (foodCat != null && mpos.GetPublicization(foodCat) >= 99.5f)
                {
                    bool starving = false;
                    List<Pawn> folks = mapPos.mapPawns.FreeColonists;
                    for (int i = 0; i < folks.Count; i++)
                    {
                        if (MalnutritionSeverity(folks[i]) > 0f) { starving = true; break; }
                    }
                    if (!starving)
                    {
                        Add(entries, "人人有饭吃（食物全社会化，无人营养不良）", 3f);
                        total += 3f;
                    }
                }

                // ① 按需分配：四类基础消费品全部 100% 社会化，且确实买得到
                if (welfare >= 99.5f && met)
                {
                    Add(entries, "按需分配（基础消费品全部社会化）", 10f);
                    total += 10f;
                }

                // ② 无阶级差别：财富基尼很低（大家都差不多）
                if (EconomyWealthGap.Gini < 0.2f)
                {
                    Add(entries, "无阶级差别（财富基尼 " + EconomyWealthGap.Gini.ToString("0.00") + "）", 5f);
                    total += 5f;
                }

                // ③ 劳动光荣：殖民地九成以上的人这一期都在干活
                List<Pawn> crew = mapPos.mapPawns.FreeColonists;
                if (crew.Count > 1)
                {
                    int working = 0;
                    for (int i = 0; i < crew.Count; i++) if (MapEconomyComponent.LaborOnMap(crew[i]) > 0.01f) working++;
                    if (working >= Mathf.CeilToInt(crew.Count * 0.9f) && MapEconomyComponent.LaborOnMap(p) > 0.01f)
                    {
                        Add(entries, "劳动光荣（" + working + "/" + crew.Count + " 人在劳动）", 3f);
                        total += 3f;
                    }
                }
            }

            // 3. 劳动时间惩罚：今天干超过 8 小时，每多 1 小时 −0.75，最多 −6
            Map mapHeld = p.MapHeld ?? p.Map;
            MapEconomyComponent me = mapHeld != null ? mapHeld.GetComponent<MapEconomyComponent>() : null;
            float hours = me != null ? me.DailyWorkHours(p) : 0f;
            if (hours > StandardHoursPerDay)
            {
                float over = hours - StandardHoursPerDay;
                float pen = -Mathf.Min(MaxOvertimePenalty, over * OvertimePenaltyPerHour);
                total += pen;
                Add(entries, "劳动时长（今日 " + hours.ToString("0.0") + " 小时，超出 "
                    + StandardHoursPerDay.ToString("0") + " 小时）", pen);

                // 加班费：倍率越高，这部分劳动越"值得"，按倍率给满意度
                if (WagesEnabled(mapHeld))
                {
                    WagePolicy wp = mapHeld.GetComponent<MapEconomyComponent>().wages;
                    if (wp != null && wp.OvertimeOnFor(p))
                    {
                        float mult = Mathf.Max(1f, wp.OvertimeMultFor(p));
                        float bonus = (mult - 1f) * OvertimeSatisfactionPerMultiplier;
                        if (bonus > 0.001f)
                        {
                            total += bonus;
                            Add(entries, "加班费（×" + mult.ToString("0.0") + "，超出 "
                                + StandardHoursPerDay.ToString("0") + " 小时按此计薪）", bonus);
                        }
                    }
                }
            }

            // 3.5 短工时加成：今天干活但不满 8 小时，越少越"清闲"，最多 +6
            if (hours > 0.01f && hours < StandardHoursPerDay && MapEconomyComponent.LaborOnMap(p) > 0.01f)
            {
                float idle = (StandardHoursPerDay - hours) / StandardHoursPerDay;
                float bonus = Mathf.Min(6f, Mathf.Round(idle * 6f));
                if (bonus > 0f)
                {
                    Add(entries, "工时较短（今日 " + hours.ToString("0.0") + " 小时，低于 "
                        + StandardHoursPerDay.ToString("0") + " 小时）", bonus);
                    total += bonus;
                }
            }

            // 4. 事件（同名合并成 ×N）
            List<SatisfactionEvent> events = EconomySatisfactionEvents.For(p);
            var order = new List<string>();
            var counts = new Dictionary<string, int>();
            var sums = new Dictionary<string, float>();
            for (int i = 0; i < events.Count; i++)
            {
                string label = events[i].label;
                if (!counts.ContainsKey(label))
                {
                    order.Add(label);
                    counts[label] = 0;
                    sums[label] = 0f;
                }
                counts[label]++;
                sums[label] += events[i].daily;
            }
            for (int i = 0; i < order.Count; i++)
            {
                string label = order[i];
                int n = counts[label];
                // 奖金是系统给的效果，不加「事件·」前缀，在明细里单独一行更显眼
                string text = label.StartsWith("奖金·") ? label : "事件·" + label;
                if (n > 1) text += " ×" + n + "（每次 " + (sums[label] / n).ToString("+0.0;-0.0") + "）";
                total += sums[label];
                Add(entries, text, sums[label]);
            }

            return Mathf.Clamp(Baseline + total, 0f, 100f);
        }

        /// <summary>基础消费品（食物/医药/成瘾品/植入物）的平均社会化程度。</summary>
        public static float BasicWelfare(MapEconomyComponent comp)
        {
            if (comp == null) return 0f;
            string[] names = { "DshEco_Food", "DshEco_Medicine", "DshEco_Drugs", "DshEco_Augment" };
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < names.Length; i++)
            {
                EconomyCategoryDef cat = DefDatabase<EconomyCategoryDef>.GetNamedSilentFail(names[i]);
                if (cat == null) continue;
                sum += comp.GetPublicization(cat);
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>工作类型 → 对应技能（没有对应技能的返回 null）。</summary>
        public static SkillDef SkillForWorkType(WorkTypeDef wt)
        {
            if (wt == null) return null;
            switch (wt.defName)
            {
                case "Mining": return SkillDefOf.Mining;
                case "Construction": return SkillDefOf.Construction;
                case "Cooking": return SkillDefOf.Cooking;
                case "Plants": case "Growing": return SkillDefOf.Plants;
                case "Crafting": case "Smithing": case "Tailoring": return SkillDefOf.Crafting;
                case "Art": return SkillDefOf.Artistic;
                case "Research": return SkillDefOf.Intellectual;
                case "Doctor": return SkillDefOf.Medicine;
                case "Handling": case "Animals": return SkillDefOf.Animals;
                default: return null;
            }
        }

        /// <summary>房间观感数值；没有房间算 0。</summary>
        public static float RoomImpressiveness(Room room)
        {
            if (room == null || room.UsesOutdoorTemperature && room.CellCount == 0) return 0f;
            try { return room.GetStat(RoomStatDefOf.Impressiveness); }
            catch { return 0f; }
        }

        /// <summary>观感 → 满意度：50 起 0.5，80 起 1.0，120 起封顶值。</summary>
        public static float ImpressivenessBonus(float impressiveness, float max)
        {
            if (impressiveness >= 120f) return max;
            if (impressiveness >= 80f) return Mathf.Round(max * 0.75f);
            if (impressiveness >= 50f) return Mathf.Round(max * 0.5f);
            if (impressiveness >= 30f) return Mathf.Round(max * 0.25f);
            return 0f;
        }

        private static void Add(List<(string label, float value)> entries, string label, float value)
        {
            if (entries != null) entries.Add((label, value));
        }

        /// <summary>每天把这些人的满意度按当前条件重算一次（事件到期也在这里清理）。</summary>
        public static void DailyUpdate(Map map)
        {
            if (map == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead) continue;

                // 原版「睡在多人宿舍」心情惩罚撤销：改为工作满意度惩罚（见 BunkBedCount）
                try
                {
                    if (p.needs != null && p.needs.mood != null)
                        p.needs.mood.thoughts.memories.RemoveMemoriesOfDef(ThoughtDefOf.SleptInBarracks);
                }
                catch { }

                float before = EconomyLedger.GetSatisfaction(p);
                float target = Target(p, null);
                EconomyLedger.AddSatisfaction(p, target - before);
                DevLog.Msg("[SAT] 重算 " + p.LabelShort + " " + before.ToString("0.0") + " → "
                    + EconomyLedger.GetSatisfaction(p).ToString("0.0") + "（当前条件目标 " + target.ToString("0.0") + "）");
            }
            EconomySatisfactionEvents.Tick();
        }

        /// <summary>发薪后立刻把所有人的满意度按新数据重算，避免提示里算式变了、进度条还是旧值。</summary>
        public static void RecomputeAll(Map map)
        {
            if (map == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead) continue;
                EconomyLedger.AddSatisfaction(p, Target(p, null) - EconomyLedger.GetSatisfaction(p));
            }
        }

        /// <summary>工资系统是否启用（关掉时不计报酬公平）。</summary>
        public static bool WagesEnabled(Map map)
        {
            if (map == null) return false;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            return comp != null && comp.wages != null && comp.wages.enabled;
        }

        /// <summary>满意度对工作速度的乘数（0.5 ~ 1.5）。</summary>
        public static float WorkSpeedFactor(Pawn p)
        {
            if (p == null) return 1f;
            float s = EconomyLedger.GetSatisfaction(p);
            return Mathf.Lerp(0.5f, 1.5f, Mathf.Clamp01(s / 100f));
        }

        /// <summary>营养不良严重度（0 = 没有）。</summary>
        public static float MalnutritionSeverity(Pawn p)
        {
            if (p == null || p.health == null) return 0f;
            List<Hediff> hs = p.health.hediffSet.hediffs;
            for (int i = 0; i < hs.Count; i++)
            {
                Hediff h = hs[i];
                if (h == null || h.def == null) continue;
                if (h.def.defName == "Malnutrition") return Mathf.Clamp01(h.Severity);
            }
            return 0f;
        }

        /// <summary>是否处于极度疲劳（力竭）状态。</summary>
        public static bool HasExhaustion(Pawn p)
        {
            if (p == null) return false;
            // 原版没有“Exhaustion”这个 Hediff：以休息需求为准（<10% 视为极度疲劳）
            try
            {
                if (p.needs != null && p.needs.rest != null && p.needs.rest.CurLevelPercentage < 0.1f) return true;
            }
            catch { }
            if (p.health == null) return false;
            List<Hediff> hs = p.health.hediffSet.hediffs;
            for (int i2 = 0; i2 < hs.Count; i2++)
            {
                Hediff h = hs[i2];
                if (h == null || h.def == null) continue;
                string dn = h.def.defName;
                if (dn == "Exhaustion" || dn == "ExtremeFatigue" || dn == "Fatigue") return true;
            }
            return false;
        }

        /// <summary>未愈合的伤势数量（不含永久损毁）。</summary>
        public static int InjuryCount(Pawn p)
        {
            if (p == null || p.health == null) return 0;
            int n = 0;
            List<Hediff> hs = p.health.hediffSet.hediffs;
            for (int i = 0; i < hs.Count; i++)
            {
                Hediff_Injury inj = hs[i] as Hediff_Injury;
                if (inj != null && !inj.IsPermanent()) n++;
            }
            return n;
        }

        /// <summary>健康对劳动效率的乘数：营养不良最高 ×0.70，力竭 ×0.80，伤痛最低 ×0.70。</summary>
        public static float HealthFactor(Pawn p)
        {
            if (p == null) return 1f;
            float f = 1f;
            float mal = MalnutritionSeverity(p);
            if (mal >= 0.5f) f *= 0.70f;
            else if (mal >= 0.25f) f *= 0.85f;
            else if (mal > 0f) f *= 0.95f;
            if (HasExhaustion(p)) f *= 0.80f;
            int injuries = InjuryCount(p);
            if (injuries > 0) f *= Mathf.Clamp(1f - 0.04f * injuries, 0.75f, 1f);
            float pain = p.health != null ? p.health.hediffSet.PainTotal : 0f;
            if (pain > 0.15f) f *= Mathf.Clamp(1f - 0.3f * (pain - 0.15f), 0.75f, 1f);
            return Mathf.Clamp(f, 0.4f, 1f);
        }

        /// <summary>
        /// 劳动效率乘数：主要是"休息"决定的，工时只做很小的追加。
        /// 休息 ≥ 40% → ×1.00；跌破后 f = exp(-2.0 × (40% - 休息) - 0.05 × 超出8小时的部分)。
        /// 例：休息 20%、今天干了 10 小时 → ×0.61；休息 0% → ×0.37。
        /// </summary>
        public static float RestFactor(Pawn p)
        {
            if (p == null || p.needs == null || p.needs.rest == null) return 1f;
            float r = Mathf.Clamp01(p.needs.rest.CurLevel);
            if (r >= RestDecayThreshold) return 1f;

            float hours = 0f;
            Map map = p.MapHeld ?? p.Map;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp != null) hours = comp.DailyWorkHours(p);

            float over = Mathf.Max(0f, hours - StandardHoursPerDay);
            float depth = RestDecayThreshold - r;
            float f = Mathf.Exp(-RestDepthDecay * depth - HoursMinorWeight * over);
            return Mathf.Clamp(f, FatigueMinFactor, 1f);
        }

        /// <summary>
        /// 工作效率总计 = 满意度 × 休息/疲劳 × 奖金排名加成。
        /// 这是这个人最终落到 WorkSpeedGlobal 上的乘数。
        /// </summary>
        public static float TotalEfficiency(Pawn p)
        {
            if (p == null) return 1f;
            float bonus = 1f;
            Map map = p.MapHeld ?? p.Map;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp != null) bonus += comp.GetBonusSpeed(p);
            return WorkSpeedFactor(p) * RestFactor(p) * HealthFactor(p) * bonus;
        }

        /// <summary>工作效率总计的一句话拆解，给提示和界面用。</summary>
        public static string EfficiencyLine(Pawn p)
        {
            if (p == null) return "";
            float sat = WorkSpeedFactor(p);
            float rest = RestFactor(p);
            float bonus = 1f;
            Map map = p.MapHeld ?? p.Map;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp != null) bonus += comp.GetBonusSpeed(p);
            return "工作效率总计 ×" + (sat * rest * bonus).ToString("0.00")
                + "（满意度 ×" + sat.ToString("0.00")
                + " · 休息 ×" + rest.ToString("0.00")
                + " · 奖金排名 ×" + bonus.ToString("0.00") + "）";
        }

        public static string Describe(Pawn p)
        {
            if (p == null) return "";
            float s = EconomyLedger.GetSatisfaction(p);
            return s.ToString("0") + "（" + Feel(s) + "）";
        }

        /// <summary>
        /// 满意度明细（鼠标提示用）：写成一条对得上的算式，
        /// 50（基准）+ 各项 = 当前值，跟原版心情一样一项一项列出来。
        /// </summary>
        public static string Breakdown(Pawn p)
        {
            Recompute(p);
            if (p == null) return "";

            // 用"当前条件算出来的目标值"显示，保证标题和算式永远一致；
            // 账本里的存量值会在日结算/事件变化时被同步成同一个数。
            float cur = Target(p, null);
            var entries = new List<(string label, float value)>();
            Target(p, entries);
            entries.Sort((a, b) => b.value.CompareTo(a.value));

            float total = 0f;
            for (int i = 0; i < entries.Count; i++) total += entries[i].value;

            var sb = new System.Text.StringBuilder();
            sb.AppendLine("工作满意度: " + cur.ToString("0") + " / 100（" + Feel(cur) + "）");
            sb.AppendLine();
            sb.AppendLine("= 50（基准） " + Sign(total) + "（各项） = " + Mathf.Clamp(Baseline + total, 0f, 100f).ToString("0.0"));
            sb.AppendLine();
            if (entries.Count == 0)
            {
                sb.AppendLine("（当前没有任何加减项，就是基准 50）");
            }
            else
            {
                for (int i = 0; i < entries.Count; i++)
                {
                    sb.AppendLine("  " + Colorize(entries[i].value) + "  " + entries[i].label);
                }
                sb.AppendLine("  ———— 各项合计 " + Colorize(total));
            }
            sb.AppendLine();
            float speed = Mathf.Lerp(0.5f, 1.5f, Mathf.Clamp01(cur / 100f));
            sb.AppendLine("效果: 工作速度 ×" + speed.ToString("0.00") + "（0 分半速，100 分快 50%）");

            float rest = RestFactor(p);
            if (rest < 0.999f)
            {
                sb.AppendLine("休息: " + (p.needs != null && p.needs.rest != null
                        ? (p.needs.rest.CurLevel * 100f).ToString("0") + "%" : "-")
                    + " → 劳动效率 ×" + rest.ToString("0.00") + "（跌到 "
                    + (RestDecayThreshold * 100f).ToString("0") + "% 以下开始指数衰减，"
                    + "工时只做很小的追加，睡饱即恢复）");
            }
            sb.AppendLine(EfficiencyLine(p));
            sb.Append("危险线: ≤" + DshEconomy.EconomyUnrest.StrikeThreshold.ToString("0") + " 罢工 / ≤"
                + DshEconomy.EconomyUnrest.RiotThreshold.ToString("0") + " 暴动 / ≤"
                + DshEconomy.EconomyUnrest.RevolutionThreshold.ToString("0") + " 革命（每游戏小时 15%）"
                + (cur <= DshEconomy.EconomyUnrest.StrikeThreshold ? "　← 已在触发线内" : "　← 当前安全"));
            return sb.ToString();
        }

        /// <summary>跟原版心情一样的配色：正数绿、负数红。</summary>
        static string Colorize(float v)
        {
            string text = (v >= 0f ? "+" : "−") + Mathf.Abs(v).ToString("0.0");
            return "<color=#" + (v >= 0f ? "7CC35A" : "D05C5C") + ">" + text + "</color>";
        }

        static string Sign(float v)
        {
            return (v >= 0f ? "+" : "−") + Mathf.Abs(v).ToString("0.0");
        }

        static string Feel(float s)
        {
            return s >= 75f ? "满意" : (s >= 50f ? "一般" : (s >= 25f ? "不满" : "极不满"));
        }
    }
}

