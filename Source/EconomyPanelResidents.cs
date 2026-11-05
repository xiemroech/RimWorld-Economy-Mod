using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 「居民」页：以剩余价值为核心。
    /// 剩余价值拆成两半显示——
    ///   绝对剩余：靠延长劳动时间 / 压低工资比例榨出来的
    ///   相对剩余：靠生产率高于基准得来的
    /// </summary>
    public static class EconomyPanelResidents
    {
        private static TaxPopulation.ColonyStats statsCache;
        private static Map statsMap;
        private static int statsTick = -1;

        private static void SetStepScale(float value)
        {
            if (DshEconomyMod.Settings != null) DshEconomyMod.Settings.stepScale = value;
        }

        /// <summary>可手动输入的数字框：返回夹紧后的值。</summary>
        private static float NumField(Rect rect, float value, float min, float max, string format)
        {
            string text = value.ToString(format);
            string edited = Widgets.TextField(rect, text);
            float parsed;
            if (edited != text && float.TryParse(edited, out parsed))
            {
                value = Mathf.Clamp(parsed, min, max);
            }
            return value;
        }

        /// <summary>悬浮在"劳动"列上时显示：这个人本期每种工作各干了多久、按什么倍率折算。</summary>
        private static string WorkBreakdown(MapEconomyComponent comp, Pawn p, WagePolicy w)
        {
            if (comp == null || p == null || w == null) return "";
            List<KeyValuePair<string, float>> list = comp.WorkHoursOf(p);
            if (list.Count == 0) return "本期还没有分工时记录（发薪后清零）";

            var sb = new System.Text.StringBuilder();
            float weightedDays = 0f;
            for (int i = 0; i < list.Count; i++)
            {
                string defName = list[i].Key;
                WorkTypeDef wt = defName == "Other" ? null : DefDatabase<WorkTypeDef>.GetNamedSilentFail(defName);
                string label = wt != null ? wt.label : "其他";
                float weight = wt != null && DshEconomyMod.Settings != null ? DshEconomyMod.Settings.GetWorkMult(wt) : 1f;
                float days = list[i].Value / GenDate.TicksPerDay;
                weightedDays += days * weight;
                sb.AppendLine(label + "  " + days.ToString("0.00") + " 天 × " + weight.ToString("0.0")
                    + " = " + (days * weight).ToString("0.00"));
            }
            sb.AppendLine("————");
            sb.AppendLine("有效工时 " + weightedDays.ToString("0.00") + " 天");
            sb.AppendLine("期望工资 " + w.UncappedWage(weightedDays * GenDate.TicksPerDay).ToString("0.0") + " 银");
            return sb.ToString();
        }

        public static void Draw(Rect inRect, MapEconomyComponent comp, Map map, ref Vector2 scroll)
        {            if (comp == null)
            {
                Widgets.Label(inRect, "需要在地图中使用。");
                return;
            }

            WagePolicy w = comp.wages;
            BonusPolicy b = comp.bonus;

            int colonistCount = map.mapPawns.FreeColonists.Count;
            float contentHeight = 440f + colonistCount * 32f + 90f;
            if (w.enabled) contentHeight += 5f * 34f;
            if (b != null && b.enabled) contentHeight += 90f + colonistCount * 24f;

            Rect outRect = inRect;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, contentHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 2f;

            float scale = DshEconomyMod.Settings != null ? DshEconomyMod.Settings.stepScale : 1f;
            int stepMoney = Mathf.Max(1, Mathf.RoundToInt(5f * scale));
            int stepPct = Mathf.Max(1, Mathf.RoundToInt(10f * scale));
            float stepDay = Mathf.Clamp(0.1f * scale, 0.01f, 1f);
            float stepMult = Mathf.Clamp(0.1f * scale, 0.01f, 1f);

            // ── 发薪：一个勾，关掉就整块收起且完全不生效 ──
            Widgets.Checkbox(new Vector2(4f, y + 3f), ref w.enabled, 24f);
            Widgets.Label(new Rect(32f, y + 4f, 120f, 24f), "发薪");
            if (!w.enabled)
            {
                Widgets.Label(new Rect(150f, y + 4f, 620f, 24f), "已关闭：不发工资，奖金也不会自动发放");
            }
            y += 34f;

            if (w.enabled)
            {
                Widgets.Label(new Rect(120f, y + 4f, 80f, 24f), "劳动日价值");
                if (Widgets.ButtonText(new Rect(202f, y, 34f, 26f), "-" + stepMoney))
                {
                    w.laborDayValue = Mathf.Clamp(w.laborDayValue - stepMoney, 0f, 300f);
                }
                w.laborDayValue = NumField(new Rect(240f, y, 46f, 24f), w.laborDayValue, 0f, 300f, "0.##");
                if (Widgets.ButtonText(new Rect(290f, y, 34f, 26f), "+" + stepMoney))
                {
                    w.laborDayValue = Mathf.Clamp(w.laborDayValue + stepMoney, 0f, 300f);
                }

                Widgets.Label(new Rect(340f, y + 4f, 70f, 24f), "工资比例");
                if (Widgets.ButtonText(new Rect(412f, y, 44f, 26f), "-" + stepPct))
                {
                    w.wageShare = Mathf.Clamp(w.wageShare - stepPct / 100f, 0f, 1f);
                }
                w.wageShare = NumField(new Rect(460f, y, 44f, 24f), w.wageShare * 100f, 0f, 100f, "0.#") / 100f;
                Widgets.Label(new Rect(508f, y + 4f, 18f, 24f), "%");
                if (Widgets.ButtonText(new Rect(528f, y, 44f, 26f), "+" + stepPct))
                {
                    w.wageShare = Mathf.Clamp(w.wageShare + stepPct / 100f, 0f, 1f);
                }
                y += 34f;

                Widgets.Label(new Rect(120f, y + 4f, 80f, 24f), "日薪上限");
                if (Widgets.ButtonText(new Rect(202f, y, 34f, 26f), "-" + stepMoney))
                {
                    w.wageCapPerDay = Mathf.Clamp(w.wageCapPerDay - stepMoney, 0f, 300f);
                }
                w.wageCapPerDay = NumField(new Rect(240f, y, 46f, 24f), w.wageCapPerDay, 0f, 300f, "0.##");
                if (Widgets.ButtonText(new Rect(290f, y, 34f, 26f), "+" + stepMoney))
                {
                    w.wageCapPerDay = Mathf.Clamp(w.wageCapPerDay + stepMoney, 0f, 300f);
                }
                Widgets.Label(new Rect(340f, y + 4f, 500f, 24f), "标准工时内实发工资不超过「人日 × 上限」；加班费不受此限");
                y += 34f;

                Widgets.Label(new Rect(120f, y + 4f, 120f, 24f), "步长（小/中/大）");
                if (Widgets.ButtonText(new Rect(244f, y, 44f, 26f), "小"))
                {
                    SetStepScale(0.1f);
                }
                if (Widgets.ButtonText(new Rect(292f, y, 44f, 26f), "中"))
                {
                    SetStepScale(1f);
                }
                if (Widgets.ButtonText(new Rect(340f, y, 44f, 26f), "大"))
                {
                    SetStepScale(2f);
                }
                Widgets.Label(new Rect(392f, y + 4f, 460f, 24f),
                    "当前：" + (scale * 100f).ToString("0") + "%（小=1% / ±1，中=10% / ±5，大=20% / ±10）");
                y += 34f;

                Widgets.Label(new Rect(120f, y + 4f, 90f, 24f), "标准工作时");
                if (Widgets.ButtonText(new Rect(212f, y, 34f, 26f), "-.1"))
                {
                    w.standardWorkday = Mathf.Clamp(w.standardWorkday - stepDay, 0f, 3f);
                }
                w.standardWorkday = NumField(new Rect(250f, y, 46f, 24f), w.standardWorkday, 0f, 3f, "0.0#");
                if (Widgets.ButtonText(new Rect(300f, y, 34f, 26f), "+.1"))
                {
                    w.standardWorkday = Mathf.Clamp(w.standardWorkday + stepDay, 0f, 3f);
                }
                Widgets.Label(new Rect(350f, y + 4f, 110f, 24f),
                    "≈ " + (w.standardWorkday * 24f).ToString("0") + " 小时");
                Widgets.Label(new Rect(466f, y + 4f, 380f, 24f),
                    w.overtimeEnabled
                        ? "加班费的分界点：超出部分按倍率计薪，会改变工资与剩余"
                        : "仅统计口径：只决定剩余怎么分类，不影响工资或总剩余");
                TooltipHandler.TipRegion(new Rect(120f, y, 300f, 26f),
                    w.overtimeEnabled
                        ? "加班工资已开启：\n"
                          + "· 标准工作时以内按工资比例计薪\n"
                          + "· 超出部分按加班倍率计薪\n"
                          + "所以改它会影响工资、总剩余和小人收入。"
                        : "标准工作时只用于划分「绝对剩余 / 相对剩余」。\n"
                          + "当前加班工资是关闭的，改它不会改变工资、不会改变总剩余，\n"
                          + "也不会让小人多干或少干。");
                y += 34f;

                Widgets.Checkbox(new Vector2(212f, y + 3f), ref w.overtimeEnabled, 24f);
                Widgets.Label(new Rect(240f, y + 4f, 80f, 24f), "加班工资");
                if (w.overtimeEnabled)
                {
                    if (Widgets.ButtonText(new Rect(322f, y, 34f, 26f), "-.1"))
                    {
                        w.overtimeMultiplier = Mathf.Clamp(w.overtimeMultiplier - stepMult, 1f, 4f);
                    }
                    w.overtimeMultiplier = NumField(new Rect(360f, y, 46f, 24f), w.overtimeMultiplier, 1f, 4f, "0.0#");
                    Widgets.Label(new Rect(410f, y + 4f, 16f, 24f), "x");
                    if (Widgets.ButtonText(new Rect(430f, y, 34f, 26f), "+.1"))
                    {
                        w.overtimeMultiplier = Mathf.Clamp(w.overtimeMultiplier + stepMult, 1f, 4f);
                    }
                    Widgets.Label(new Rect(470f, y + 4f, 360f, 24f), "超出标准工时的部分按此倍率计薪");
                }
                else
                {
                    Widgets.Label(new Rect(322f, y + 4f, 500f, 24f), "已关闭：加班不额外付钱，标准工作时仅作统计口径");
                }
                y += 34f;
            }

            // ── 拖欠工资：一个勾控制启用，剩余次数用完自动关闭 ──
            Widgets.Checkbox(new Vector2(4f, y + 3f), ref comp.arrearsEnabled, 24f);
            Widgets.Label(new Rect(32f, y + 4f, 120f, 24f), "拖欠工资");
            if (!comp.arrearsEnabled)
            {
                Widgets.Label(new Rect(150f, y + 4f, 660f, 24f),
                    "已关闭：正常发薪（历史累计欠薪 " + comp.arrearsCount + " 次）");
            }
            else
            {
                if (Widgets.ButtonText(new Rect(150f, y, 34f, 26f), "-"))
                {
                    comp.arrearsAutoTimes = Mathf.Clamp(comp.arrearsAutoTimes - 1, 1, 50);
                }
                Widgets.Label(new Rect(190f, y + 4f, 130f, 24f), "自动欠薪 " + comp.arrearsAutoTimes + " 次");
                if (Widgets.ButtonText(new Rect(326f, y, 34f, 26f), "+"))
                {
                    comp.arrearsAutoTimes = Mathf.Clamp(comp.arrearsAutoTimes + 1, 1, 50);
                }
                Widgets.Label(new Rect(372f, y + 4f, 220f, 24f), "剩余 " + comp.arrearsRemaining + " 次");
                if (Widgets.ButtonText(new Rect(560f, y, 120f, 26f), "立即欠一次"))
                {
                    comp.arrearsRemaining += 1;
                    comp.arrearsCount += 0;
                    comp.arrearsEnabled = true;
                }
                TooltipHandler.TipRegion(new Rect(150f, y, 530f, 26f),
                    "开启后，发薪日不发工资，累计欠薪次数 +1。\n"
                    + "次数越多，工作满意度跌得越狠（指数级）。\n"
                    + "剩余次数归零后会自动关闭。");
            }
            y += 34f;

            // ── 奖金：同样一个勾，关掉就收起且完全不生效 ──
            if (b != null)
            {
                Widgets.Checkbox(new Vector2(4f, y + 3f), ref b.enabled, 24f);
                Widgets.Label(new Rect(32f, y + 4f, 120f, 24f), "奖金");
                if (!b.enabled)
                {
                    Widgets.Label(new Rect(150f, y + 4f, 620f, 24f), "已关闭：不发放奖金，也不结算满意度奖惩");
                }
                y += 34f;

                if (b.enabled)
                {
                    if (Widgets.ButtonText(new Rect(120f, y, 30f, 26f), "-5"))
                    {
                        b.amount = Mathf.Clamp(b.amount - 5f, 0f, 1000f);
                    }
                    b.amount = NumField(new Rect(154f, y, 50f, 24f), b.amount, 0f, 1000f, "0.##");
                    if (Widgets.ButtonText(new Rect(208f, y, 30f, 26f), "+5"))
                    {
                        b.amount = Mathf.Clamp(b.amount + 5f, 0f, 1000f);
                    }

                    if (Widgets.ButtonText(new Rect(252f, y, 24f, 26f), "<"))
                    {
                        b.activeIndex = b.activeIndex <= 0 ? b.groups.Count - 1 : b.activeIndex - 1;
                    }
                    string groupName = b.Active.name;
                    string editedName = Widgets.TextField(new Rect(280f, y, 76f, 24f), groupName);
                    if (editedName != groupName) b.Active.name = editedName;
                    if (Widgets.ButtonText(new Rect(360f, y, 24f, 26f), ">"))
                    {
                        b.activeIndex = (b.activeIndex + 1) % b.groups.Count;
                    }
                    if (Widgets.ButtonText(new Rect(390f, y, 44f, 26f), "+组"))
                    {
                        b.AddGroup();
                    }
                    if (Widgets.ButtonText(new Rect(438f, y, 44f, 26f), "-组"))
                    {
                        b.RemoveActiveGroup();
                    }
                    if (Widgets.ButtonText(new Rect(488f, y, 80f, 26f), "立即发放"))
                    {
                        int paid = EconomyBonus.Pay(map, b);
                        Messages.Message("已发放奖金 " + paid + " 白银", MessageTypeDefOf.NeutralEvent, false);
                    }
                    if (Widgets.ButtonText(new Rect(574f, y, 60f, 26f), "选人"))
                    {
                        Find.WindowStack.Add(new Window_BonusMembers(b.Active, map.mapPawns.FreeColonists));
                    }
                    Widgets.Checkbox(new Vector2(642f, y + 3f), ref b.autoPay, 24f);
                    Widgets.Label(new Rect(670f, y + 4f, 120f, 24f), "发薪时自动发");
                    y += 34f;

                    // 获奖人数（同时决定末位惩罚人数），上限为成员数的 30%
                    List<Pawn> memberList = b.Active.members;
                    // 上限按"奖金组人数"的 30%；组还没选人时按全体人口算
                    int memberCount = memberList != null && memberList.Count > 0
                        ? memberList.Count
                        : map.mapPawns.FreeColonistsCount;
                    int maxWin = b.MaxRecipients(memberCount);
                    if (Widgets.ButtonText(new Rect(4f, y, 24f, 24f), "-"))
                    {
                        b.recipients = Mathf.Clamp(b.recipients - 1, 1, maxWin);
                    }
                    Widgets.Label(new Rect(32f, y + 3f, 32f, 24f), b.recipients.ToString());
                    if (Widgets.ButtonText(new Rect(64f, y, 24f, 24f), "+"))
                    {
                        b.recipients = Mathf.Clamp(b.recipients + 1, 1, maxWin);
                    }
                    Widgets.Label(new Rect(96f, y + 3f, viewRect.width - 100f, 24f),
                        "获奖人数 / 末位惩罚人数   上限 " + maxWin + " 人（组内 " + memberCount + " 人的 30%）");
                    if (b.recipients > maxWin) b.recipients = maxWin;
                    y += 30f;

                    List<Pawn> ranked = new List<Pawn>();
                    List<Pawn> members = b.Active.members;
                    if (members != null)
                    {
                        for (int i = 0; i < members.Count; i++)
                        {
                            Pawn mp = members[i];
                            if (mp != null && mp.Spawned && mp.IsColonist) ranked.Add(mp);
                        }
                    }
                    ranked.SortByDescending(mp => EconomyLedger.GetRawLabor(mp));

                    Widgets.Label(new Rect(8f, y, 400f, 22f), b.Active.name + " 排名（按劳动量）");
                    y += 22f;
                    for (int i = 0; i < ranked.Count; i++)
                    {
                        Pawn rp = ranked[i];
                        Widgets.Label(new Rect(20f, y, viewRect.width - 30f, 22f),
                            (i + 1) + ". " + rp.LabelShort
                            + "    实际工时 " + (EconomyLedger.GetRawLabor(rp) / GenDate.TicksPerDay).ToString("0.00") + " 人日");
                        y += 22f;
                    }
                    if (ranked.Count == 0)
                    {
                        Widgets.Label(new Rect(20f, y, viewRect.width - 30f, 22f), "（空，发薪时会自动纳入全体）");
                        y += 22f;
                    }
                }
            }

            // ── 汇总 ──
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            Dictionary<Pawn, int> silverByOwner = EconomyBank.PrivateSilverByOwner(map);
            int totalPersonal = 0;
            foreach (var kv in silverByOwner) totalPersonal += kv.Value;
            float totalLabor = 0f;
            float totalProduced = 0f;
            float totalWage = 0f;
            float totalAbs = 0f;
            float totalRel = 0f;
            for (int i = 0; i < colonists.Count; i++)
            {
                float lab = EconomyLedger.GetLabor(colonists[i]);
                totalLabor += lab;
                totalProduced += w.ProducedValue(lab, colonists[i]);
                totalWage += w.WageFor(lab, colonists[i], EconomyLedger.GetRawLabor(colonists[i]));
                totalAbs += w.AbsoluteSurplus(lab, colonists[i], EconomyLedger.GetRawLabor(colonists[i]));
                totalRel += w.RelativeSurplus(lab, colonists[i], EconomyLedger.GetRawLabor(colonists[i]));
            }
            float totalSurplus = totalAbs + totalRel;
            float rate = totalWage > 0.01f ? totalSurplus / totalWage : 0f;

            Widgets.DrawBoxSolid(new Rect(2f, y, viewRect.width - 6f, 96f), new Color(0.16f, 0.14f, 0.10f, 0.9f));
            Widgets.Label(new Rect(10f, y + 4f, 320f, 24f), "剩余价值（本期）");
            Widgets.Label(new Rect(10f, y + 24f, viewRect.width - 20f, 24f),
                "创造总值 " + totalProduced.ToString("0.0")
                + "     工资 " + totalWage.ToString("0.0")
                + "     剩余 " + totalSurplus.ToString("0.0")
                + "     剩余价值率 " + (rate * 100f).ToString("0") + "%");
            Widgets.Label(new Rect(10f, y + 46f, viewRect.width - 20f, 24f),
                "绝对剩余 " + totalAbs.ToString("0.0")
                + "     （超出标准工作时长的那部分工时创造的：靠加班/延长劳动时间）");
            Widgets.Label(new Rect(10f, y + 68f, viewRect.width - 20f, 24f),
                "相对剩余 " + totalRel.ToString("0.0")
                + "     （标准工作时长以内产生：工资比例 + 上限截留）");
            y += 102f;

            Widgets.Label(new Rect(4f, y, viewRect.width, 22f),
                "公共库存 " + EconomyBank.ColonySilver(map).ToString("N0")
                + "     个人白银 " + totalPersonal.ToString("N0")
                + "     累计劳动 " + (totalLabor / GenDate.TicksPerDay).ToString("0.0") + " 人日");
            y += 24f;

            // ── 人口财富与收入差距：一秒最多重算一次，避免每帧全量扫描造成卡顿 ──
            if (statsMap != map || Find.TickManager.TicksGame - statsTick > 60 || statsTick < 0)
            {
                statsCache = TaxPopulation.Compute(map);
                statsMap = map;
                statsTick = Find.TickManager.TicksGame;
            }
            TaxPopulation.ColonyStats cs = statsCache;
            Widgets.Label(new Rect(4f, y, viewRect.width, 22f),
                "私有财富：总计 " + cs.wealthTotal.ToString("N0")
                + "（含私银 " + cs.privateSilverTotal.ToString("N0") + "）"
                + "   平均 " + cs.wealthAvg.ToString("N0")
                + "   最高 " + cs.wealthMax.ToString("N0")
                + "   最低 " + cs.wealthMin.ToString("N0"));
            y += 22f;

            Widgets.Label(new Rect(4f, y, viewRect.width, 22f),
                "公共物资（随身携带、未私有化）：合计 " + cs.publicTotal.ToString("N0") + " 银，不计入个人财富");
            y += 22f;

            float ratio = cs.incomeMin > 0.01f ? cs.incomeMax / cs.incomeMin : 0f;
            Widgets.Label(new Rect(4f, y, viewRect.width, 22f),
                "本期收入：合计 " + cs.incomeTotal.ToString("N0")
                + "   最高 " + cs.incomeMax.ToString("N0")
                + "   最低 " + cs.incomeMin.ToString("N0")
                + (ratio > 0f ? "   倍差 " + ratio.ToString("0.0") + " 倍" : "")
                + "   收入基尼 " + cs.incomeGini.ToString("0.00"));
            y += 22f;

            Widgets.Label(new Rect(4f, y, viewRect.width, 22f),
                "财富分档（按排名三等分）：低 " + cs.tierLow + " 人   中 " + cs.tierMid + " 人   高 " + cs.tierHigh + " 人");
            y += 26f;

            // ── 工作类型工资权重：收进独立窗口，主界面只留入口 ──
            if (Widgets.ButtonText(new Rect(4f, y, 140f, 26f), "工作权重设置"))
            {
                Find.WindowStack.Add(new Window_WorkWeights());
            }
            if (Widgets.ButtonText(new Rect(150f, y, 140f, 26f), "个人工资设置"))
            {
                Find.WindowStack.Add(new Window_PawnWage());
            }
            if (Widgets.ButtonText(new Rect(296f, y, 110f, 26f), "工单账本"))
            {
                Find.WindowStack.Add(new Window_BillLedger());
            }
            Widgets.Label(new Rect(412f, y + 4f, viewRect.width - 420f, 24f),
                "劳动量 × 权重 = 有效工时；上限按人结算，没单独设的沿用全局 " + w.wageCapPerDay.ToString("0") + " 银/日");
            y += 30f;

            if (Widgets.ButtonText(new Rect(4f, y, 140f, 26f), "立即发放全部"))
            {
                int paid = comp.PayWagesNow();
                Messages.Message("已发放工资 " + paid + " 白银", MessageTypeDefOf.NeutralEvent, false);
            }
            Widgets.Label(new Rect(160f, y + 4f, 640f, 24f),
                "调高劳动日价值 → 相对剩余↑；多干活 → 绝对剩余↑；降低工资比例 → 两者同比↑");
            y += 32f;

            // ── 表头 ──
            Widgets.Label(new Rect(8f, y, 130f, 22f), "居民");
            Widgets.Label(new Rect(140f, y, 55f, 22f), "劳动(天)");
            Widgets.Label(new Rect(198f, y, 55f, 22f), "创造");
            Widgets.Label(new Rect(256f, y, 55f, 22f), "工资");
            Widgets.Label(new Rect(314f, y, 55f, 22f), "绝对");
            Widgets.Label(new Rect(372f, y, 55f, 22f), "相对");
            Widgets.Label(new Rect(430f, y, 65f, 22f), "余额");
            Widgets.Label(new Rect(492f, y, 55f, 22f), "满意度");
            Widgets.Label(new Rect(548f, y, 40f, 22f), "奖金");
            y += 24f;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                float lab = EconomyLedger.GetLabor(p);
                int balance;
                silverByOwner.TryGetValue(p, out balance);
                float produced = w.ProducedValue(lab, p);
                float wage = w.WageFor(lab, p, EconomyLedger.GetRawLabor(p));

                Rect row = new Rect(4f, y, viewRect.width - 8f, 30f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));

                Widgets.Label(new Rect(row.x + 4f, row.y + 5f, 130f, 24f), p.LabelShort);
                Widgets.Label(new Rect(row.x + 136f, row.y + 5f, 58f, 24f), (lab / GenDate.TicksPerDay).ToString("0.00"));
                // 用延迟求值：只有真的要显示提示时才去算分工时，避免每帧为每一行拼字符串
                TooltipHandler.TipRegion(new Rect(row.x + 136f, row.y + 5f, 58f, 24f),
                    new TipSignal(() => WorkBreakdown(comp, p, w), p.thingIDNumber));
                Widgets.Label(new Rect(row.x + 194f, row.y + 5f, 58f, 24f), produced.ToString("0.0"));
                Widgets.Label(new Rect(row.x + 252f, row.y + 5f, 58f, 24f), wage.ToString("0.0"));
                Widgets.Label(new Rect(row.x + 310f, row.y + 5f, 58f, 24f), w.AbsoluteSurplus(lab, p, EconomyLedger.GetRawLabor(p)).ToString("0.0"));
                Widgets.Label(new Rect(row.x + 368f, row.y + 5f, 58f, 24f), w.RelativeSurplus(lab, p, EconomyLedger.GetRawLabor(p)).ToString("0.0"));
                Widgets.Label(new Rect(row.x + 426f, row.y + 5f, 60f, 24f), balance.ToString("N0"));
                Widgets.Label(new Rect(row.x + 488f, row.y + 5f, 55f, 24f), EconomyLedger.GetSatisfaction(p).ToString("0"));

                BonusPolicy bp = comp.bonus;
                if (bp != null && bp.enabled)
                {
                    bool rec = bp.Active.Has(p);
                    bool before = rec;
                    Widgets.Checkbox(new Vector2(row.x + 548f, row.y + 4f), ref rec, 22f);
                    if (rec != before) bp.Active.Toggle(p);
                }

                if (Widgets.ButtonText(new Rect(row.x + 582f, row.y + 3f, 60f, 24f), "结算"))
                {
                    EconomyWages.PayOne(p, map, w);
                }
                if (balance > 0 && Widgets.ButtonText(new Rect(row.x + 648f, row.y + 3f, 60f, 24f), "征用"))
                {
                    int taken = EconomyWages.Confiscate(p, map);
                    Messages.Message("已征用 " + p.LabelShort + " 的白银 " + taken, MessageTypeDefOf.NeutralEvent, false);
                }
                y += 32f;
            }

            Widgets.EndScrollView();
        }
    }
}
