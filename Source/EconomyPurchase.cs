using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 购买逻辑（最小闭环）：公共化程度低于 50% 的商品，小人消费时要付钱。
    /// 先从"吃饭/吃药"这种必然发生的消费入手，钱从个人白银扣给殖民地。
    /// </summary>
    public static class EconomyPurchase
    {
        public const float FreeThreshold = 100f;  // 只有 100%（完全公有）才免费自取；50% = 原版价，照常收费

        public static bool Patch(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Thing), "Ingested", new[] { typeof(Pawn), typeof(float) });
                if (target == null)
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 Thing.Ingested，购买逻辑不可用");
                    return false;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPurchase), "Postfix")));
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 购买逻辑补丁失败：" + e.Message);
                return false;
            }
        }

        public static void Postfix(Thing __instance, Pawn ingester)
        {
            try
            {
                if (__instance == null || ingester == null) return;
                if (!ingester.IsColonist || ingester.Map == null) return;

                Map map = ingester.Map;
                MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
                if (comp == null) return;

                // 吃得好：按原版食物等级给一档正向事件（mod 食物的 preferability 一样适用）
                try
                {
                    if (__instance.def.ingestible != null)
                    {
                        FoodPreferability pref = __instance.def.ingestible.preferability;
                        string label = null;
                        if (pref >= FoodPreferability.MealLavish) label = "吃得好（珍馐）";
                        else if (pref >= FoodPreferability.MealFine) label = "吃得好（佳肴）";
                        else if (pref >= FoodPreferability.MealSimple) label = "吃得好（家常菜）";
                        if (label != null)
                        {
                            EconomySatisfactionEvents.RemoveByPrefix(ingester, "吃得好");
                            EconomySatisfactionEvents.Add(ingester, label, 2f, 1);
                        }
                    }
                }
                catch { }

                // 吃自己买过的东西（带本人私有标签）：不再重复收费
                if (EconomyPrivate.OwnerOf(__instance) == ingester) return;

                // 营养膏单独走营养膏机的收费逻辑，不参与按公共化定价
                if (EconomyPasteDispenser.IsPaste(__instance))
                {
                    EconomyPasteDispenser.Charge(map, ingester);
                    return;
                }

                EconomyCategoryDef cat = EconomyRegistry.CategoryOf(__instance.def);
                if (cat == null) return;
                if (comp.GetPublicization(cat) >= FreeThreshold) return;

                float unit = EconomyPrice.UnitPrice(__instance, map);
                int price = Mathf.Max(1, Mathf.RoundToInt(unit));
                if (price <= 0) return;

                int have = EconomyBank.PrivateSilver(map, ingester);
                if (have < price)
                {
                    // 买不起就一分不扣，避免把整堆银币划走
                    // 吃了/用了却没付钱（不论食物还是成瘾品）→ 道德指责
                    // 标签不带食物名：否则每换一种食物就是一个新事件，永远叠不起来
                    EconomySatisfactionEvents.Add(ingester, "道德指责（无力支付）", -1f, 3);
                    EconomySatisfaction.Recompute(ingester);
                    Log.Message("[DSH-ECONOMY] 购买失败：" + ingester.LabelShort + " 想吃 "
                        + __instance.LabelNoCount + "，需要 " + price + "，只有 " + have);
                    return;
                }

                int paid = EconomyBank.TakePrivateSilver(map, ingester, price);
                Log.Message("[DSH-ECONOMY] 购买：" + ingester.LabelShort + " 花 " + paid
                    + " 买了 " + __instance.LabelNoCount + "（" + cat.label + " 公共化 "
                    + comp.GetPublicization(cat).ToString("0") + "%）");
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 购买结算异常：" + e.Message);
            }
        }
    }
}
