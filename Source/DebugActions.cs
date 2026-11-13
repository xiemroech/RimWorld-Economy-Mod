using System;
using System.Collections.Generic;
using System.Text;
using LudeonTK;
using RimWorld;
using Verse.AI;
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
            DevLog.Msg("[DSH-ECONOMY] 已为 " + added + " 个人形种族启用「经济」页签");
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
            DevLog.Msg("[DSH-ECONOMY] 已为 " + added + " 种存储建筑启用「补货」页签");
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
                DevLog.Msg("[DSH-ECONOMY] 屋内惩罚部件已挂载=" + present + " 部件总数=" + stat.parts.Count);
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
                DevLog.Msg("[DSH-ECONOMY] 满意度速度部件已挂载=" + present + " 部件总数=" + stat.parts.Count);
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
            DevLog.Msg("[DSH-ECONOMY] 病人优先躺床：已注入 Humanlike 思维树（第 2 位）");
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
            DevLog.Msg(EconomyRegistry.Describe());
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

            DevLog.Msg("[DSH-ECONOMY] 构建标记 = 2026-11-11-C DSHSTAMPX（日志彻底静音）");
            try
            {
                UnityEngine.Texture2D ownTex = Verse.ContentFinder<UnityEngine.Texture2D>.Get("UI/Issues/DshEco_Ownership", false);
                DevLog.Msg("[DSH-ECONOMY] 所有权贴图=" + (ownTex != null)
                    + (ownTex != null ? (" 尺寸=" + ownTex.width + "x" + ownTex.height) : "（载入失败，路径 UI/Issues/DshEco_Ownership）"));
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 所有权贴图探测异常：" + e.Message);
            }
            DevLog.Msg("[DSH-ECONOMY] 主按钮已加载=" + hasButton
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
            DevLog.Msg(EconomyRegistry.Describe());
            ThingDef currency = EconomyRegistry.PrimaryCurrency;
            DevLog.Msg("[DSH经济] 主货币：" + (currency != null ? currency.defName : "未找到"));
        }

                [DebugAction("DSH经济", "私产诊断", actionType = DebugActionType.Action,
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        private static void DumpPrivateOwnership()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            List<Pawn> crew = map.mapPawns.FreeColonists;
            for (int i = 0; i < crew.Count; i++)
            {
                Pawn p = crew[i];
                DevLog.Msg("[DSH-ECONOMY] 私产诊断 " + p.LabelShort
                    + "：私有银=" + EconomyBank.PrivateSilver(map, p)
                    + " 私有物=" + EconomyPrivate.OwnedItems(p, map).Count + " 件");

                if (p.inventory == null || p.inventory.innerContainer == null) continue;
                IList<Thing> inner = p.inventory.innerContainer;
                for (int j = 0; j < inner.Count; j++)
                {
                    Thing t = inner[j];
                    if (t.def != ThingDefOf.Silver) continue;
                    DevLog.Msg("    背包银 " + t.stackCount
                        + " 组件=" + (EconomyPrivate.TagOf(t) != null)
                        + " 归属=" + (EconomyPrivate.OwnerOf(t) == null
                            ? "无" : EconomyPrivate.OwnerOf(t).LabelShort));
                }
            }
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
            DevLog.Msg("[DSH经济] 未归类 " + n + " 种：" + sb);
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
            if (fails == 0) DevLog.Msg("[DSH经济] 自检通过\n" + sb);
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
            DevLog.Msg("[DSH经济] 国库 +1000，现有 " + EconomyBank.ColonySilver(map) + " 银");
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
            DevLog.Msg("[DSH经济] 发放私银 " + n + " 银给 " + colonists.Count + " 人");
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
            DevLog.Msg("[DSH经济] 欠薪开启：接下来 " + c.arrearsRemaining + " 期不发工资");
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
            DevLog.Msg("[DSH经济] 欠薪状态已清零");
        }

        [DebugAction("DSH经济", "欠薪：自动次数 +1", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ArrearsTimesUp()
        {
            MapEconomyComponent c = Find.CurrentMap?.GetComponent<MapEconomyComponent>();
            if (c == null) return;
            c.arrearsAutoTimes = Mathf.Clamp(c.arrearsAutoTimes + 1, 1, 50);
            DevLog.Msg("[DSH经济] 欠薪自动次数 = " + c.arrearsAutoTimes);
        }

        [DebugAction("DSH经济", "欠薪：自动次数 -1", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ArrearsTimesDown()
        {
            MapEconomyComponent c = Find.CurrentMap?.GetComponent<MapEconomyComponent>();
            if (c == null) return;
            c.arrearsAutoTimes = Mathf.Clamp(c.arrearsAutoTimes - 1, 1, 50);
            DevLog.Msg("[DSH经济] 欠薪自动次数 = " + c.arrearsAutoTimes);
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
            DevLog.Msg("[DSH经济] 已给 " + cs.Count + " 人加上重度营养不良");
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
            DevLog.Msg("[DSH经济] 已给 " + cs.Count + " 人加上极度疲劳（" + def.defName + "）");
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
            DevLog.Msg("[DSH经济] 已给 " + cs.Count + " 人各加 3 处外伤");
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
            DevLog.Msg("[DSH经济] 已清除营养不良/疲劳/外伤");
        }

        [DebugAction("DSH经济", "贫富差距：打印统计", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PrintWealthGap()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            List<Pawn> cs = map.mapPawns.FreeColonists;
            for (int i = 0; i < cs.Count; i++)
                DevLog.Msg("[DSH经济] 贫富差距 " + cs[i].LabelShort + "：" + EconomyWealthGap.Describe(cs[i]));
        }

        [DebugAction("DSH经济", "立即发薪", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void PayNow()
        {
            Map map = Find.CurrentMap;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp == null) return;
            int paid = comp.PayWagesNow();
            DevLog.Msg("[DSH经济] 立即发薪：" + paid + " 银");
        }

        [DebugAction("DSH经济", "重算全员满意度", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void RecomputeSatisfaction()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            EconomySatisfaction.RecomputeAll(map);
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++) Need_Satisfaction.Sync(colonists[i]);
            DevLog.Msg("[DSH经济] 满意度已重算");
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
            DevLog.Msg("[DSH经济] 状态\n" + sb);
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
            DevLog.Msg("[DSH经济] 全体今日劳动 +" + hours.ToString("0") + " 小时，已重算满意度");
        }

        [DebugAction("DSH经济", "清空今日劳动小时", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ClearDailyHours()
        {
            Map map = Find.CurrentMap;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;
            if (comp == null) return;
            comp.ClearDailyWork();
            EconomySatisfaction.RecomputeAll(map);
            DevLog.Msg("[DSH经济] 今日劳动小时已清零");
        }

        [DebugAction("DSH经济", "清空满意度事件", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ClearSatisfactionEvents()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            EconomySatisfactionEvents.Clear();
            EconomySatisfaction.RecomputeAll(map);
            DevLog.Msg("[DSH经济] 满意度事件已清空");
        }

        [DebugAction("DSH经济", "强制罢工（3 人）", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceStrike()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            int n = EconomyUnrest.ForceStrike(map, 10f);
            DevLog.Msg("[DSH经济] 强制罢工 " + n + " 人");
        }

        [DebugAction("DSH经济", "强制暴动（3 人）", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceRiot()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            int n = EconomyUnrest.ForceRiot(map, 10f);
            DevLog.Msg("[DSH经济] 强制暴动 " + n + " 人");
        }

        [DebugAction("DSH经济", "强制革命（3 人）", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ForceRevolution()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            int n = EconomyUnrest.ForceRevolution(map, 10f);
            DevLog.Msg("[DSH经济] 强制革命 " + n + " 人");
        }

        [DebugAction("DSH经济", "清空动乱状态", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void ClearUnrest()
        {
            EconomyUnrest.ClearRuntimeState();
            DevLog.Msg("[DSH经济] 动乱运行状态已清空");
        }

        /// <summary>把类型里"找原料/能不能开工"相关的方法签名打进日志（用于直接调用原版判定）。</summary>
        private static void DumpBillMethods(System.Type t, string label)
        {
            Log.Message("[MINE] ---- " + label + " 候选方法 ----");
            foreach (System.Reflection.MethodInfo mi in t.GetMethods(
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static
                | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
            {
                string n = mi.Name;
                if (n.IndexOf("Ingredient", System.StringComparison.Ordinal) < 0
                    && n.IndexOf("DoNow", System.StringComparison.Ordinal) < 0) continue;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[MINE] ").Append(label).Append('.').Append(n).Append('(');
                System.Reflection.ParameterInfo[] ps = mi.GetParameters();
                for (int i = 0; i < ps.Length; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append(ps[i].ParameterType.ToString()).Append(' ').Append(ps[i].Name);
                }
                sb.Append(") 返回=").Append(mi.ReturnType.Name);
                Log.Message(sb.ToString());
            }
        }

        /// <summary>劳动体检：打印"记账这一层到底有没有跑"，用来判断工资为 0 是不是工时没记进来。</summary>
        [DebugAction("DSH经济", "劳动体检", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpLabor()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            Log.Message("[LABOR-CK] ===== 劳动体检开始，tick=" + Find.TickManager.TicksGame + " =====");
            Log.Message("[LABOR-CK] 会话开启累计=" + EconomyLaborSessions.OpenedTotal
                + " 结算累计=" + EconomyLaborSessions.ClosedTotal
                + " Active=" + EconomyLaborSessions.Active);
            Log.Message("[LABOR-CK] 最后一次开启：" + EconomyLaborSessions.LastOpenInfo);
            Log.Message("[LABOR-CK] 最后一次结算：" + EconomyLaborSessions.LastClosedInfo);
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            List<Pawn> crew = map.mapPawns.FreeColonists;
            for (int i = 0; i < crew.Count; i++)
            {
                Pawn p = crew[i];
                if (p == null) continue;
                int start = EconomyLaborSessions.OpenStartTick(p);
                string line = "[LABOR-CK] " + p.LabelShort
                    + " 当前活=" + (p.CurJobDef != null ? p.CurJobDef.defName : "无")
                    + " 算在干活=" + EconomyWages.IsWorking(p)
                    + " 会话=" + (start >= 0 ? ("进行中 已跑 " + (Find.TickManager.TicksGame - start) + "tick") : "无")
                    + " 账本劳动=" + EconomyLedger.GetLabor(p).ToString("0")
                    + " 账本原始=" + EconomyLedger.GetRawLabor(p).ToString("0")
                    + " 今日工时=" + (comp != null ? comp.DailyWorkHours(p).ToString("0.00") : "?") + "h";
                Log.Message(line);
            }
            // 财富口径体检：面板上"私有财富" vs 贫富差距用的最大值，必须是同一套数
            try
            {
                TaxPopulation.ColonyStats st = TaxPopulation.Cached(map, 1);
                Log.Message("[WEALTH-CK] 统计口径：最富=" + st.wealthMax.ToString("0") + " 最穷=" + st.wealthMin.ToString("0")
                    + " 平均=" + st.wealthAvg.ToString("0") + " 基尼=" + st.wealthGini.ToString("0.00"));
                for (int i = 0; i < crew.Count; i++)
                {
                    Pawn p = crew[i];
                    if (p == null) continue;
                    int silver = EconomyBank.PrivateSilver(map, p);
                    float w = TaxPopulation.PawnPrivateWealth(p, silver);
                    Log.Message("[WEALTH-CK] " + p.LabelShort
                        + " 私银=" + silver
                        + " 私有物权重=" + (w - silver).ToString("0")
                        + " 合计=" + w.ToString("0")
                        + " 倍数=" + (w > 0.01f ? (st.wealthMax / w).ToString("0.0") : "-"));
                    // 明细：谁的财富异常，就把最贵的几件列出来，直接看是哪样东西被算进去了
                    try
                    {
                        float bagV = 0f, wornV = 0f, handV = 0f, storeV = 0f;
                        if (p.inventory != null && p.inventory.innerContainer != null)
                        {
                            foreach (Thing t in p.inventory.innerContainer)
                            {
                                if (t == null || t.Destroyed || t.def == RimWorld.ThingDefOf.Silver) continue;
                                bagV += t.MarketValue * t.stackCount;
                            }
                        }
                        if (p.apparel != null)
                        {
                            foreach (Apparel a in p.apparel.WornApparel)
                            {
                                if (a == null || a.Destroyed) continue;
                                wornV += a.MarketValue * a.stackCount;
                            }
                        }
                        if (p.carryTracker != null && p.carryTracker.CarriedThing != null)
                            handV = p.carryTracker.CarriedThing.MarketValue * p.carryTracker.CarriedThing.stackCount;
                        // 私有仓库（归他名下的存储组）里的东西：用来对照"名义上有多少、实际算了多少"
                        List<SlotGroup> gs = map.haulDestinationManager.AllGroupsListForReading;
                        for (int gi = 0; gi < gs.Count; gi++)
                        {
                            SlotGroup g = gs[gi];
                            if (g == null || g.HeldThings == null) continue;
                            if (EconomyPrivateStorageBehavior.OwnerOfGroup(g) != p) continue;
                            foreach (Thing t in g.HeldThings)
                            {
                                if (t == null || t.Destroyed || t.def == RimWorld.ThingDefOf.Silver) continue;
                                storeV += t.MarketValue * t.stackCount;
                            }
                        }
                        Log.Message("[WEALTH-CK]   " + p.LabelShort
                            + " 背包=" + bagV.ToString("0") + " 穿戴=" + wornV.ToString("0")
                            + " 手持=" + handV.ToString("0") + " 私有仓库=" + storeV.ToString("0")
                            + " 建筑=" + TaxPopulation.BuildingsValueOf(p, map).ToString("0")
                            + " → 随身合计=" + (bagV + wornV + handV).ToString("0"));
                        // 最贵的几件（含穿戴），用来判断"首富"是定价离谱还是真有一件好东西
                        try
                        {
                            List<Thing> owned = EconomyPrivate.OwnedItems(p, map);
                            List<Thing> top = new List<Thing>();
                            for (int k = 0; k < owned.Count; k++)
                            {
                                Thing t = owned[k];
                                if (t == null || t.Destroyed || t.def == RimWorld.ThingDefOf.Silver) continue;
                                top.Add(t);
                            }
                            top.Sort((a, b) => (b.MarketValue * b.stackCount).CompareTo(a.MarketValue * a.stackCount));
                            System.Text.StringBuilder sb = new System.Text.StringBuilder();
                            sb.Append("[WEALTH-CK]     ").Append(p.LabelShort).Append(" 共 ").Append(top.Count).Append(" 件，最贵：");
                            for (int k = 0; k < top.Count && k < 3; k++)
                            {
                                if (k > 0) sb.Append(" / ");
                                sb.Append(top[k].def.defName).Append('×').Append(top[k].stackCount)
                                  .Append('=').Append((top[k].MarketValue * top[k].stackCount).ToString("0"));
                            }
                            Log.Message(sb.ToString());
                        }
                        catch (System.Exception e3)
                        {
                            Log.Warning("[WEALTH-CK] 最贵明细失败：" + e3.Message);
                        }
                    }
                    catch (System.Exception e2)
                    {
                        Log.Warning("[WEALTH-CK] 明细失败：" + e2.Message);
                    }
                }
            }
            catch (System.Exception e)
            {
                Log.Warning("[WEALTH-CK] 财富体检失败：" + e.Message);
            }
            Log.Message("[LABOR-CK] ===== 结束 =====");
            Messages.Message("劳动体检已打印到 Player.log（搜 [LABOR-CK]）", MessageTypeDefOf.NeutralEvent, false);
        }

        [DebugAction("DSH经济", "导出参考贴图", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpTextures()
        {
            TextureDump.Force();
        }

        /// <summary>银矿体检：把"为什么没人来干"的每一道关卡都打一行，用来定位账单卡在哪。</summary>
        [DebugAction("DSH经济", "银矿体检", actionType = DebugActionType.Action, allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void CheckSilverMine()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;

            WorkGiver_DoBill doBill = null;
            try
            {
                // 全部 WorkGiver_DoBill 都会命中（医疗手术那个也是它的子类），
                // 必须挑"制作"工作类型的那一个，否则测的根本不是账单派活。
                List<WorkGiverDef> all = new List<WorkGiverDef>();
                foreach (WorkGiverDef def in DefDatabase<WorkGiverDef>.AllDefs)
                {
                    if (def == null || def.Worker == null) continue;
                    if (!(def.Worker is WorkGiver_DoBill)) continue;
                    all.Add(def);
                    string wt = def.workType != null ? def.workType.defName : "无";
                    Log.Message("[MINE] 候选 DoBill：" + def.defName + " 工作类型=" + wt);
                    if (doBill == null && def.defName == "DoBill") doBill = def.Worker as WorkGiver_DoBill;
                }
                if (doBill == null && all.Count > 0) doBill = all[0].Worker as WorkGiver_DoBill;
            }
            catch { }
            Log.Message("[MINE] 实际用来测的 DoBill=" + (doBill == null ? "没找到" : doBill.def.defName));

            // 把原版"能不能开工"这条链上的方法签名打出来，
            // 好让我们直接调用真正的原版判定，而不是自己猜。
            try
            {
                DumpBillMethods(typeof(Bill), "Bill");
                DumpBillMethods(typeof(WorkGiver_DoBill), "WorkGiver_DoBill");
            }
            catch (System.Exception e)
            {
                Log.Warning("[MINE] 方法转储失败：" + e.Message);
            }

            int found = 0;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b == null || b.def == null) continue;
                if (b.def.defName != "DshEco_SilverMine" && b.def.defName != "DshEco_SilverBench") continue;
                found++;
                Log.Message("[MINE] ==== " + b.def.defName + " @ " + b.Position
                    + " 已生成=" + b.Spawned + " 损坏=" + b.Destroyed);

                IBillGiver giver = b as IBillGiver;
                if (giver == null || giver.BillStack == null)
                {
                    Log.Message("[MINE] 不是账单建筑（IBillGiver 缺失）");
                    continue;
                }
                Log.Message("[MINE] 账单数=" + giver.BillStack.Count
                    + " 交互格=" + b.InteractionCell
                    + " 有交互格=" + b.def.hasInteractionCell
                    + " 断电=" + (b.TryGetComp<CompPowerTrader>() != null && !b.TryGetComp<CompPowerTrader>().PowerOn)
                    + " 故障=" + EconomyDepreciation.IsBroken(b));

                foreach (Bill bill in giver.BillStack)
                {
                    if (bill == null || bill.recipe == null) continue;
                    string allow = "?";
                    try
                    {
                        int ok = 0;
                        foreach (Pawn p in map.mapPawns.FreeColonists)
                            if (!p.Dead && !p.Downed && bill.PawnAllowedToStartAnew(p)) ok++;
                        allow = ok + "/" + map.mapPawns.FreeColonists.Count;
                    }
                    catch { }
                    Log.Message("[MINE] 账单 " + bill.recipe.defName
                        + " 暂停=" + bill.suspended
                        + " 该做了=" + bill.ShouldDoNow()
                        + " 工时可做=" + (bill.recipe.workAmount > 0f)
                        + " 技能=" + (bill.recipe.workSkill != null ? bill.recipe.workSkill.defName : "无")
                        + " 允许开工人数=" + allow
                        + " 研究已好=" + (bill.recipe.researchPrerequisite == null || bill.recipe.researchPrerequisite.IsFinished));
                }

                foreach (Pawn p in map.mapPawns.FreeColonists)
                {
                    if (p == null || p.Dead || p.Downed) continue;
                    IntVec3 cell = b.InteractionCell;
                    bool standable = cell.Standable(map);
                    bool reach1 = false, reach2 = false, reach3 = false;
                    try { reach1 = map.reachability.CanReach(p.Position, cell, PathEndMode.OnCell, TraverseMode.PassDoors, Danger.Some); } catch { }
                    try { reach2 = p.CanReach(cell, PathEndMode.ClosestTouch, Danger.Some); } catch { }
                    try { reach3 = p.CanReach(b, PathEndMode.InteractionCell, Danger.Some); } catch { }
                    bool mineOk = false;
                    try { mineOk = p.workSettings != null && p.workSettings.WorkIsActive(WorkTypeDefOf.Mining); } catch { }
                    bool craftOk = false;
                    try { craftOk = p.workSettings != null && p.workSettings.WorkIsActive(WorkTypeDefOf.Crafting); } catch { }
                    string job = "无";
                    if (doBill != null)
                    {
                        try
                        {
                            Job j = doBill.JobOnThing(p, b, false);
                            job = j == null ? "空" : j.def.defName;
                        }
                        catch (System.Exception e) { job = "异常:" + e.GetType().Name; }
                    }
                    // 我们自己的拼单路线（原版找料 + 原版 DoBill 任务）
                    string mineJob = "?";
                    try
                    {
                        Job mj = null; // 自建派活器已删除，改走原版 WorkGiver_DoBill
                        mineJob = mj == null ? ("空（" + "-" + "）") : mj.def.defName;
                    }
                    catch (System.Exception e) { mineJob = "异常:" + e.GetType().Name; }
                    Log.Message("[MINE]   " + p.LabelShort
                        + " 格可站=" + standable
                        + " 路1=" + reach1 + " 路2=" + reach2 + " 路3=" + reach3
                        + " 采矿工作=" + mineOk
                        + " 制作工作=" + craftOk
                        + " DoBill=" + job
                        + " 我们的拼单=" + mineJob
                        + " 当前=" + (p.CurJobDef == null ? "无" : p.CurJobDef.defName));
                }
            }
            if (found == 0) Log.Message("[MINE] 本图没有银矿场/银加工台");
        }
    }
}



















