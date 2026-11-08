using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 购买结算的落点：东西真的进背包那一刻才扣钱。
    /// </summary>
    public static class MarketPatch
    {
        public static bool Patch(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(ThingOwner<Thing>), "TryAdd", new[] { typeof(Thing), typeof(bool) });
            if (target == null)
            {
                Log.Warning("[DSH-ECONOMY] 市场结算：没找到 ThingOwner.TryAdd");
                return false;
            }
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(TryAddPrefix), BindingFlags.Static | BindingFlags.NonPublic)),
                postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(TryAddPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            int hooked = 0;
            MethodInfo[] all = typeof(Pawn_JobTracker).GetMethods(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Name != "EndCurrentJob") continue;
                try
                {
                    harmony.Patch(all[i],
                        prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(EndJobPrefix),
                            BindingFlags.Static | BindingFlags.NonPublic)),
                        postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(EndJobPostfix),
                            BindingFlags.Static | BindingFlags.NonPublic)));
                    hooked++;
                }
                catch (Exception e)
                {
                    Log.Warning("[DSH-ECONOMY] 任务结束探针挂载失败：" + e.Message);
                }
            }
            Log.Message("[DSH-ECONOMY] 任务结束探针：已挂 " + hooked + " 个 EndCurrentJob 重载");

            HookProduction(harmony);

            return true;
        }

        /// <summary>
        /// 采集"东西是怎么来的"：合成 / 挖掘 / 收割 / 屠宰。
        /// 全部按方法名探测并 try/catch，1.7 改了签名最坏只是少记一类，不会崩。
        /// </summary>
        private static void HookProduction(Harmony harmony)
        {
            int recipe = 0, mine = 0, harvest = 0, butcher = 0;

            // 1) 合成：一个配方产出若干件
            MethodInfo[] g = typeof(GenRecipe).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
            for (int i = 0; i < g.Length; i++)
            {
                if (g[i].Name != "MakeRecipeProducts") continue;
                if (!typeof(IEnumerable<Thing>).IsAssignableFrom(g[i].ReturnType)) continue;
                try
                {
                    harmony.Patch(g[i], postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                        nameof(RecipeProductsPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    recipe++;
                }
                catch (Exception e) { Log.Warning("[DSH-ECONOMY] 产地统计·合成 挂载失败：" + e.Message); }
            }

            // 2) 挖掘：矿石被挖掉
            MethodInfo[] m = typeof(Mineable).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < m.Length; i++)
            {
                if (m[i].Name != "Destroy") continue;
                // 只挂 Mineable 自己声明的那个：否则会挂到 Building.Destroy 上，所有建筑拆掉都记一次
                if (m[i].DeclaringType != typeof(Mineable)) continue;
                try
                {
                    harmony.Patch(m[i], postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                        nameof(MinePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    mine++;
                }
                catch (Exception e) { Log.Warning("[DSH-ECONOMY] 产地统计·挖掘 挂载失败：" + e.Message); }
            }

            // 3) 收割：作物被收走。
            // 1.7 里不保证有 Harvest() 这个方法和 Thing 这个返回类型，所以按名字宽松匹配：
            // 能返回 Thing 的读结果；返回 void 的读作物定义。两条都挂不上就把方法名打出来。
            MethodInfo[] p = typeof(Plant).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < p.Length; i++)
            {
                if (p[i].Name == null) continue;
                if (p[i].Name.IndexOf("Harvest", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (p[i].GetParameters().Length > 0) continue;
                try
                {
                    if (typeof(Thing).IsAssignableFrom(p[i].ReturnType))
                    {
                        harmony.Patch(p[i], postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                            nameof(HarvestPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        harvest++;
                    }
                    else if (p[i].ReturnType == typeof(void))
                    {
                        harmony.Patch(p[i], postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                            nameof(HarvestVoidPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        harvest++;
                    }
                }
                catch (Exception e) { Log.Warning("[DSH-ECONOMY] 产地统计·收割 挂载失败：" + e.Message); }
            }
            // 收割在 1.7 里没有可挂的方法（Plant 上只有 get_HarvestableNow 这类 getter），
            // 靠静态作物表兜底即可，不再打诊断日志。

            // 4) 屠宰：尸体变成肉
            MethodInfo[] c = typeof(Corpse).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < c.Length; i++)
            {
                if (c[i].Name != "ButcherProducts") continue;
                if (!typeof(IEnumerable<Thing>).IsAssignableFrom(c[i].ReturnType)) continue;
                try
                {
                    harmony.Patch(c[i], postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                        nameof(ButcherPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    butcher++;
                }
                catch (Exception e) { Log.Warning("[DSH-ECONOMY] 产地统计·屠宰 挂载失败：" + e.Message); }
            }

            // 5) 动物产出（毛/奶/蛋）：1.7 的 CompHasGatherableBodyResource 只有 get_xxx 属性，
            // 没有任何可挂的 Gather() 方法（实测 7 个类型全是 getter）。
            // 改为在 EndJobPostfix 里按"采集任务成功"记账，这里不再挂钩子。

            Log.Message("[DSH-ECONOMY] 产地统计：合成 " + recipe + " / 挖掘 " + mine
                + " / 收割 " + harvest + " / 屠宰 " + butcher + " 个采集点（动物产出按任务结算记账）");
        }

        private static void RecipeProductsPostfix(ref IEnumerable<Thing> __result)
        {
            if (__result == null) return;
            foreach (Thing t in __result)
            {
                if (t == null) continue;
                EconomyProductionStats.Note(t.def, EconomyProductionStats.Source.Recipe, t.stackCount);
            }
        }

        private static System.Reflection.FieldInfo mineableField;
        private static bool mineableFieldTried;

        private static void MinePostfix(Mineable __instance)
        {
            if (__instance == null || __instance.def == null || __instance.def.building == null) return;
            if (!mineableFieldTried)
            {
                mineableFieldTried = true;
                mineableField = typeof(BuildingProperties).GetField("mineableThing");
            }
            if (mineableField == null) return;
            ThingDef prod = mineableField.GetValue(__instance.def.building) as ThingDef;
            if (prod == null) return;
            EconomyProductionStats.Note(prod, EconomyProductionStats.Source.Mine, 1);
        }

        private static void HarvestPostfix(ref Thing __result)
        {
            if (__result == null) return;
            EconomyProductionStats.Note(__result.def, EconomyProductionStats.Source.Harvest, __result.stackCount);
        }

        /// <summary>收作物的方法若返回 void，就从作物定义本身取产物。</summary>
        private static void HarvestVoidPostfix(Plant __instance)
        {
            if (__instance == null || __instance.def == null || __instance.def.plant == null) return;
            ThingDef prod = __instance.def.plant.harvestedThingDef;
            if (prod == null) return;
            EconomyProductionStats.Note(prod, EconomyProductionStats.Source.Harvest, 1);
        }

        /// <summary>动物产出（毛/奶/蛋）后置：产物定义从组件的 props 里反射取。</summary>
        private static void GatherPostfix(ThingComp __instance)
        {
            if (__instance == null) return;
            try
            {
                ThingDef prod = GatheredResourceOf(__instance);
                if (prod != null) EconomyProductionStats.Note(prod, EconomyProductionStats.Source.Gather, 1);
            }
            catch { }
        }

        private static ThingDef GatheredResourceOf(ThingComp comp)
        {
            object props = comp.props;
            if (props == null) return null;
            System.Reflection.FieldInfo[] fs = props.GetType().GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            for (int i = 0; i < fs.Length; i++)
            {
                if (!typeof(ThingDef).IsAssignableFrom(fs[i].FieldType)) continue;
                ThingDef d = fs[i].GetValue(props) as ThingDef;
                if (d != null) return d;
            }
            return null;
        }

        /// <summary>找不到动物产出组件时，把所有名字像"产出"的 ThingComp 子类列出来。</summary>
        private static void DumpGatherComps()
        {
            try
            {
                string names = "";
                int n = 0;
                foreach (Type t in typeof(ThingComp).AllSubclasses())
                {
                    if (t == null || t.Name == null) continue;
                    if (t.Name.IndexOf("Gather", StringComparison.OrdinalIgnoreCase) < 0
                        && t.Name.IndexOf("Product", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    names += t.Name + " ";
                    n++;
                    if (n >= 25) break;
                }
                Log.Warning("[DSH-ECONOMY] 产出组件候选（" + n + " 个）：" + (n == 0 ? "无" : names));
            }
            catch { }
        }

        /// <summary>挂不上钩子时，把该类型里名字相关的方法名打出来，方便定位 1.7 改成了什么。</summary>
        /// <summary>把某个类型自己声明的方法全列出来（不带过滤），用来找 1.7 的真实方法名。</summary>
        private static void DumpTypeMethods(Type t, int max)
        {
            try
            {
                MethodInfo[] ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                    | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                string s = "";
                int n = 0;
                for (int i = 0; i < ms.Length && n < max; i++)
                {
                    s += ms[i].Name + "(" + ms[i].GetParameters().Length + ") ";
                    n++;
                }
                Log.Warning("[DSH-ECONOMY] 产出方法 " + t.Name + "（" + ms.Length + " 个）：" + (n == 0 ? "无" : s));
            }
            catch { }
        }

        private static void DumpMethods(string typeName, MethodInfo[] all, string keyword)
        {
            string names = "";
            for (int i = 0; i < all.Length; i++)
            {
                if (all[i].Name == null) continue;
                if (all[i].Name.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) < 0
                    && all[i].Name.IndexOf("Gather", StringComparison.OrdinalIgnoreCase) < 0) continue;
                names += all[i].Name + "(" + all[i].GetParameters().Length + ")->" + all[i].ReturnType.Name + " ";
            }
            Log.Warning("[DSH-ECONOMY] 产地统计·" + keyword + "：在 " + typeName
                + " 里没找到可挂的方法，候选=[" + (names.Length == 0 ? "无" : names) + "]");
        }

        private static void ButcherPostfix(ref IEnumerable<Thing> __result)
        {
            if (__result == null) return;
            foreach (Thing t in __result)
            {
                if (t == null || t.def == null) continue;
                if (t.def.ingestible == null) continue;   // 只记肉/皮这类产物里的食物，避免把皮革也算成屠宰主产物
                EconomyProductionStats.Note(t.def, EconomyProductionStats.Source.Butcher, t.stackCount);
            }
        }

        /// <summary>
        /// 买的/领的东西不许并进已有的堆：
        /// 合并会销毁新来的那份，标签就没地方打了。
        /// </summary>
        private static void TryAddPrefix(ThingOwner<Thing> __instance, ref bool canMergeWithExistingStacks)
        {
            if (!canMergeWithExistingStacks) return;
            Pawn_InventoryTracker inv = __instance.Owner as Pawn_InventoryTracker;
            if (inv == null || inv.pawn == null) return;
            if (!EconomyMarket.HasPending(inv.pawn)) return;

            canMergeWithExistingStacks = false;
        }

        private static void TryAddPostfix(ThingOwner<Thing> __instance, Thing item, bool canMergeWithExistingStacks, bool __result)
        {
            DevLog.Msg("[DSH-ECONOMY] TryAdd进来了 结果=" + __result);
            if (!__result || item == null || __instance == null) return;
            Pawn_InventoryTracker inv = __instance.Owner as Pawn_InventoryTracker;
            if (inv == null || inv.pawn == null) return;

            ThingDef def = item.def;
            int incoming = item.stackCount;
            bool merged = item.Destroyed;

            // 注意：OnInventoryAdded 会 remove 掉 pending，所以必须在它之前判断
            bool wasPurchase = EconomyMarket.HasPending(inv.pawn);
            EconomyMarket.OnInventoryAdded(inv.pawn, item);
             // 临时探针：看清食物到底有没有进背包、有没有订单
            if (def != null && def.ingestible != null)
            {
                DevLog.Msg("[DSH-ECONOMY] 食物进包：" + def.label + " ×" + incoming
                    + " 订单=" + wasPurchase + " 合并=" + merged);
            }

            // 公共白银进了背包：登记一下，让搬运逻辑把它送回仓库（或丢下）
            if (def == ThingDefOf.Silver && inv.pawn.IsColonist)
            {
                EconomyPublicSilver.MarkDirty(inv.pawn);
            }

            // 只给"市场购买"打私有：任务奖励、拾取、搬运不该被误标。
            if (def == null || !inv.pawn.IsColonist || !wasPurchase) return;

            // 正常情况下 prefix 已经阻止合并了；万一还是合并了，退回去找幸存的那堆
            Thing target = merged ? FindStack(inv.pawn, def) : item;
            if (target == null || target.Destroyed) return;
            if (EconomyPrivate.OwnerOf(target) != null) return;

            EconomyPrivate.SetPrivate(target, inv.pawn);
        }

        /// <summary>在背包里找一堆指定物品（合并后用来定位幸存者）。</summary>
        private static Thing FindStack(Pawn pawn, ThingDef def)
        {
            if (pawn == null || pawn.inventory == null || pawn.inventory.innerContainer == null) return null;
            IList<Thing> inner = pawn.inventory.innerContainer;
            for (int i = 0; i < inner.Count; i++)
            {
                Thing t = inner[i];
                if (t == null || t.Destroyed) continue;
                if (t.def != def) continue;
                return t;
            }
            return null;
            }  
             /// <summary>探针：记录带订单的小人为什么结束任务。</summary>
        private static void EndJobPrefix(Pawn_JobTracker __instance, JobCondition condition)
        {
            Pawn p = __instance.pawn;
            if (p == null || !p.IsColonist) return;
            if (!EconomyMarket.HasPending(p)) return;
            DevLog.Msg("[DSH-ECONOMY] 任务结束 " + p.LabelShort
                + " 原因=" + condition
                + " 任务=" + (__instance.curJob == null ? "无" : __instance.curJob.def.defName));
        }
        /// <summary>任务正常成功结束后，就地结算订单。</summary>
        private static void EndJobPostfix(Pawn_JobTracker __instance, JobCondition condition)
        {
            Pawn p = __instance.pawn;
            if (p != null && p.IsColonist) TryNoteAnimalGather(__instance.curJob);
            if (condition != JobCondition.Succeeded) return;
            if (p == null || !p.IsColonist) return;
            if (!EconomyMarket.HasPending(p)) return;
            EconomyMarket.SettleFromInventory(p);
        }

        // ── 动物产出（毛/奶/蛋）：1.7 没有可挂的 Gather() 方法，只有 get_xxx 属性。
        // 所以在"干完一件采集活"时记录：任务名里带 GatherAnimal，目标就是那只动物。
        private static PropertyInfo resourceDefProp;
        private static bool resourceDefTried;

        private static bool IsGatherableComp(Type t)
        {
            while (t != null && t != typeof(ThingComp))
            {
                string n = t.Name;
                if (n.IndexOf("Gatherable", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("Milkable", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("Shearable", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                if (n.IndexOf("AnimalProduct", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                t = t.BaseType;
            }
            return false;
        }

        private static ThingDef ResourceDefOf(ThingComp comp)
        {
            try
            {
                if (!resourceDefTried)
                {
                    resourceDefTried = true;
                    Type t = comp.GetType();
                    while (t != null && resourceDefProp == null)
                    {
                        resourceDefProp = t.GetProperty("ResourceDef", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                        t = t.BaseType;
                    }
                }
                if (resourceDefProp == null) return null;
                return resourceDefProp.GetValue(comp) as ThingDef;
            }
            catch { return null; }
        }

        private static void TryNoteAnimalGather(Job job)
        {
            if (job == null || job.def == null || job.def.defName == null) return;
            if (job.def.defName.IndexOf("GatherAnimal", StringComparison.OrdinalIgnoreCase) < 0) return;
            Pawn animal = job.targetA.Thing as Pawn;
            if (animal == null) return;
            foreach (ThingComp comp in animal.AllComps)
            {
                if (comp == null) continue;
                if (!IsGatherableComp(comp.GetType())) continue;
                ThingDef res = ResourceDefOf(comp);
                if (res != null) EconomyProductionStats.Note(res, EconomyProductionStats.Source.Gather, 1);
            }
        }
    }
}