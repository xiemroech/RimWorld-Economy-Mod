using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 营养膏机收费：用营养膏机取食要付钱，钱从个人白银扣，变成殖民地公共财产。
    /// 设置直接挂在营养膏机的按钮上（每个地图一份），跟原版"客人花费"的交互一致。
    /// </summary>
    public static class EconomyPasteDispenser
    {
        public static bool Patch(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Building_NutrientPasteDispenser), "GetGizmos");
                if (target == null)
                {
                    Log.Warning("[DSH-ECONOMY] 未找到营养膏机 GetGizmos，收费按钮不可用");
                    return false;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyPasteDispenser), "Postfix")));
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 营养膏机补丁失败：" + e.Message);
                return false;
            }
        }

        public static void Postfix(Building_NutrientPasteDispenser __instance, ref IEnumerable<Gizmo> __result)
        {
            Map map = __instance.Map;
            if (map == null) return;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return;

            List<Gizmo> list = new List<Gizmo>(__result);

            Command_Action toggle = new Command_Action();
            toggle.defaultLabel = "营养膏收费：" + (comp.pasteChargeEnabled ? "开" : "关");
            toggle.defaultDesc = "开启后，殖民者每次从营养膏机取食都要付钱（从个人白银扣，转为殖民地公共财产）。";
            Texture2D coin = ContentFinder<Texture2D>.Get("UI/Icons/DshEco_Coin", false);
            if (coin != null) toggle.icon = coin;
            toggle.action = delegate
            {
                comp.pasteChargeEnabled = !comp.pasteChargeEnabled;
                Messages.Message("营养膏收费已" + (comp.pasteChargeEnabled ? "开启" : "关闭"),
                    MessageTypeDefOf.NeutralEvent, false);
            };
            list.Add(toggle);

            Command_Action up = new Command_Action();
            up.defaultLabel = "单价 +5（当前 " + comp.pastePrice.ToString("0") + " 银）";
            up.defaultDesc = "提高每份营养膏的价格。";
            Texture2D upIcon = ContentFinder<Texture2D>.Get("UI/Icons/DshEco_PriceUp", false);
            if (upIcon != null) up.icon = upIcon;
            up.action = delegate { comp.pastePrice = Mathf.Clamp(comp.pastePrice + 5f, 0f, 200f); };
            list.Add(up);

            Command_Action down = new Command_Action();
            down.defaultLabel = "单价 -5（当前 " + comp.pastePrice.ToString("0") + " 银）";
            down.defaultDesc = "降低每份营养膏的价格。";
            Texture2D downIcon = ContentFinder<Texture2D>.Get("UI/Icons/DshEco_PriceDown", false);
            if (downIcon != null) down.icon = downIcon;
            down.action = delegate { comp.pastePrice = Mathf.Clamp(comp.pastePrice - 5f, 0f, 200f); };
            list.Add(down);

            __result = list;
        }

        /// <summary>吃营养膏时收费；返回是否扣到了钱。</summary>

        /// <summary>取餐即扣：小人开始去吃营养膏时先付钱，付不起就不让他拿。</summary>
        /// <summary>营养膏被吃下那一刻：扣钱并把它（这份餐）标成吃的人私有。</summary>
        public static void IngestedPostfix(Thing __instance, Pawn ingester)
        {
            try
            {
                if (__instance == null || ingester == null) return;
                if (!IsPaste(__instance)) return;
                // 取餐时没结算上的（地上捡的、合并丢了标签的、没走搬运的），在这里补扣一次。
                // 吃下去这一口才是真正的消费，所以以吃为准，避免"吃到了却没付钱"。
                if (EconomyPrivate.OwnerOf(__instance) == null)
                {
                    MapEconomyComponent comp2 = ingester.Map != null
                        ? ingester.Map.GetComponent<MapEconomyComponent>() : null;
                    if (comp2 != null && comp2.pasteChargeEnabled)
                        Charge(ingester.Map, ingester);
                    Log.Message("[DSH-ECONOMY] 营养膏： " + ingester.LabelShort + " 吃的是无主那份，已在入口补结算");
                }
            }
            catch (System.Exception e) { EcoLog.WarnOnce("paste-ingested", e); }
        }


        /// <summary>取餐那一刻（进搬运栏）：扣钱并标私有，这样它能被私有仓库收进去。</summary>
        public static void CarryPostfix(Pawn_CarryTracker __instance, Thing item, int __result)
        {
            try
            {
                Pawn p = __instance != null ? __instance.pawn : null;
                if (__result <= 0) return;                   // 返回实际拿起的数量，0 = 没拿起来
                if (p == null) return;
                // 合并可能把新 Thing 并进手上已有的堆，所以以"手上现在这堆"为准
                item = p.carryTracker != null && p.carryTracker.CarriedThing != null
                    ? p.carryTracker.CarriedThing : item;
                if (item == null) return;
                if (!p.IsColonist || p.Map == null) return;
                if (!IsPaste(item)) return;

                // 送饭给囚犯：留成公共的，不标私有、不收费
                if (p.CurJobDef == JobDefOf.DeliverFood) return;
                // 其他情况（自己去吃、搬运）一律在拿到手的那一刻算私有并计费
                MapEconomyComponent comp = p.Map.GetComponent<MapEconomyComponent>();
                if (EconomyPrivate.OwnerOf(item) != null) return;   // 已结算过

                // 收费开关只管"收不收费"，私有标签永远打
                bool paid = false;
                if (comp != null && comp.pasteChargeEnabled) paid = Charge(p.Map, p);
                EconomyPrivate.SetPrivate(item, p);
                TagCarriedPaste(p);

                // 一次把缺口补齐：多接几份塞进背包（都是私有，且逐份计费）
                TryDispenseExtra(p, comp);
                Log.Message("[DSH-ECONOMY] 营养膏取餐：" + p.LabelShort + " 取 1 份，" +
                            (paid ? "已付费" : "免费/未收费") + "，标为私有");
            }
            catch (System.Exception e) { EcoLog.WarnOnce("paste-carry", e); }
        }


        /// <summary>接餐时顺便把"还差几顿"补齐，最多 6 份。</summary>
        private static void TryDispenseExtra(Pawn p, MapEconomyComponent comp)
        {
            try
            {
                Job job = p.CurJob;
                if (job == null || job.targetA.Thing == null) return;
                Building_NutrientPasteDispenser disp = job.targetA.Thing as Building_NutrientPasteDispenser;
                if (disp == null || disp.IsForbidden(p) || !disp.CanDispenseNow) return;

                float have = EconomyConsumption.StoredNutrition(p, p.Map, true);
                float nutritionPerMeal = ThingDefOf.MealNutrientPaste.GetStatValueAbstract(StatDefOf.Nutrition);
                int want = Mathf.Clamp(Mathf.CeilToInt((5.4f - have) / Mathf.Max(0.1f, nutritionPerMeal)), 0, 6);
                if (want <= 0) return;

                int made = 0;
                for (int i = 0; i < want; i++)
                {
                    if (comp != null && comp.pasteChargeEnabled && EconomyBank.PrivateSilver(p.Map, p) < Mathf.Max(1f, comp.pastePrice)) break;
                    Thing meal = disp.TryDispenseFood();
                    if (meal == null) break;
                    EconomyPrivate.SetPrivate(meal, p);
                    if (comp != null && comp.pasteChargeEnabled) Charge(p.Map, p);
                    if (!p.inventory.innerContainer.TryAdd(meal))
                    {
                        GenPlace.TryPlaceThing(meal, p.Position, p.Map, ThingPlaceMode.Near);
                        break;
                    }
                    made++;
                }
                if (made > 0) Log.Message("[DSH-ECONOMY] 营养膏补货：" + p.LabelShort + " 额外带了 " + made + " 份（私有）");
            }
            catch (System.Exception e) { EcoLog.WarnOnce("paste-extra", e); }
        }


        private static readonly System.Collections.Generic.Dictionary<Pawn, int> lastStockTick =
            new System.Collections.Generic.Dictionary<Pawn, int>();

        /// <summary>定期给缺营养膏的殖民者"在机器上买几份"，产出私有营养膏以便存进自己仓库。</summary>
        public static void StockTick(Map map)
        {
            try
            {
                if (map == null || Find.TickManager == null) return;
                int now = Find.TickManager.TicksGame;
                MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
                if (comp == null) return;

                List<Building_NutrientPasteDispenser> disps = null;
                foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
                {
                    if (p == null || p.Dead || p.Downed) continue;
                    int last;
                    if (lastStockTick.TryGetValue(p, out last) && now - last < 2500) continue;

                    float have = EconomyConsumption.StoredNutrition(p, map, true);
                    float per = ThingDefOf.MealNutrientPaste.GetStatValueAbstract(StatDefOf.Nutrition);
                    int want = Mathf.Clamp(Mathf.CeilToInt((5.4f - have) / Mathf.Max(0.1f, per)), 0, 6);
                    if (want <= 0) continue;

                    if (disps == null)
                    {
                        disps = new List<Building_NutrientPasteDispenser>();
                        foreach (Building b in map.listerBuildings.allBuildingsColonist)
                        {
                            Building_NutrientPasteDispenser d = b as Building_NutrientPasteDispenser;
                            if (d != null && d.CanDispenseNow && !d.IsForbidden(p)) disps.Add(d);
                        }
                    }
                    if (disps.Count == 0) return;

                    Building_NutrientPasteDispenser disp = disps[0];
                    int made = 0;
                    for (int i = 0; i < want; i++)
                    {
                        if (comp.pasteChargeEnabled)
                        {
                            int price = Mathf.Max(1, Mathf.RoundToInt(comp.pastePrice));
                            if (EconomyBank.PrivateSilver(map, p) < price) break;
                        }
                        Thing meal = disp.TryDispenseFood();
                        if (meal == null) break;
                        EconomyPrivate.SetPrivate(meal, p);
                        if (EconomyPrivate.OwnerOf(meal) != p)
                            Log.Warning("[DSH-ECONOMY] 营养膏打标失败：" + meal.LabelShort + " 仍是公共");
                        if (comp.pasteChargeEnabled) Charge(map, p);
                        if (!p.inventory.innerContainer.TryAdd(meal))
                        {
                            GenPlace.TryPlaceThing(meal, p.Position, p.Map, ThingPlaceMode.Near);
                        }
                        TagCarriedPaste(p);
                        made++;
                    }
                    if (made > 0)
                    {
                        lastStockTick[p] = now;
                        Log.Message("[DSH-ECONOMY] 营养膏备货：" + p.LabelShort + " 买了 " + made + " 份私有营养膏（存粮 " + have.ToString("F1") + " 营养）");
                    }
                }
            }
            catch (System.Exception e) { EcoLog.WarnOnce("paste-stock", e); }
        }


        /// <summary>把背包里所有无主营养膏都记到 p 名下（合并进已有公共堆时会丢标签）。</summary>
        public static void TagCarriedPaste(Pawn p)
        {
            try
            {
                if (p == null) return;
                // 手上的那份也要标：营养膏是 carryTracker 端着去吃的，不进背包，
                // 合并进已有堆时新的 Thing 会顶替旧的，只标 inventory 会漏掉手上这堆
                Thing carried = p.carryTracker != null ? p.carryTracker.CarriedThing : null;
                if (carried != null && IsPaste(carried) && EconomyPrivate.OwnerOf(carried) == null)
                    EconomyPrivate.SetPrivate(carried, p);
                if (p.inventory == null) return;
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (t == null || !IsPaste(t)) continue;
                    if (EconomyPrivate.OwnerOf(t) == null) EconomyPrivate.SetPrivate(t, p);
                }
            }
            catch (System.Exception e) { EcoLog.WarnOnce("paste-tag-carry", e); }
        }


        /// <summary>这个人名下有没有现成的营养膏（背包或自己的仓库）。</summary>
        public static bool OwnsPaste(Pawn p)
        {
            try
            {
                if (p == null) return false;
                if (p.carryTracker != null && p.carryTracker.CarriedThing != null
                    && IsPaste(p.carryTracker.CarriedThing)) return true;
                if (p.inventory != null)
                {
                    foreach (Thing t in p.inventory.innerContainer)
                        if (t != null && IsPaste(t) && EconomyPrivate.OwnerOf(t) == p) return true;
                }
                Map map = p.MapHeld ?? p.Map;
                if (map == null || map.haulDestinationManager == null) return false;
                foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                {
                    if (g == null || g.CellsList == null) continue;
                    if (EconomyPrivateStorageBehavior.OwnerOfGroup(g) != p) continue;
                    for (int i = 0; i < g.CellsList.Count; i++)
                    {
                        List<Thing> here = g.CellsList[i].GetThingList(map);
                        for (int k = 0; k < here.Count; k++)
                            if (here[k] != null && IsPaste(here[k]) && EconomyPrivate.OwnerOf(here[k]) == p) return true;
                    }
                }
            }
            catch (System.Exception e) { EcoLog.WarnOnce("paste-owns", e); }
            return false;
        }

        public static bool Charge(Map map, Pawn eater)
        {
            if (map == null || eater == null || !eater.IsColonist) return false;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null || !comp.pasteChargeEnabled) return false;

            int price = Mathf.Max(1, Mathf.RoundToInt(comp.pastePrice));
            int have = EconomyBank.PrivateSilver(map, eater);
            if (have < price)
            {
                // 没钱也吃上了：事后记一笔「道德指责」，工作满意度 −1，可叠加
                EconomySatisfactionEvents.Add(eater, "铁丝之力（吃不起营养膏，拿铁丝捅了投币口）", -2f, 3);
                EconomySatisfaction.Recompute(eater);
                Log.Message("[DSH-ECONOMY] 营养膏： " + eater.LabelShort + " 付不起（需 " + price + "，只有 " + have + "），记道德指责");
                return false;
            }

            int paid = EconomyBank.TakePrivateSilver(map, eater, price);
            Log.Message("[DSH-ECONOMY] 营养膏： " + eater.LabelShort + " 付了 " + paid + " 银（余额 " + (have - paid) + "）");
            return paid >= price;
        }

        /// <summary>地图上有没有营养膏机（有的话居民就不买生食，直接去吃现成的）。</summary>
        public static bool HasDispenser(Map map)
        {
            if (map == null) return false;
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b is Building_NutrientPasteDispenser d && !d.IsBrokenDown()) return true;
            }
            return false;
        }

        public static bool IsPaste(Thing t)
        {
            if (t == null || t.def == null) return false;
            if (t.def == ThingDefOf.MealNutrientPaste) return true;
            // 别的 mod 可能换 def，按名字兜底
            string n = t.def.defName;
            return n != null && n.IndexOf("NutrientPaste", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
