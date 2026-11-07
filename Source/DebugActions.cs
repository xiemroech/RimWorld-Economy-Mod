using System;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>Def 全部加载完成后构建注册表。放在静态构造里，保证只跑一次。</summary>
    [StaticConstructorOnStartup]
    public static class EconomyBootstrap
    {
        /// <summary>
        /// 给所有人形种族的 ThingDef 挂上「经济」检查页签。
        /// 同样用代码而不是 XML Patch：Page 注册依赖 ThingDef.inspectorTabs，
        /// Patch 只能改原始 XML，继承来的 def 改不到。
        /// </summary>
        private static void AttachPawnTab()
        {
            int added = 0;
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef d = defs[i];
                if (d == null || d.category != ThingCategory.Pawn) continue;
                if (d.race == null || !d.race.Humanlike) continue;

                if (d.inspectorTabsResolved == null) d.inspectorTabsResolved = new List<InspectTabBase>();

                bool has = false;
                for (int k = 0; k < d.inspectorTabsResolved.Count; k++)
                {
                    if (d.inspectorTabsResolved[k] is ITab_Pawn_Economy)
                    {
                        has = true;
                        break;
                    }
                }
                if (!has)
                {
                    d.inspectorTabsResolved.Add(new ITab_Pawn_Economy());
                    added++;
                }
            }
            Log.Message("[DSH-ECONOMY] 已为 " + added + " 个人形种族启用「经济」页签");
        }

        /// <summary>
        /// 给所有存储建筑挂上「补货」检查页签。
        /// 不用 gizmo：部分存储 mod 重写 GetGizmos 且不调 base，组件按钮会被吞掉。
        /// </summary>
        private static void AttachStorageRestockTab()
        {
            int added = 0;
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef d = defs[i];
                if (d == null || d.thingClass == null) continue;
                if (!typeof(Building_Storage).IsAssignableFrom(d.thingClass)) continue;

                if (d.inspectorTabsResolved == null) d.inspectorTabsResolved = new List<InspectTabBase>();

                bool has = false;
                for (int k = 0; k < d.inspectorTabsResolved.Count; k++)
                {
                    if (d.inspectorTabsResolved[k] is ITab_StorageRestock)
                    {
                        has = true;
                        break;
                    }
                }
                if (!has)
                {
                    d.inspectorTabsResolved.Add(new ITab_StorageRestock());
                    added++;
                }
            }
            Log.Message("[DSH-ECONOMY] 已为 " + added + " 种存储建筑启用「补货」页签");
        }

        private static void AttachItemTags()
        {
            List<ThingDef> defs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < defs.Count; i++)
            {
                ThingDef d = defs[i];
                if (d == null || d.category != ThingCategory.Item) continue;
                if (d.comps == null) d.comps = new List<CompProperties>();
                bool has = false;
                for (int j = 0; j < d.comps.Count; j++)
                {
                    if (d.comps[j] is CompProperties_ItemTag)
                    {
                        has = true;
                        break;
                    }
                }
                if (!has) d.comps.Add(new CompProperties_ItemTag());
            }
        }
        /// <summary>
        /// 把屋内惩罚部件直接挂到原版工作台速度统计上。
        /// 之前靠 XML Patch，xpath 匹配不到时会静默失效，所以改成代码保证生效。
        /// </summary>
        private static void EnsureIndoorStatPart()
        {
            try
            {
                StatDef stat = StatDefOf.WorkTableWorkSpeedFactor;
                if (stat == null)
                {
                    Log.Warning("[DSH-ECONOMY] 找不到 WorkTableWorkSpeedFactor");
                    return;
                }
                if (stat.parts == null) stat.parts = new List<StatPart>();
                bool present = false;
                for (int i = 0; i < stat.parts.Count; i++)
                {
                    if (stat.parts[i] is StatPart_IndoorWorkbench) present = true;
                }
                if (!present) stat.parts.Add(new StatPart_IndoorWorkbench());
                Log.Message("[DSH-ECONOMY] 屋内惩罚部件已挂载=" + present + " 部件总数=" + stat.parts.Count);
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 挂载屋内惩罚失败：" + e.Message);
            }
        }

        /// <summary>把满意度部件挂到真实工作速度统计上（影响实际干活快慢）。</summary>
        private static void EnsureSatisfactionStatPart()
        {
            try
            {
                StatDef stat = StatDefOf.WorkSpeedGlobal;
                if (stat == null)
                {
                    Log.Warning("[DSH-ECONOMY] 找不到 WorkSpeedGlobal");
                    return;
                }
                if (stat.parts == null) stat.parts = new List<StatPart>();
                bool present = false;
                for (int i = 0; i < stat.parts.Count; i++)
                {
                    if (stat.parts[i] is StatPart_Satisfaction) present = true;
                }
                if (!present) stat.parts.Add(new StatPart_Satisfaction());
                Log.Message("[DSH-ECONOMY] 满意度速度部件已挂载=" + present + " 部件总数=" + stat.parts.Count);
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 挂载满意度速度部件失败：" + e.Message);
            }
        }

        private static void AttachPatientRestNode()
        {
            ThinkTreeDef tree = DefDatabase<ThinkTreeDef>.GetNamedSilentFail("Humanlike");
            Verse.AI.ThinkNode_Priority root = tree != null ? tree.thinkRoot as Verse.AI.ThinkNode_Priority : null;
            if (root == null || root.subNodes == null) { Log.Warning("[DSH-ECONOMY] 未找到 Humanlike 思维树，病人躺床未生效"); return; }

            for (int i = 0; i < root.subNodes.Count; i++)
            {
                if (root.subNodes[i] is JobGiver_EconomyPatientRest) return;
            }
            root.subNodes.Insert(1, new JobGiver_EconomyPatientRest());
            Log.Message("[DSH-ECONOMY] 病人优先躺床：已注入 Humanlike 思维树（第 2 位）");
        }

        static EconomyBootstrap()
        {
            ReflectionAudit.Begin();
            AttachItemTags();
            AttachPawnTab();
            AttachStorageRestockTab();
            EnsureIndoorStatPart();
            EnsureSatisfactionStatPart();
            RimHudIntegration.TryApply();
            EconomyPreceptForcer.InjectMemeRequirements();
            EconomyRegistry.Rebuild();
        EconomyRecipeTuning.Apply();
        try { EconomyDepreciation.EnsureComps(); } catch (System.Exception ex) { Log.Warning("[DSH-ECONOMY] 折旧组件注入失败：" + ex.Message); }
        try { AttachPatientRestNode(); } catch (System.Exception ex) { Log.Warning("[DSH-ECONOMY] 病人优先躺床注入失败：" + ex.Message); }
            EconomySilverGraphic.Apply();
            try
            {
                if (EconomyHarmonyProbe.HarmonyLoaded())
                {
                    EconomyMedicalPatcher.Apply();
                    EconomyTagPatcher.Apply();
                }
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] Harmony 初始化异常，已跳过：" + e.Message);
            }
            Log.Message(EconomyRegistry.Describe());
            bool hasButton = DefDatabase<MainButtonDef>.GetNamedSilentFail("DshEconomy") != null;
            int tagged = 0;
            List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef d = allDefs[i];
                if (d.comps == null) continue;
                for (int j = 0; j < d.comps.Count; j++)
                {
                    if (d.comps[j] is CompProperties_ItemTag)
                    {
                        tagged++;
                        break;
                    }
                }
            }
            int cookSkill = 0;
            List<RecipeDef> recipes = DefDatabase<RecipeDef>.AllDefsListForReading;
            for (int i = 0; i < recipes.Count; i++)
            {
                if (recipes[i].workSkill == SkillDefOf.Cooking) cookSkill++;
            }

            int mealQuality = 0;
            for (int i = 0; i < allDefs.Count; i++)
            {
                ThingDef d = allDefs[i];
                if (d.comps == null) continue;
                for (int j = 0; j < d.comps.Count; j++)
                {
                    if (d.comps[j].compClass == typeof(CompQuality))
                    {
                        mealQuality++;
                        break;
                    }
                }
            }

            ThingDef mineDef = DefDatabase<ThingDef>.GetNamedSilentFail("DshEco_SilverMine");
            ThingDef benchDef = DefDatabase<ThingDef>.GetNamedSilentFail("DshEco_SilverBench");
            int mineRecipes = mineDef != null && mineDef.AllRecipes != null ? mineDef.AllRecipes.Count : -1;
            int benchRecipes = benchDef != null && benchDef.AllRecipes != null ? benchDef.AllRecipes.Count : -1;

            Log.Message("[DSH-ECONOMY] 主按钮已加载=" + hasButton
                + " 分类=" + EconomyRegistry.Categories.Count
                + " 物品=" + EconomyRegistry.CoveredCount
                + " 带标签物品定义=" + tagged
                + " 烹饪配方带技能=" + cookSkill
                + " 带品质定义=" + mealQuality
                + " 矿场配方=" + mineRecipes
                + " 加工台配方=" + benchRecipes
                + " 货币=" + (EconomyRegistry.PrimaryCurrency != null ? EconomyRegistry.PrimaryCurrency.defName : "无"));
            ReflectionAudit.Report();
        }
    }

    public static class DshEconomyDebug
    {
        [DebugAction("DSH经济", "打印注册表摘要", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PrintSummary()
        {
            Log.Message(EconomyRegistry.Describe());
            ThingDef currency = EconomyRegistry.PrimaryCurrency;
            Log.Message("[DSH经济] 主货币：" + (currency != null ? currency.defName : "未找到"));
        }

        [DebugAction("DSH经济", "打印未归类物品", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PrintUncovered()
        {
            StringBuilder sb = new StringBuilder();
            int n = 0;
            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def == null) continue;
                if (def.category != ThingCategory.Item) continue;
                if (EconomyRegistry.CategoryOf(def) != null) continue;
                n++;
                if (n <= 60) sb.Append(def.defName).Append(' ');
            }
            Log.Message("[DSH经济] 未归类 " + n + " 种：" + sb);
        }

        [DebugAction("DSH经济", "运行自检", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RunSelfTestAction()
        {
            RunSelfTest(Find.CurrentMap);
        }

        public static void RunSelfTest(Map map)
        {
            StringBuilder sb = new StringBuilder();
            int fails = 0;

            if (!EconomyRegistry.Built)
            {
                fails++;
                sb.AppendLine("FAIL 注册表未构建");
            }

            if (EconomyRegistry.PrimaryCurrency == null)
            {
                fails++;
                sb.AppendLine("FAIL 未找到货币物品");
            }

            int covered = EconomyRegistry.CoveredCount;
            if (covered < 100)
            {
                fails++;
                sb.AppendLine("FAIL 纳入物品过少：" + covered);
            }

            // 价格必须随公共化单调不增
            if (map != null)
            {
                MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
                ThingDef probe = FindProbeItem();
                EconomyCategoryDef cat = probe != null ? EconomyRegistry.CategoryOf(probe) : null;
                if (comp != null && cat != null)
                {
                    float original = comp.GetPublicization(cat);
                    float prev = float.MaxValue;
                    for (float p = 0f; p <= 100f; p += 25f)
                    {
                        comp.SetPublicization(cat, p);
                        float price = EconomyPrice.UnitPrice(probe, map);
                        if (price > prev + 0.0001f)
                        {
                            fails++;
                            sb.AppendLine("FAIL 价格未随公共化下降：p=" + p + " price=" + price + " prev=" + prev);
                            break;
                        }
                        prev = price;
                    }
                    comp.SetPublicization(cat, original);
                }
                else
                {
                    fails++;
                    sb.AppendLine("FAIL 找不到可用于测试的物品或分类");
                }
            }

            sb.AppendLine("纳入 " + EconomyRegistry.CoveredCount + " 种，分类 " + EconomyRegistry.Categories.Count + " 个");
            if (fails == 0) Log.Message("[DSH经济] 自检通过\n" + sb);
            else Log.Error("[DSH经济] 自检失败 " + fails + " 项\n" + sb);
        }

        private static ThingDef FindProbeItem()
        {
            for (int i = 0; i < EconomyRegistry.Categories.Count; i++)
            {
                ThingDef t = EconomyRegistry.SampleItem(EconomyRegistry.Categories[i]);
                if (t != null) return t;
            }
            return null;
        }

        // ───────── 开发者工具：钱、工资、满意度、状态 ─────────

        [DebugAction("DSH经济", "国库 +1000 银", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AddTreasury()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            EconomyBank.GiveColonySilver(map, 1000, map.Center);
            Log.Message("[DSH经济] 国库 +1000，现有 " + EconomyBank.ColonySilver(map) + " 银");
        }

        [DebugAction("DSH经济", "全体私银 +100（每人）", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AddPersonalSilver()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            int n = 0;
            for (int i = 0; i < colonists.Count; i++)
            {
                n += EconomyBank.SplitSilverToPrivate(map, colonists[i], 100);
            }
            Log.Message("[DSH经济] 发放私银 " + n + " 银给 " + colonists.Count + " 人");
        }

        [DebugAction("DSH经济", "欠薪：开启（按设定次数自动欠）", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void StartArrears()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            MapEconomyComponent c = map.GetComponent<MapEconomyComponent>();
            if (c == null) return;
            c.arrearsAutoTimes = Mathf.Max(1, c.arrearsAutoTimes);
            c.arrearsEnabled = true;
            c.arrearsRemaining = c.arrearsAutoTimes;
            Log.Message("[DSH经济] 欠薪开启：接下来 " + c.arrearsRemaining + " 期不发工资");
        }

        [DebugAction("DSH经济", "欠薪：关闭并清零计数", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ClearArrears()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            MapEconomyComponent c = map.GetComponent<MapEconomyComponent>();
            if (c == null) return;
            c.arrearsEnabled = false;
            c.arrearsRemaining = 0;
            c.arrearsCount = 0;
            EconomySatisfaction.RecomputeAll(map);
            Log.Message("[DSH经济] 欠薪状态已清零");
        }

        [DebugAction("DSH经济", "欠薪：自动次数 +1", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ArrearsTimesUp()
        {
            MapEconomyComponent c = Find.CurrentMap?.GetComponent<MapEconomyComponent>();
            if (c == null) return;
            c.arrearsAutoTimes = Mathf.Clamp(c.arrearsAutoTimes + 1, 1, 50);
            Log.Message("[DSH经济] 欠薪自动次数 = " + c.arrearsAutoTimes);
        }

        [DebugAction("DSH经济", "欠薪：自动次数 -1", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ArrearsTimesDown()
        {
            MapEconomyComponent c = Find.CurrentMap?.GetComponent<MapEconomyComponent>();
            if (c == null) return;
            c.arrearsAutoTimes = Mathf.Clamp(c.arrearsAutoTimes - 1, 1, 50);
            Log.Message("[DSH经济] 欠薪自动次数 = " + c.arrearsAutoTimes);
        }


        [DebugAction("DSH经济", "健康：营养不良（重度）", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AddMalnutrition()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("Malnutrition");
            if (def == null) { Log.Warning("[DSH经济] 找不到 Malnutrition"); return; }
            List<Pawn> cs = map.mapPawns.FreeColonists;
            for (int i = 0; i < cs.Count; i++)
            {
                Hediff h = HediffMaker.MakeHediff(def, cs[i]);
                h.Severity = 0.6f;
                cs[i].health.AddHediff(h);
            }
            EconomySatisfaction.RecomputeAll(map);
            Log.Message("[DSH经济] 已给 " + cs.Count + " 人加上重度营养不良");
        }

        [DebugAction("DSH经济", "健康：极度疲劳", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AddExhaustion()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            HediffDef def = DefDatabase<HediffDef>.GetNamedSilentFail("Exhaustion")
                ?? DefDatabase<HediffDef>.GetNamedSilentFail("ExtremeFatigue")
                ?? DefDatabase<HediffDef>.GetNamedSilentFail("Fatigue");
            if (def == null) { Log.Warning("[DSH经济] 找不到疲劳 HediffDef（Exhaustion/ExtremeFatigue/Fatigue）"); return; }
            List<Pawn> cs = map.mapPawns.FreeColonists;
            for (int i = 0; i < cs.Count; i++)
            {
                Hediff h = HediffMaker.MakeHediff(def, cs[i]);
                h.Severity = 0.8f;
                cs[i].health.AddHediff(h);
            }
            EconomySatisfaction.RecomputeAll(map);
            Log.Message("[DSH经济] 已给 " + cs.Count + " 人加上极度疲劳（" + def.defName + "）");
        }

        [DebugAction("DSH经济", "健康：添加 3 处外伤", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AddInjuries()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            HediffDef cut = DefDatabase<HediffDef>.GetNamedSilentFail("Cut")
                ?? DefDatabase<HediffDef>.GetNamedSilentFail("Bruise");
            if (cut == null) { Log.Warning("[DSH经济] 找不到 Cut/Bruise"); return; }
            List<Pawn> cs = map.mapPawns.FreeColonists;
            for (int i = 0; i < cs.Count; i++)
            {
                for (int k = 0; k < 3; k++)
                {
                    Hediff h = HediffMaker.MakeHediff(cut, cs[i]);
                    h.Severity = 8f;
                    cs[i].health.AddHediff(h);
                }
            }
            EconomySatisfaction.RecomputeAll(map);
            Log.Message("[DSH经济] 已给 " + cs.Count + " 人各加 3 处外伤");
        }

        [DebugAction("DSH经济", "健康：清除上述状态", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ClearHealthStates()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            List<Pawn> cs = map.mapPawns.FreeColonists;
            for (int i = 0; i < cs.Count; i++)
            {
                Pawn p = cs[i];
                List<Hediff> hs = new List<Hediff>(p.health.hediffSet.hediffs);
                for (int k = 0; k < hs.Count; k++)
                {
                    Hediff h = hs[k];
                    if (h == null || h.def == null) continue;
                    string n = h.def.defName;
                    if (n == "Malnutrition" || n == "Exhaustion" || n == "ExtremeFatigue" || n == "Fatigue"
                        || h is Hediff_Injury)
                        p.health.RemoveHediff(h);
                }
            }
            EconomySatisfaction.RecomputeAll(map);
            Log.Message("[DSH经济] 已清除营养不良/疲劳/外伤");
        }

        [DebugAction("DSH经济", "贫富差距：打印统计", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PrintWealthGap()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            List<Pawn> cs = map.mapPawns.FreeColonists;
            for (int i = 0; i < cs.Count; i++)
                Log.Message("[DSH经济] 贫富差距 " + cs[i].LabelShort + "：" + EconomyWealthGap.Describe(cs[i]));
        }

        [DebugAction("DSH经济", "立即发薪", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PayNow()
        {
            Map map = Find.CurrentMap;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp == null) return;
            int paid = comp.PayWagesNow();
            Log.Message("[DSH经济] 立即发薪：" + paid + " 银");
        }

        [DebugAction("DSH经济", "重算全员满意度", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RecomputeSatisfaction()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            EconomySatisfaction.RecomputeAll(map);
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++) Need_Satisfaction.Sync(colonists[i]);
            Log.Message("[DSH经济] 满意度已重算");
        }

        [DebugAction("DSH经济", "打印经济状态", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpState()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            WagePolicy w = comp != null ? comp.wages : null;
            var sb = new StringBuilder();
            sb.AppendLine("国库 " + EconomyBank.ColonySilver(map) + " 银   银价指数 "
                + (comp != null ? comp.SilverIndex.ToString("0.000") : "-"));
            if (w != null)
            {
                sb.AppendLine("全局：日价值 " + w.laborDayValue.ToString("0") + " / 比例 "
                    + (w.wageShare * 100f).ToString("0") + "% / 上限 " + w.wageCapPerDay.ToString("0")
                    + " / 标准工时 " + w.standardWorkday.ToString("0.0")
                    + " / 加班 " + (w.overtimeEnabled ? "开 " + w.overtimeMultiplier.ToString("0.0") + "x" : "关")
                    + " / 个人覆盖 " + (w.overrides != null ? w.overrides.Count : 0) + " 人");
            }
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                float lab = EconomyLedger.GetLabor(p);
                float hours = comp != null ? comp.DailyWorkHours(p) : 0f;
                string over = "";
                if (w != null && w.HasOverride(p))
                {
                    over = "  [个人设置]";
                }
                sb.AppendLine(p.LabelShort + "  劳动 " + (lab / GenDate.TicksPerDay).ToString("0.00")
                    + " 人日  今日 " + hours.ToString("0.0") + " 小时  满意度 "
                    + EconomyLedger.GetSatisfaction(p).ToString("0") + over);
            }
            Log.Message("[DSH经济] 状态\n" + sb);
        }

        [DebugAction("DSH经济", "全体今日劳动 +2 小时", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AddDailyHours2()
        {
            AddDailyHours(2f);
        }

        [DebugAction("DSH经济", "全体今日劳动 +8 小时", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void AddDailyHours8()
        {
            AddDailyHours(8f);
        }

        private static void AddDailyHours(float hours)
        {
            Map map = Find.CurrentMap;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                comp.AddDailyWork(colonists[i], hours * 2500f);
            }
            EconomySatisfaction.RecomputeAll(map);
            Log.Message("[DSH经济] 全体今日劳动 +" + hours.ToString("0") + " 小时，已重算满意度");
        }

        [DebugAction("DSH经济", "清空今日劳动小时", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ClearDailyHours()
        {
            Map map = Find.CurrentMap;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp == null) return;
            comp.ClearDailyWork();
            EconomySatisfaction.RecomputeAll(map);
            Log.Message("[DSH经济] 今日劳动小时已清零");
        }

        [DebugAction("DSH经济", "清空满意度事件", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ClearSatisfactionEvents()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            EconomySatisfactionEvents.Clear();
            EconomySatisfaction.RecomputeAll(map);
            Log.Message("[DSH经济] 满意度事件已清空");
        }

        [DebugAction("DSH经济", "强制罢工（3 人）", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceStrike()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            int n = EconomyUnrest.ForceStrike(map, 10f);
            Log.Message("[DSH经济] 强制罢工 " + n + " 人");
        }

        [DebugAction("DSH经济", "强制暴动（3 人）", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceRiot()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            int n = EconomyUnrest.ForceRiot(map, 10f);
            Log.Message("[DSH经济] 强制暴动 " + n + " 人");
        }

        [DebugAction("DSH经济", "强制革命（3 人）", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceRevolution()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            int n = EconomyUnrest.ForceRevolution(map, 10f);
            Log.Message("[DSH经济] 强制革命 " + n + " 人");
        }

        [DebugAction("DSH经济", "清空动乱状态", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ClearUnrest()
        {
            EconomyUnrest.ClearRuntimeState();
            Log.Message("[DSH经济] 动乱运行状态已清空");
        }

        [DebugAction("DSH经济", "导出参考贴图", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpTextures()
        {
            TextureDump.Force();
        }
    }
}
