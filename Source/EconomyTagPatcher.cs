using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 防止带私有标签的物品与别人的物品合并堆叠。
    /// CanStackWith 负责"能不能叠"，TryAbsorbStack 负责"真的吸进去"，两个都要拦。
    /// 补丁按功能分组，单组异常不会拖垮其它组。
    /// </summary>
    public static class EconomyTagPatcher
    {
        public static bool Patched;

        public static void Apply()
        {
            if (Patched) return;

            Harmony harmony = new Harmony("dsh.economy.tags");

            string[] groupNames =
            {
                "防合并私有标签", "显示标签", "产权存储", "库存生成追踪",
                "搬运", "食物进食", "劳动工资", "市场购买",
                "营养膏机", "医疗CE", "满意度", "动乱战斗",
                "折旧维修", "贸易", "文化戒律", "军人"
            };
            bool[] groupOk = new bool[groupNames.Length];

            groupOk[0] = PatchStacking(harmony);
            groupOk[1] = PatchDisplay(harmony);
            groupOk[2] = PatchOwnershipStorage(harmony);
            groupOk[3] = PatchSpawnTracking(harmony);
            groupOk[4] = PatchHauling(harmony);
            groupOk[5] = PatchFoodAccess(harmony);
            groupOk[6] = PatchWorkAccounting(harmony);
            groupOk[7] = PatchMarketPurchase(harmony);
            groupOk[8] = PatchPasteDispenser(harmony);
            groupOk[9] = PatchMedicalCE(harmony);
            groupOk[10] = PatchSatisfaction(harmony);
            groupOk[11] = PatchRiotCombat(harmony);
            groupOk[12] = PatchDepreciationRepair(harmony);
            groupOk[13] = PatchTrade(harmony);
            groupOk[14] = PatchPrecepts(harmony);
            groupOk[15] = PatchMilitary(harmony);

            int succeeded = 0;
            List<string> failed = new List<string>();
            for (int i = 0; i < groupNames.Length; i++)
            {
                if (groupOk[i]) succeeded++;
                else failed.Add(groupNames[i]);
            }

            Log.Message("[DSH-ECONOMY] 私有物品防合并补丁已应用");

            string summary = "[DSH-ECONOMY] 补丁分组：成功 " + succeeded + "/" + groupNames.Length;
            if (failed.Count > 0) summary += "，失败分组：" + string.Join("、", failed.ToArray());
            Log.Message(summary);

            // 至少一组生效才置位，全军覆没时下次还能重试
            if (succeeded > 0) Patched = true;
        }

        // ── 分组：防合并 / 私有标签传递 ─────────────────────────────
        private static bool PatchStacking(Harmony harmony)
        {
            try
            {
                bool ok = false;

                MethodInfo canStack = AccessTools.Method(typeof(Thing), "CanStackWith", new[] { typeof(Thing) });
                if (canStack != null)
                {
                    try
                    {
                        harmony.Patch(canStack, postfix: new HarmonyMethod(AccessTools.Method(typeof(StackPatch), "Postfix")));
                        ok = true;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] 防合并补丁失败，已跳过：" + e.Message);
                    }
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 Thing.CanStackWith，跳过防合并");
                }

                MethodInfo absorb = AccessTools.Method(typeof(Thing), "TryAbsorbStack", new[] { typeof(Thing), typeof(bool) });
                if (absorb != null)
                {
                    harmony.Patch(absorb, prefix: new HarmonyMethod(AccessTools.Method(typeof(AbsorbPatch), "Prefix")));
                    ok = true;
                    try
                    {
                        harmony.Patch(absorb, postfix: new HarmonyMethod(AccessTools.Method(typeof(TagPreservePatch), "AbsorbPostfix")));
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] 合并保留标签补丁失败：" + e.Message);
                    }
                }

                MethodInfo split = AccessTools.Method(typeof(Thing), "SplitOff", new[] { typeof(int) });
                if (split != null)
                {
                    try
                    {
                        harmony.Patch(split, postfix: new HarmonyMethod(AccessTools.Method(typeof(TagPreservePatch), "SplitPostfix")));
                        Log.Message("[DSH-ECONOMY] 拆分保留私有标签：已挂载");
                        ok = true;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] 拆分保留标签补丁失败：" + e.Message);
                    }
                }

                return ok;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 防合并私有标签 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：显示 / 标签 ───────────────────────────────────────
        private static bool PatchDisplay(Harmony harmony)
        {
            try
            {
                bool ok = false;

                MethodInfo labelGetter = AccessTools.PropertyGetter(typeof(Thing), "Label");
                if (labelGetter != null)
                {
                    try
                    {
                        harmony.Patch(labelGetter, postfix: new HarmonyMethod(AccessTools.Method(typeof(PrivateLabelPatch), "Postfix")));
                        Log.Message("[DSH-ECONOMY] 私人物品名后标注物主：已挂载");
                        ok = true;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] 物品标注补丁失败：" + e.Message);
                    }
                }

                MethodInfo defGraphic = AccessTools.PropertyGetter(typeof(ThingDef), "graphic");
                if (defGraphic != null)
                {
                    harmony.Patch(defGraphic, postfix: new HarmonyMethod(AccessTools.Method(typeof(SilverGraphicPatch), "Postfix")));
                    Log.Message("[DSH-ECONOMY] 银锭世界贴图补丁（ThingDef）：已挂载");
                    ok = true;
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] ThingDef.graphic 不是属性，跳过");
                }

                MethodInfo thingGraphic = AccessTools.PropertyGetter(typeof(Thing), "Graphic");
                if (thingGraphic != null)
                {
                    harmony.Patch(thingGraphic, postfix: new HarmonyMethod(AccessTools.Method(typeof(SilverGraphicPatch), "ThingPostfix")));
                    Log.Message("[DSH-ECONOMY] 银锭世界贴图补丁（Thing）：已挂载");
                    ok = true;
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] Thing.Graphic 不是属性，跳过");
                }

                bool priceInspectPatched = false;
                try { priceInspectPatched = EconomyPriceInspect.Patch(harmony); }
                catch (System.Exception ex) { Log.Warning("[DSH-ECONOMY] 物品现价显示补丁失败：" + ex.Message); }
                Log.Message("[DSH-ECONOMY] 物品信息显示现价：" + (priceInspectPatched ? "已挂载" : "不可用"));
                if (priceInspectPatched) ok = true;

                bool priceDescPatched = false;
                try { priceDescPatched = EconomyPriceDescription.Patch(harmony); }
                catch (System.Exception ex) { Log.Warning("[DSH-ECONOMY] 描述栏现价补丁失败：" + ex.Message); }
                Log.Message("[DSH-ECONOMY] 描述面板显示现价：" + (priceDescPatched ? "已挂载" : "不可用"));
                if (priceDescPatched) ok = true;

                return ok;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 显示标签 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：产权 / 存储 ───────────────────────────────────────
        private static bool PatchOwnershipStorage(Harmony harmony)
        {
            try
            {
                bool ok = false;

                MethodInfo store = AccessTools.Method(typeof(StoreUtility), "TryFindBestBetterStoreCellFor");
                if (store != null)
                {
                    harmony.Patch(store, postfix: new HarmonyMethod(AccessTools.Method(typeof(StoreUtilityPrivatePatch), "Postfix")));
                    ok = true;
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 StoreUtility.TryFindBestBetterStoreCellFor，私人仓库未拦截");
                }

                MethodInfo zoneGizmos = AccessTools.Method(typeof(Zone), "GetGizmos");
                if (zoneGizmos != null)
                {
                    harmony.Patch(zoneGizmos, postfix: new HarmonyMethod(AccessTools.Method(typeof(ZoneOwnershipGizmoPatch), "Postfix")));
                    ok = true;
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 Zone.GetGizmos，堆放区所有权按钮不可用");
                }

                MethodInfo received = AccessTools.Method(typeof(Building_Storage), "Notify_ReceivedThing", new[] { typeof(Thing) });
                if (received != null)
                {
                    try
                    {
                        harmony.Patch(received, postfix: new HarmonyMethod(AccessTools.Method(typeof(StorageReceivedEjectPatch), "Postfix")));
                        Log.Message("[DSH-ECONOMY] 储物建筑收货归属检查：已挂载");
                        ok = true;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] 储物建筑收货检查失败：" + e.Message);
                    }
                }

                MethodInfo goodCell = AccessTools.Method(typeof(StoreUtility), "IsGoodStoreCell",
                    new[] { typeof(IntVec3), typeof(Map), typeof(Thing), typeof(Pawn), typeof(Faction) });
                if (goodCell != null)
                {
                    harmony.Patch(goodCell, prefix: new HarmonyMethod(AccessTools.Method(typeof(PrivateStorageHaulPatch), "CellPrefix")));
                    Log.Message("[DSH-ECONOMY] 私有物存储点过滤：已挂载");
                    ok = true;
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 StoreUtility.IsGoodStoreCell，私有物仍可能被选中公共仓库");
                }

                MethodInfo groupAdded = AccessTools.Method(typeof(SlotGroup), "Notify_Added", new[] { typeof(Thing) });
                if (groupAdded != null)
                {
                    harmony.Patch(groupAdded, postfix: new HarmonyMethod(AccessTools.Method(typeof(PrivateStorageEjectPatch), "Postfix")));
                    Log.Message("[DSH-ECONOMY] 私有仓库兜底拦截：已挂载");
                    ok = true;
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 SlotGroup.Notify_Added，私有仓库兜底拦截不可用");
                }

                MethodInfo buildingGizmos = AccessTools.Method(typeof(ThingWithComps), "GetGizmos");
                if (buildingGizmos != null)
                {
                    harmony.Patch(buildingGizmos, postfix: new HarmonyMethod(AccessTools.Method(typeof(StorageOwnershipGizmoPatch), "Postfix")));
                    Log.Message("[DSH-ECONOMY] 存储建筑所有权按钮：已挂载");
                    ok = true;
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 ThingWithComps.GetGizmos，存储建筑所有权不可用");
                }

                return ok;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 产权存储 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：库存增量 / 生成追踪 ───────────────────────────────
        private static bool PatchSpawnTracking(Harmony harmony)
        {
            try
            {
                MethodInfo spawn = AccessTools.Method(typeof(Thing), "SpawnSetup", new[] { typeof(Map), typeof(bool) });
                MethodInfo despawn = AccessTools.Method(typeof(Thing), "DeSpawn", new[] { typeof(DestroyMode) });
                if (spawn != null && despawn != null)
                {
                    harmony.Patch(spawn, postfix: new HarmonyMethod(AccessTools.Method(typeof(StockIncrementalPatch), "SpawnPostfix")));
                    harmony.Patch(spawn, postfix: new HarmonyMethod(AccessTools.Method(typeof(PrivateItemStorageGuard), "Postfix")));
                    harmony.Patch(spawn, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPrivateStorageBehavior), "SpawnPostfix")));
                    harmony.Patch(despawn, prefix: new HarmonyMethod(AccessTools.Method(typeof(StockIncrementalPatch), "DespawnPrefix")));
                    harmony.Patch(despawn, prefix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPrivateStorageBehavior), "DespawnPrefix")));
                    Log.Message("[DSH-ECONOMY] 增量库存统计：已挂载");
                    return true;
                }

                Log.Warning("[DSH-ECONOMY] 未找到 Thing.SpawnSetup/DeSpawn，增量库存不可用");
                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 库存生成追踪 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：搬运 ──────────────────────────────────────────────
        private static bool PatchHauling(Harmony harmony)
        {
            try
            {
                MethodInfo canHaul = AccessTools.Method(typeof(Verse.AI.HaulAIUtility), "PawnCanAutomaticallyHaul",
                    new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
                if (canHaul != null)
                {
                    harmony.Patch(canHaul, prefix: new HarmonyMethod(AccessTools.Method(typeof(PrivateStorageHaulPatch), "Prefix")));
                    Log.Message("[DSH-ECONOMY] 私有仓库取出限制：已挂载");
                    try
                    {
                        harmony.Patch(canHaul, prefix: new HarmonyMethod(AccessTools.Method(typeof(EconomyHaulFilter), "Prefix")));
                        Log.Message("[DSH-ECONOMY] 搬运白名单：已挂载");
                    }
                    catch (System.Exception ex2) { Log.Warning("[DSH-ECONOMY] 搬运白名单失败：" + ex2.Message); }
                    return true;
                }

                Log.Warning("[DSH-ECONOMY] 未找到 HaulAIUtility.PawnCanAutomaticallyHaul，私有仓库取出限制不可用");
                return false;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 搬运 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：食物 / 进食 ───────────────────────────────────────
        private static bool PatchFoodAccess(Harmony harmony)
        {
            try
            {
                bool ok = false;

                MethodInfo willEat = AccessTools.Method(typeof(FoodUtility), "WillEat",
                    new[] { typeof(Pawn), typeof(Thing), typeof(Pawn), typeof(bool), typeof(bool) });
                if (willEat != null)
                {
                    try
                    {
                        harmony.Patch(willEat, prefix: new HarmonyMethod(AccessTools.Method(typeof(PrivateFoodEatBlock), "Prefix")));
                        Log.Message("[DSH-ECONOMY] 私有食物不可被他人食用（WillEat）：已挂载");
                        ok = true;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] 私有食物 WillEat 补丁失败：" + e.Message);
                    }
                    try
                    {
                        MethodInfo findFood = AccessTools.Method(typeof(FoodUtility), "TryFindBestFoodSourceFor",
                            new[] { typeof(Pawn), typeof(Pawn), typeof(bool), typeof(Thing).MakeByRefType(),
                                    typeof(ThingDef).MakeByRefType(), typeof(bool), typeof(bool), typeof(bool), typeof(bool) });
                        if (findFood != null)
                        {
                            harmony.Patch(findFood, postfix: new HarmonyMethod(AccessTools.Method(typeof(PrivateFoodEatBlock), "FindFoodPostfix")));
                            Log.Message("[DSH-ECONOMY] 别人的私有营养膏在搜索阶段就被排除：已挂载");
                        }

                        MethodInfo optimality = AccessTools.Method(typeof(FoodUtility), "FoodOptimality",
                            new[] { typeof(Pawn), typeof(Thing), typeof(ThingDef), typeof(float), typeof(bool) });
                        if (optimality != null)
                        {
                            harmony.Patch(optimality, postfix: new HarmonyMethod(AccessTools.Method(typeof(PrivateFoodEatBlock), "OptimalityPostfix")));
                            Log.Message("[DSH-ECONOMY] 优先吃自己买的食物：已挂载");
                        }
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] 私有食物 WillEat 补丁失败：" + e.Message);
                    }
                }
                else
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 FoodUtility.WillEat(Pawn,Thing,...)");
                }

                MethodInfo willEatDef = AccessTools.Method(typeof(FoodUtility), "WillEat",
                    new[] { typeof(Pawn), typeof(ThingDef), typeof(Pawn), typeof(bool), typeof(bool) });
                if (willEatDef != null)
                {
                    try
                    {
                        ok = true;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] WillEat(ThingDef) 补丁失败：" + e.Message);
                    }
                }

                MethodInfo chairOrSpot = AccessTools.Method(typeof(Toils_Ingest), "TryFindChairOrSpot",
                    new[] { typeof(Pawn), typeof(Thing), typeof(IntVec3).MakeByRefType() });
                if (chairOrSpot != null)
                {
                    try
                    {
                        harmony.Patch(chairOrSpot, postfix: new HarmonyMethod(AccessTools.Method(typeof(EatAtTablePatch), "Postfix")));
                        Log.Message("[DSH-ECONOMY] 吃饭优先找桌子（全图兜底）：已挂载");
                        ok = true;
                    }
                    catch (Exception e)
                    {
                        Log.Warning("[DSH-ECONOMY] 找桌子补丁失败：" + e.Message);
                    }
                }

                return ok;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 食物进食 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：劳动 / 工资 ───────────────────────────────────────
        private static bool PatchWorkAccounting(Harmony harmony)
        {
            try
            {
                bool workPatched = EconomyWorkTypes.Patch(harmony);
                if (!workPatched) Log.Warning("[DSH-ECONOMY] 工作类型权重不可用，所有工作按 1.0 计");

                Log.Message("[DSH-ECONOMY] 劳动计量：每 250 tick 对账（不 patch 热路径）");

                bool billPatched = EconomyProductionAccounting.Patch(harmony);
                Log.Message("[DSH-ECONOMY] 工单记账（投入/产出/工资）：" + (billPatched ? "已挂载" : "不可用"));

                return workPatched || billPatched;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 劳动工资 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：市场 / 购买 ───────────────────────────────────────
        private static bool PatchMarketPurchase(Harmony harmony)
        {
            try
            {
                bool buyPatched = EconomyPurchase.Patch(harmony);
                if (buyPatched) Log.Message("[DSH-ECONOMY] 购买逻辑已启用（公共化 <50% 的消费品要付费）");
                else Log.Warning("[DSH-ECONOMY] 购买逻辑不可用");

                bool marketPatched = MarketPatch.Patch(harmony);
                Log.Message("[DSH-ECONOMY] 市场购买结算（拿到手才扣钱）：" + (marketPatched ? "已挂载" : "不可用"));

                return buyPatched || marketPatched;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 市场购买 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：营养膏机 ──────────────────────────────────────────
        private static bool PatchPasteDispenser(Harmony harmony)
        {
                System.Reflection.MethodInfo carry = AccessTools.Method(typeof(Pawn_CarryTracker), "TryStartCarry", new[] { typeof(Thing), typeof(int), typeof(bool) });
                if (carry != null)
                {
                    harmony.Patch(carry, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPasteDispenser), "CarryPostfix")));
                    Log.Message("[DSH-ECONOMY] 营养膏取餐即扣费+私有：已挂载");
                }
                System.Reflection.MethodInfo ingested = AccessTools.Method(typeof(Thing), "Ingested", new[] { typeof(Pawn), typeof(float) });
                if (ingested != null)
                {
                    harmony.Patch(ingested, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPasteDispenser), "IngestedPostfix")));
                    Log.Message("[DSH-ECONOMY] 营养膏吃下即扣费+私有：已挂载");
                }

            try
            {
                                bool pastePatched = EconomyPasteDispenser.Patch(harmony);
                Log.Message("[DSH-ECONOMY] 营养膏机收费按钮：" + (pastePatched ? "已挂载" : "不可用"));
                return pastePatched;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 营养膏机 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：医疗 / CE ─────────────────────────────────────────
        private static bool PatchMedicalCE(Harmony harmony)
        {
            try
            {
                bool cePatched = CECompat.Patch(harmony);
                Log.Message("[DSH-ECONOMY] CE 换弹精神状态放行：" + (cePatched ? "已挂载" : "未启用"));

                bool augmentGatePatched = false;
                try { augmentGatePatched = EconomyAugmentGate.Patch(harmony); }
                catch (System.Exception ex) { Log.Warning("[DSH-ECONOMY] 植入物付费门槛补丁失败：" + ex.Message); }
                Log.Message("[DSH-ECONOMY] 植入物付不起不能做：" + (augmentGatePatched ? "已挂载" : "不可用"));

                return cePatched || augmentGatePatched;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 医疗CE 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：满意度 / Orientation ──────────────────────────────
        private static bool PatchSatisfaction(Harmony harmony)
        {
            try
            {
                bool needInjected = SatisfactionNeedInjector.Patch(harmony);
                Log.Message("[DSH-ECONOMY] 满意度需求（原生 Need）：" + (needInjected ? "已挂载" : "不可用"));
                bool rimHudTip = RimHudIntegration.Patch(harmony);

                bool cardPatched = NeedsSatisfactionPatch.PatchCard(harmony);
                Log.Message("[DSH-ECONOMY] 角色卡满意度行：" + (cardPatched ? "已挂载" : "不可用"));

                bool inspectPatched = EconomyInspectPatch.Patch(harmony);
                Log.Message("[DSH-ECONOMY] 信息栏满意度：" + (inspectPatched ? "已挂载" : "不可用"));

                return needInjected || rimHudTip || cardPatched || inspectPatched;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 满意度 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：动乱 / 战斗 ───────────────────────────────────────
        private static bool PatchRiotCombat(Harmony harmony)
        {
            try
            {
                bool berserkPatched = EconomyBerserkTargetPatch.Patch(harmony);
                Log.Message("[DSH-ECONOMY] 狂暴目标过滤：" + (berserkPatched ? "已挂载" : "不可用"));

                bool barracksPatched = BarracksPatch.Patch(harmony);
                Log.Message("[DSH-ECONOMY] 多人宿舍心情惩罚撤销：" + (barracksPatched ? "已挂载" : "不可用"));

                return berserkPatched || barracksPatched;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 动乱战斗 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：折旧 / 维修 ───────────────────────────────────────
        private static bool PatchDepreciationRepair(Harmony harmony)
        {
            try
            {
                DepreciationGizmoPatch.Patch(harmony);
                return true;
            }
            catch (System.Exception ex)
            {
                Log.Warning("[DSH-ECONOMY] 折旧维修按钮补丁失败：" + ex.Message);
                return false;
            }
        }

        // ── 分组：贸易 ──────────────────────────────────────────────
        private static bool PatchTrade(Harmony harmony)
        {
            try
            {
                bool tradeLimitPatched = false;
                try { tradeLimitPatched = EconomyTradeLimit.Patches.Patch(harmony); }
                catch (System.Exception ex) { Log.Warning("[DSH-ECONOMY] 贸易限制补丁失败：" + ex.Message); }
                Log.Message("[DSH-ECONOMY] 外部贸易限制：" + (tradeLimitPatched ? "已挂载" : "不可用"));

                bool rimChatPatched = false;
                try { rimChatPatched = EconomyRimChatTrade.Patches.Patch(harmony); }
                catch (System.Exception ex) { Log.Warning("[DSH-ECONOMY] RimChat 预期价格补丁失败：" + ex.Message); }
                Log.Message("[DSH-ECONOMY] RimChat 预期价格（边际计价）：" + (rimChatPatched ? "已挂载" : "未启用"));

                return tradeLimitPatched || rimChatPatched;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 贸易 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：文化 / 戒律 ───────────────────────────────────────
        private static bool PatchPrecepts(Harmony harmony)
        {
            try
            {
                bool ok = false;
                try
                {
                    var rm = AccessTools.Method(typeof(Ideo), "RemovePrecept", new[] { typeof(Precept), typeof(bool) });
                    if (rm != null)
                    {
                        harmony.Patch(rm, prefix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPreceptForcer), "RemovePreceptPrefix")));
                        var req = AccessTools.Method(typeof(Ideo), "PreceptIsRequired", new[] { typeof(PreceptDef) });
                        if (req != null) harmony.Patch(req, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPreceptForcer), "PreceptIsRequiredPostfix")));
                        var canAdd = AccessTools.Method(typeof(Ideo), "CanAddPreceptAllFactions", new[] { typeof(PreceptDef) });
                        if (canAdd != null) harmony.Patch(canAdd, prefix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPreceptForcer), "CanAddPreceptPrefix")));
                        Log.Message("[DSH-ECONOMY] 财产制度戒律锁定（不可移除/不可换向）");
                        var rec = AccessTools.Method(typeof(Ideo), "RecachePrecepts", Type.EmptyTypes);
                        if (rec != null) harmony.Patch(rec, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPreceptForcer), "RecachePostfix")));
                        Log.Message("[DSH-ECONOMY] 财产制度戒律防移除：已挂载");
                        ok = true;
                    }
                }
                catch (Exception e) { Log.Warning("[DSH-ECONOMY] 戒律防移除补丁失败：" + e.Message); }
                return ok;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 文化戒律 补丁失败：" + e.Message);
                return false;
            }
        }

        // ── 分组：军人 ──────────────────────────────────────────────
        private static bool PatchMilitary(Harmony harmony)
        {
            try
            {
                bool ok = false;

                try
                {
                    if (EconomyMentalGuard.Patch(harmony))
                    {
                        Log.Message("[DSH-ECONOMY] 军人精神状态免疫：已挂载");
                        ok = true;
                    }
                    else
                        Log.Warning("[DSH-ECONOMY] 军人精神状态免疫：未找到 TryStartMentalState");
                }
                catch (System.Exception ex3) { Log.Warning("[DSH-ECONOMY] 军人精神状态免疫失败：" + ex3.Message); }

                try
                {
                    System.Reflection.MethodInfo mWorkActive = AccessTools.Method(typeof(Pawn_WorkSettings), "WorkIsActive", new[] { typeof(WorkTypeDef) });
                    if (mWorkActive != null)
                    {
                        harmony.Patch(mWorkActive, prefix: new HarmonyMethod(AccessTools.Method(typeof(EconomyMilitary), "WorkIsActivePrefix")));
                        System.Reflection.MethodInfo mGet = AccessTools.Method(typeof(Pawn_WorkSettings), "GetPriority", new[] { typeof(WorkTypeDef) });
                        if (mGet != null) harmony.Patch(mGet, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyMilitary), "GetPriorityPostfix")));
                        System.Reflection.MethodInfo mSet = AccessTools.Method(typeof(Pawn_WorkSettings), "SetPriority", new[] { typeof(WorkTypeDef), typeof(int) });
                        if (mSet != null) harmony.Patch(mSet, prefix: new HarmonyMethod(AccessTools.Method(typeof(EconomyMilitary), "SetPriorityPrefix")));
                        System.Reflection.MethodInfo mDisabled = AccessTools.Method(typeof(Pawn), "GetDisabledWorkTypes", new[] { typeof(bool) });
                        if (mDisabled != null) harmony.Patch(mDisabled, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyMilitary), "GetDisabledWorkTypesPostfix")));
                        Log.Message("[DSH-ECONOMY] 军人工作类型硬禁用：已挂载（GetPriority=" + (mGet != null) + " SetPriority=" + (mSet != null) + " 原版禁用表=" + (mDisabled != null) + "）");
                        ok = true;
                    }
                }
                catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 军人工作硬禁用补丁失败：" + e.Message); }

                return ok;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 军人 补丁失败：" + e.Message);
                return false;
            }
        }
    }

    public static class StackPatch
    {
        public static void Postfix(Thing __instance, Thing other, ref bool __result)
        {
            if (!__result) return;
            if (EconomyPrivate.OwnerOf(__instance) != EconomyPrivate.OwnerOf(other))
            {
                __result = false;
                return;
            }
            // 品质不同的物品不能合并，否则品质会被"先入为主"覆盖
            if (QualityDiffers(__instance, other)) __result = false;
        }

        internal static bool QualityDiffers(Thing a, Thing b)
        {
            RimWorld.CompQuality qa = a != null ? a.TryGetComp<RimWorld.CompQuality>() : null;
            RimWorld.CompQuality qb = b != null ? b.TryGetComp<RimWorld.CompQuality>() : null;
            if (qa == null && qb == null) return false;
            if (qa == null || qb == null) return true;
            return qa.Quality != qb.Quality;
        }
    }

    /// <summary>合并/拆分时把私有标签带过去，避免"主人的货变成无主公物"。</summary>
    public static class TagPreservePatch
    {
        public static void AbsorbPostfix(Thing __instance, Thing other)
        {
            if (__instance == null || other == null) return;
            Pawn mine = EconomyPrivate.OwnerOf(__instance);
            if (mine != null) return;
            Pawn theirs = EconomyPrivate.OwnerOf(other);
            if (theirs != null) EconomyPrivate.SetPrivate(__instance, theirs);
        }

        public static void SplitPostfix(Thing __instance, Thing __result)
        {
            if (__instance == null || __result == null) return;
            Pawn owner = EconomyPrivate.OwnerOf(__instance);
            if (owner == null) return;
            if (EconomyPrivate.OwnerOf(__result) != null) return;
            EconomyPrivate.SetPrivate(__result, owner);
        }
    }

    public static class AbsorbPatch
    {
        public static bool Prefix(Thing __instance, Thing other, ref bool __result)
        {
            if (EconomyPrivate.OwnerOf(__instance) == EconomyPrivate.OwnerOf(other)
                && !StackPatch.QualityDiffers(__instance, other))
            {
                return true;
            }
            __result = false;
            return false;
        }
    }
}
