using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>测试沙盒：一键造人、发钱、调满意度、触发状态、清空运行态。</summary>
    public class Window_TestSandbox : Window
    {
        private Vector2 scroll;
        private string verifyText = "";

        public Window_TestSandbox()
        {
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = false;
            forcePause = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(640f, 620f); }
        }

        private static Map Map
        {
            get { return Find.CurrentMap; }
        }

        public override void DoWindowContents(Rect inRect)
        {
            Map map = Map;
            if (map == null)
            {
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f), "没有当前地图");
                return;
            }

            Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 22f),
                "测试沙盒（改动立刻生效，只用于调试存档）");
            float y = inRect.y + 28f;

            Rect outRect = new Rect(inRect.x, y, inRect.width, inRect.height - 34f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, 1600f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float rowY = 4f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "生成 5 个测试殖民者"))
            {
                SpawnTestColonists(map, 5);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "每人发 200 私银"))
            {
                List<Pawn> colonists = map.mapPawns.FreeColonists;
                for (int i = 0; i < colonists.Count; i++)
                {
                    EconomyBank.SplitSilverToPrivate(map, colonists[i], 200);
                }
                Messages.Message("已发放 200 私银 × " + colonists.Count, MessageTypeDefOf.NeutralEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "事件：全体加薪 +2/日·3天"))
            {
                float b = AvgSatisfaction(map);
                EconomySatisfactionEvents.AddForAll(map, "加薪", 2f, 3);
                EconomySatisfaction.DailyUpdate(map);
                Messages.Message("加薪已加入并结算一天：全体 " + b.ToString("0.0") + " → "
                    + AvgSatisfaction(map).ToString("0.0"), MessageTypeDefOf.PositiveEvent, false);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "事件：全体欠薪 −3/日·3天"))
            {
                float b = AvgSatisfaction(map);
                EconomySatisfactionEvents.AddForAll(map, "欠薪", -3f, 3);
                EconomySatisfaction.DailyUpdate(map);
                Messages.Message("欠薪已加入并结算一天：全体 " + b.ToString("0.0") + " → "
                    + AvgSatisfaction(map).ToString("0.0"), MessageTypeDefOf.NegativeEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "事件：恶劣住宿 −1.5/日·5天"))
            {
                float b = AvgSatisfaction(map);
                EconomySatisfactionEvents.AddForAll(map, "恶劣住宿", -1.5f, 5);
                EconomySatisfaction.DailyUpdate(map);
                Messages.Message("恶劣住宿已加入并结算一天：全体 " + b.ToString("0.0") + " → "
                    + AvgSatisfaction(map).ToString("0.0"), MessageTypeDefOf.NegativeEvent, false);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "清空满意度事件"))
            {
                EconomySatisfactionEvents.Clear();
                Messages.Message("已清空全部满意度事件", MessageTypeDefOf.NeutralEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "立即结算一天（满意度）"))
            {
                EconomySatisfaction.DailyUpdate(map);
                Messages.Message("已按当前事件结算一天满意度（不加速游戏时间）", MessageTypeDefOf.NeutralEvent, false);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "按当前条件重算（全体）"))
            {
                EconomySatisfaction.DailyUpdate(map);
                Messages.Message("已按当前条件重算全体满意度：" + AvgSatisfaction(map).ToString("0.0"),
                    MessageTypeDefOf.NeutralEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "触发 3 人革命"))
            {
                int n = EconomyUnrest.ForceRevolution(map, 2f);
                Messages.Message("革命：" + n + " 人", MessageTypeDefOf.NegativeEvent, false);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "触发 3 人暴动"))
            {
                int n = EconomyUnrest.ForceRiot(map, 8f);
                Messages.Message("暴动：" + n + " 人", MessageTypeDefOf.NegativeEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "打印工单账本"))
            {
                var ledger = map.GetComponent<EconomyBillLedger>();
                if (ledger != null)
                {
                    Log.Message("[BILL] 工单账本：\n" + ledger.Report());
                    Messages.Message("工单账本已写入日志（[BILL]）", MessageTypeDefOf.NeutralEvent, false);
                }
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "触发 3 人罢工"))
            {
                int n = EconomyUnrest.ForceStrike(map, 15f);
                Messages.Message("罢工：" + n + " 人", MessageTypeDefOf.NegativeEvent, false);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "清空运行状态"))
            {
                EconomyUnrest.ClearRuntimeState();
                Messages.Message("已清空 罢工/暴动/革命 的静态状态", MessageTypeDefOf.NeutralEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "打印当前状态到日志"))
            {
                DumpState(map);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "国库 +5000"))
            {
                EconomyBank.GiveColonySilver(map, 5000, map.Center);
                Messages.Message("国库 +5000", MessageTypeDefOf.PositiveEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "验证：满意度 → 工作速度"))
            {
                verifyText = EconomyDiagnostics.VerifyWorkSpeed(true);
                Log.Message("[SPEED] 自检结果：" + verifyText.Replace("\n", " | "));
                Messages.Message("已写入日志（[SPEED] 开头）", MessageTypeDefOf.NeutralEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "导出参考贴图"))
            {
                TextureDump.Force();
                Messages.Message("已尝试导出，见日志 [TEXDUMP] / [DSH-ECONOMY] 贴图导出完成", MessageTypeDefOf.NeutralEvent, false);
            }
            rowY += 34f;

            // ── 劳动时长 / 休息 → 效率 ──
            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "今日劳动 +2 小时"))
            {
                AddDailyHours(map, 2f);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "今日劳动 +8 小时"))
            {
                AddDailyHours(map, 8f);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "清空今日劳动小时"))
            {
                MapEconomyComponent c = map.GetComponent<MapEconomyComponent>();
                if (c != null) c.ClearDailyWork();
                EconomySatisfaction.RecomputeAll(map);
                Messages.Message("今日劳动小时已清零", MessageTypeDefOf.NeutralEvent, false);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "休息 −20%（全体）"))
            {
                SetRest(map, -0.2f);
            }
            rowY += 34f;

            // ── 健康 / 欠薪 / 贫富差距 / 制度诊断 ──
            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "健康：营养不良（全体）"))
            {
                AddHediffAll(map, "Malnutrition", 0.6f);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "健康：极度疲劳（全体）"))
            {
                SetRestAll(map, 0.05f);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "健康：添加 3 处外伤"))
            {
                AddInjuriesAll(map, 3);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "健康：清除上述状态"))
            {
                ClearHealthAll(map);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "欠薪：开启（自动 1 次）"))
            {
                MapEconomyComponent c = map.GetComponent<MapEconomyComponent>();
                if (c != null)
                {
                    c.arrearsEnabled = true;
                    c.arrearsRemaining = Mathf.Max(1, c.arrearsAutoTimes);
                    Messages.Message("欠薪已开启：下次结算欠薪 " + c.arrearsRemaining + " 次",
                        MessageTypeDefOf.NeutralEvent, false);
                }
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "欠薪：立即欠一次（马上结算）"))
            {
                MapEconomyComponent c = map.GetComponent<MapEconomyComponent>();
                if (c != null)
                {
                    c.arrearsEnabled = true;
                    c.arrearsRemaining = 1;
                    c.Settle(24);
                    Messages.Message("已立即结算并欠薪一次", MessageTypeDefOf.NegativeEvent, false);
                }
            }
            rowY += 34f;
            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "欠薪：关闭并清零"))
            {
                MapEconomyComponent c = map.GetComponent<MapEconomyComponent>();
                if (c != null) { c.arrearsEnabled = false; c.arrearsRemaining = 0; c.arrearsCount = 0; }
                EconomySatisfaction.RecomputeAll(map);
                Messages.Message("欠薪已关闭，计数清零", MessageTypeDefOf.NeutralEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "贫富差距：打印统计"))
            {
                DumpWealthGap(map);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "制度：打印文化与模因"))
            {
                EconomyDoctrine.LogMemeDiagnostics(map);
                Messages.Message("已写入日志（[DOCTRINE]）", MessageTypeDefOf.NeutralEvent, false);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "休息 = 20%（全体）"))
            {
                SetRestTo(map, 0.2f);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "休息拉满（全体）"))
            {
                SetRestTo(map, 1f);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "打印休息与效率"))
            {
                PrintRest(map);
            }
            rowY += 34f;
            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "贸易：召唤商队"))
            {
                verifyText = EconomySandboxTrade.SummonCaravan(map);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "贸易：立刻和派系交易"))
            {
                verifyText = EconomySandboxTrade.OpenOrbitalTrade(map);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "贸易：和图里的商人交易"))
            {
                verifyText = EconomySandboxTrade.OpenTradeWithNearestTrader(map);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "贸易：清空本次额度"))
            {
                EconomyTradeLimit.End();
                verifyText = "贸易额度已清空";
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "贸易：给商人 5000 白银"))
            {
                verifyText = EconomySandboxTrade.GiveTraderSilver(5000);
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "贸易：召唤轨道商船"))
            {
                verifyText = EconomySandboxTrade.OpenOrbitalTrade(map);
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "折旧：让全体生产建筑故障"))
            {
                int n = EconomyDepreciation.BreakAll(map);
                verifyText = "已让 " + n + " 个生产建筑故障";
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "折旧：免费修好全体"))
            {
                int n = EconomyDepreciation.RepairAll(map);
                verifyText = "已修好 " + n + " 个生产建筑";
            }
            rowY += 34f;

            if (Widgets.ButtonText(new Rect(4f, rowY, 240f, 28f), "折旧：打印统计"))
            {
                EconomyDepreciation.DumpStats(map);
                verifyText = "统计已打印到日志";
            }
            if (Widgets.ButtonText(new Rect(254f, rowY, 240f, 28f), "折旧：开/关"))
            {
                MapEconomyComponent mc = map.GetComponent<MapEconomyComponent>();
                if (mc != null)
                {
                    mc.depreciationEnabled = !mc.depreciationEnabled;
                    verifyText = "折旧已" + (mc.depreciationEnabled ? "开启" : "关闭");
                }
            }
            rowY += 34f;

            if (!verifyText.NullOrEmpty())
            {
                float h = Text.CalcHeight(verifyText, viewRect.width - 8f);
                Widgets.Label(new Rect(4f, rowY, viewRect.width - 8f, h), verifyText);
                rowY += h + 8f;
            }
            rowY += 6f;

            Widgets.Label(new Rect(4f, rowY, viewRect.width - 8f, 22f),
                "殖民者：" + map.mapPawns.FreeColonists.Count + " 人"
                + "   国库：" + EconomyBank.ColonySilver(map).ToString("N0") + " 银");
            rowY += 24f;

            List<Pawn> list = map.mapPawns.FreeColonists;
            for (int i = 0; i < list.Count; i++)
            {
                Pawn p = list[i];
                string state = p.InMentalState && p.MentalStateDef != null ? p.MentalStateDef.label : "—";
                string ev = EconomySatisfactionEvents.Describe(p);
                Widgets.Label(new Rect(4f, rowY, viewRect.width - 8f, 22f),
                    p.LabelShort + "   满意度 " + EconomyLedger.GetSatisfaction(p).ToString("0")
                    + "   私银 " + EconomyBank.PrivateSilver(map, p)
                    + "   状态 " + state
                    + (ev == "（无）" ? "" : "   事件 " + ev));
                rowY += 22f;
            }

            Widgets.EndScrollView();
        }

        private static void SpawnTestColonists(Map map, int count)
        {
            IntVec3 center = map.Center;
            for (int i = 0; i < count; i++)
            {
                PawnKindDef kind = PawnKindDefOf.Colonist;
                Pawn pawn = PawnGenerator.GeneratePawn(new PawnGenerationRequest(
                    kind, Faction.OfPlayer, PawnGenerationContext.NonPlayer, -1,
                    forceGenerateNewPawn: true));

                IntVec3 cell = CellFinder.RandomClosewalkCellNear(center, map, 15);
                GenSpawn.Spawn(pawn, cell, map);
                if (pawn.Faction != Faction.OfPlayer) pawn.SetFaction(Faction.OfPlayer);

                EquipShotgun(pawn);
                GiveShotgunAmmo(pawn);

                Messages.Message("已生成测试殖民者：" + pawn.LabelShort, MessageTypeDefOf.NeutralEvent, false);
            }
        }

        /// <summary>优先发霰弹枪，没有就找名字带 Shotgun 的远程武器。</summary>
        private static void EquipShotgun(Pawn pawn)
        {
            if (pawn == null || pawn.equipment == null) return;

            ThingDef gun = null;
            string[] preferred = { "Gun_Shotgun", "Gun_PumpShotgun", "Gun_ChainShotgun", "Gun_SawedOffShotgun" };
            for (int i = 0; i < preferred.Length; i++)
            {
                gun = DefDatabase<ThingDef>.GetNamedSilentFail(preferred[i]);
                if (gun != null) break;
            }
            if (gun == null)
            {
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef d = all[i];
                    if (d == null || !d.IsRangedWeapon) continue;
                    if (d.defName.IndexOf("Shotgun", System.StringComparison.OrdinalIgnoreCase) >= 0) { gun = d; break; }
                }
            }
            if (gun == null) return;

            try
            {
                ThingWithComps w = ThingMaker.MakeThing(gun) as ThingWithComps;
                if (w != null) pawn.equipment.AddEquipment(w);
                Log.Message("[SANDBOX] " + pawn.LabelShort + " 发枪 " + gun.defName);
            }
            catch (System.Exception e)
            {
                Log.Warning("[SANDBOX] 发枪失败：" + e.Message);
            }
        }

        /// <summary>CE 环境：给背包塞霰弹枪弹药，保证能换弹。</summary>
        private static void GiveShotgunAmmo(Pawn pawn)
        {
            if (pawn == null || pawn.inventory == null || pawn.inventory.innerContainer == null) return;

            // 1) 首选：从武器自己的 CE 弹药组件读出它配的弹药（不会拿到弹壳）
            ThingDef ammo = CECompat.GetAmmoTypeForWeapon(pawn);

            // 2) 兜底：按名字找，明确排除弹壳
            if (ammo == null)
            {
                List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < all.Count; i++)
                {
                    ThingDef d = all[i];
                    if (d == null || d.defName == null) continue;
                    string n = d.defName;
                    if (n.IndexOf("Ammo", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (n.IndexOf("Casing", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Empty", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    if (n.IndexOf("Shell", System.StringComparison.OrdinalIgnoreCase) >= 0
                        && n.IndexOf("Ammo", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (n.IndexOf("Shotgun", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    ammo = d;
                    break;
                }
            }
            if (ammo == null) return;

            try
            {
                Thing stack = ThingMaker.MakeThing(ammo);
                stack.stackCount = 200;
                if (!pawn.inventory.innerContainer.TryAdd(stack))
                {
                    GenPlace.TryPlaceThing(stack, pawn.Position, pawn.Map, ThingPlaceMode.Near);
                }
                Log.Message("[SANDBOX] " + pawn.LabelShort + " 发弹药 " + ammo.defName + " ×200");
            }
            catch (System.Exception e)
            {
                Log.Warning("[SANDBOX] 发弹药失败：" + e.Message);
            }
        }

        /// <summary>给所有殖民者"今天"加若干小时劳动，立刻重算满意度。</summary>
        private static void AddDailyHours(Map map, float hours)
        {
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                comp.AddDailyWork(colonists[i], hours * 2500f);
            }
            EconomySatisfaction.RecomputeAll(map);
            Messages.Message("全体今日劳动 +" + hours.ToString("0") + " 小时", MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>把所有人的休息需求整体推一个增量（-0.2 = 更累）。</summary>
        private static void SetRest(Map map, float delta)
        {
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Need rest = colonists[i].needs != null ? colonists[i].needs.rest : null;
                if (rest != null) rest.CurLevel = Mathf.Clamp01(rest.CurLevel + delta);
                Need_Satisfaction.Sync(colonists[i]);
            }
            EconomySatisfaction.RecomputeAll(map);
            Messages.Message("全体休息 " + (delta > 0 ? "+" : "") + (delta * 100f).ToString("0") + "%",
                MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>把所有人的休息需求设成固定值。</summary>
        private static void SetRestTo(Map map, float value)
        {
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Need rest = colonists[i].needs != null ? colonists[i].needs.rest : null;
                if (rest != null) rest.CurLevel = Mathf.Clamp01(value);
                Need_Satisfaction.Sync(colonists[i]);
            }
            EconomySatisfaction.RecomputeAll(map);
            Messages.Message("全体休息 = " + (value * 100f).ToString("0") + "%", MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>把每个人的休息、今日工时、效率倍数打到日志。</summary>
        private static void PrintRest(Map map)
        {
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            StringBuilder sb = new StringBuilder();
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                Need rest = p.needs != null ? p.needs.rest : null;
                float r = rest != null ? rest.CurLevel : -1f;
                float h = comp != null ? comp.DailyWorkHours(p) : 0f;
                sb.AppendLine(p.LabelShort + "  休息 " + (r * 100f).ToString("0") + "%"
                    + "  今日工时 " + h.ToString("0.0") + "h"
                    + "  休息效率 " + EconomySatisfaction.RestFactor(p).ToString("0.00")
                    + "  总效率 " + EconomySatisfaction.TotalEfficiency(p).ToString("0.00")
                    + "  满意 " + EconomyLedger.GetSatisfaction(p).ToString("0"));
            }
            Log.Message("[REST] 休息与效率\n" + sb);
            Messages.Message("已写入日志（[REST] 开头）", MessageTypeDefOf.NeutralEvent, false);
        }

        /// <summary>全体平均满意度，用来在消息里显示"结算一天到底变了多少"。</summary>
        private static float AvgSatisfaction(Map map)
        {
            if (map == null) return 0f;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            if (colonists.Count == 0) return 0f;
            float sum = 0f;
            for (int i = 0; i < colonists.Count; i++) sum += EconomyLedger.GetSatisfaction(colonists[i]);
            return sum / colonists.Count;
        }

        private static void SetAllSatisfaction(Map map, float value)
        {
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                EconomyLedger.AddSatisfaction(colonists[i], value - EconomyLedger.GetSatisfaction(colonists[i]));
            }
            Messages.Message("全体满意度 → " + value.ToString("0"), MessageTypeDefOf.NeutralEvent, false);
        }

        private static void DumpState(Map map)
        {
            Log.Message("[SANDBOX] ===== 状态快照 =====");
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                Log.Message("[SANDBOX] " + p.LabelShort
                    + " sat=" + EconomyLedger.GetSatisfaction(p).ToString("0")
                    + " silver=" + EconomyBank.PrivateSilver(map, p)
                    + " wealth=" + TaxPopulation.PawnPrivateWealth(p).ToString("0")
                    + " state=" + (p.InMentalState && p.MentalStateDef != null ? p.MentalStateDef.defName : "null")
                    + " job=" + (p.CurJob != null ? p.CurJob.def.defName : "null"));
            }
        }

        private static void AddHediffAll(Map map, string hediffDefName, float severity)
        {
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail(hediffDefName);
            if (def == null)
            {
                Messages.Message("找不到 HediffDef：" + hediffDefName, MessageTypeDefOf.NegativeEvent, false);
                return;
            }
            int n = 0;
            foreach (Pawn p in map.mapPawns.FreeColonists)
            {
                if (p.health == null) continue;
                Hediff h = HediffMaker.MakeHediff(def, p);
                h.Severity = severity;
                p.health.AddHediff(h);
                n++;
            }
            EconomySatisfaction.RecomputeAll(map);
            Messages.Message("已给 " + n + " 人加上 " + def.LabelCap, MessageTypeDefOf.NeutralEvent, false);
        }

        private static void SetRestAll(Map map, float level)
        {
            int n = 0;
            foreach (Pawn p in map.mapPawns.FreeColonists)
            {
                if (p.needs != null && p.needs.rest != null)
                {
                    p.needs.rest.CurLevel = p.needs.rest.MaxLevel * level;
                    n++;
                }
            }
            EconomySatisfaction.RecomputeAll(map);
            Messages.Message("已把 " + n + " 人的休息压到 " + (level * 100f).ToString("0") + "%（极度疲劳）",
                MessageTypeDefOf.NeutralEvent, false);
        }

        private static void AddInjuriesAll(Map map, int count)
        {
            DamageDef cut = DefDatabase<DamageDef>.GetNamedSilentFail("Cut");
            int n = 0;
            foreach (Pawn p in map.mapPawns.FreeColonists)
            {
                if (p.health == null) continue;
                for (int i = 0; i < count; i++)
                {
                    BodyPartRecord part = p.health.hediffSet.GetNotMissingParts().RandomElement();
                    Hediff_Injury inj = (Hediff_Injury)HediffMaker.MakeHediff(HediffDefOf.Cut, p, part);
                    inj.Severity = 8f;
                    p.health.AddHediff(inj);
                }
                n++;
            }
            EconomySatisfaction.RecomputeAll(map);
            Messages.Message("已给 " + n + " 人各加 " + count + " 处外伤", MessageTypeDefOf.NeutralEvent, false);
        }

        private static void ClearHealthAll(Map map)
        {
            foreach (Pawn p in map.mapPawns.FreeColonists)
            {
                if (p.health == null) continue;
                List<Hediff> list = new List<Hediff>(p.health.hediffSet.hediffs);
                for (int i = 0; i < list.Count; i++)
                {
                    Hediff h = list[i];
                    if (h == null || h.def == null) continue;
                    string dn = h.def.defName;
                    if (dn == "Malnutrition" || dn == "Exhaustion" || dn == "ExtremeFatigue"
                        || dn == "Fatigue" || h is Hediff_Injury)
                        p.health.RemoveHediff(h);
                }
            }
            EconomySatisfaction.RecomputeAll(map);
            Messages.Message("已清除营养不良 / 疲劳 / 外伤", MessageTypeDefOf.NeutralEvent, false);
        }

        private static void DumpWealthGap(Map map)
        {
            float gini = EconomyWealthGap.Gini;
            Log.Message("[GAP] 贫富差距：基尼=" + gini.ToString("0.00"));
            foreach (Pawn p in map.mapPawns.FreeColonists)
            {
                Log.Message("[GAP] " + p.LabelShort
                    + " 私有财富=" + TaxPopulation.PawnPrivateWealth(p, EconomyBank.PrivateSilver(map, p)).ToString("0")
                    + " 比值=" + EconomyWealthGap.RatioOf(p).ToString("0.0")
                    + " 阈值=" + EconomyWealthGap.ThresholdOf(p).ToString("0.0")
                    + " 满意度=" + EconomyLedger.GetSatisfaction(p).ToString("0"));
            }
            Messages.Message("贫富差距已写入日志（[GAP]）", MessageTypeDefOf.NeutralEvent, false);
        }
    }
}
