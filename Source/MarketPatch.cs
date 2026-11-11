using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using UnityEngine;

namespace DshEconomy
{
    /// <summary>
    /// 购买结算的落点：东西真的进背包那一刻才扣钱。
    /// </summary>
    public static class MarketPatch
    {
        public static bool Patch(Harmony harmony)
        {
            MethodInfo stackM = AccessTools.Method(typeof(Thing), "CanStackWith", new[] { typeof(Thing) });
            if (stackM != null)
            {
                harmony.Patch(stackM, prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                    nameof(NoMergeDifferentIngredients), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            // 真正的合并动作在 TryAbsorbStack 里。CanStackWith 只是"能不能"，
            // 地面物品并堆经常绕过它直接吞噬，所以还得挂这里。
            MethodInfo absorb = AccessTools.Method(typeof(Thing), "TryAbsorbStack", new[] { typeof(Thing), typeof(bool) });
            if (absorb != null)
            {
                harmony.Patch(absorb, prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                    nameof(NoAbsorbDifferentIngredients), BindingFlags.Static | BindingFlags.NonPublic)));
            }
            // 只搬不捡：禁止把地上的公共物品捡进背包（有订单的购买/救济例外）。
            HookJobGiverProbe(harmony);
            // 合并探测：只观察、不拦截，用来找"标签丢失"的真入口。
            // 用枚举而不是精确签名——1.7 里 TryAbsorbStack 的参数表不一定只有一种。
            try
            {
                MethodInfo[] absorbs = typeof(Thing).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                int hookedMerge = 0;
                string mergeError = null;
                for (int mi = 0; mi < absorbs.Length; mi++)
                {
                    MethodInfo m = absorbs[mi];
                    if (m == null || m.Name != "TryAbsorbStack") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length != 2) continue;
                    if (ps[0].ParameterType != typeof(Thing) || ps[1].ParameterType != typeof(bool)) continue;
                    try
                    {
                        harmony.Patch(m, postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(MergeProbePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        hookedMerge++;
                    }
                    catch (System.Exception ex)
                    {
                        mergeError = m.Name + " 挂载失败：" + ex.GetType().Name + " " + ex.Message;
                    }
                }
                DevLog.Msg("[DSH-ECONOMY] 合并探测：已挂 " + hookedMerge + " 个 TryAbsorbStack 重载"
                    + (string.IsNullOrEmpty(mergeError) ? "" : "｜失败原因：" + mergeError));
                if (hookedMerge == 0)
                {
                    System.Text.StringBuilder sb = new System.Text.StringBuilder();
                    for (int mi = 0; mi < absorbs.Length; mi++)
                    {
                        MethodInfo m = absorbs[mi];
                        if (m == null) continue;
                        string nm = m.Name;
                        if (nm.IndexOf("Absorb", System.StringComparison.OrdinalIgnoreCase) < 0
                            && nm.IndexOf("Merge", System.StringComparison.OrdinalIgnoreCase) < 0
                            && nm.IndexOf("Stack", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                        sb.Append(nm).Append('(');
                        ParameterInfo[] cps = m.GetParameters();
                        for (int k = 0; k < cps.Length; k++)
                        {
                            if (k > 0) sb.Append(", ");
                            sb.Append(cps[k].ParameterType.Name);
                        }
                        sb.Append(") ");
                    }
                    DevLog.Msg("[DSH-ECONOMY] 并堆候选方法：" + sb.ToString());
                }
            }
            catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 合并探测挂载失败：" + e.Message); }

            HookDespawnProbe(harmony);
            HookFoodTrace(harmony);
            // 已回滚：之前想接管存储界面的价格列，原版那一列是打开页签时就缓存好的，
            // 挂了几层都改不到，最后改成"在仓库里就换价"又会影响财富。
            // 现在一律不碰原版价格：价格只在物品信息（右下角）里显示。
            HookHideMarketItems(harmony);
            HookPriceBoard(harmony);
            HookMarketHaulBoost(harmony);
            try { BedRestOverride.Apply(harmony); }
            catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 卧床判定独占失败：" + e.Message); }
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
            DevLog.Msg("[DSH-ECONOMY] 任务结束探针：已挂 " + hooked + " 个 EndCurrentJob 重载");

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

            DevLog.Msg("[DSH-ECONOMY] 产地统计：合成 " + recipe + " / 挖掘 " + mine
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
            // 一块矿能挖出好几件（mineableYield），只记 1 件会把"每件工时"抬高好几倍，
            // 于是矿物价格被算贵。有 yield 就按 yield 记。
            int yield = 1;
            try
            {
                if (__instance.def.building.mineableYield > 0) yield = __instance.def.building.mineableYield;
            }
            catch { yield = 1; }
            EconomyProductionStats.Note(prod, EconomyProductionStats.Source.Mine, yield);
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
        /// 【已停用】买的/领的东西不许并堆。
        /// 实测这个拦法会让"背包里放不进第二份食物"（小人拿了就掉、留不住口粮），
        /// 而真正丢标签的并堆又不在这个入口上，所以整体放行，回到原版行为。
        /// </summary>
        private static void TryAddPrefix(ThingOwner<Thing> __instance, Thing item, ref bool canMergeWithExistingStacks)
        {
            try
            {
                if (__instance == null || item == null) return;

                // 背包与手上：一律不许并，并了就会把两个主人的标签并成一个
                if (__instance.Owner is Pawn_InventoryTracker || __instance.Owner is Pawn_CarryTracker)
                {
                    canMergeWithExistingStacks = false;
                    return;
                }

                Pawn itemOwner = EconomyPrivate.OwnerOf(item);
                string itemKey = EconomyValue.IngredientKey(item);

                for (int i = 0; i < __instance.Count; i++)
                {
                    Thing t = __instance[i];
                    if (t == null || t.Destroyed || t.def != item.def) continue;

                    string tKey = EconomyValue.IngredientKey(t);
                    bool bothUnknown = string.IsNullOrEmpty(tKey) && string.IsNullOrEmpty(itemKey);
                    bool sameKey = bothUnknown
                        || (!string.IsNullOrEmpty(tKey) && !string.IsNullOrEmpty(itemKey)
                            && string.Equals(tKey, itemKey, System.StringComparison.Ordinal));
                    bool sameOwner = EconomyPrivate.OwnerOf(t) == itemOwner;

                    if (sameKey && sameOwner) continue;

                    canMergeWithExistingStacks = false;
                    string why = !sameOwner ? "归属不同"
                        : (bothUnknown ? "其他" : (string.IsNullOrEmpty(tKey) || string.IsNullOrEmpty(itemKey) ? "用料未知" : "配方不同"));
                    MergeLog(item, t, why);
                    return;
                }
            }
            catch { }
        }

        private static int lastMergeLog = -99999;

        /// <summary>记录一次被拒绝的并堆（30 tick 节流，避免刷屏）。</summary>
        private static void MergeLog(Thing a, Thing b, string why)
        {
            try
            {
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                if (now - lastMergeLog < 30) return;
                lastMergeLog = now;
                DevLog.Msg("[MERGE] 拒绝合并：" + (a != null ? a.LabelShort : "?") + " ×" + (a != null ? a.stackCount : 0)
                    + " + " + (b != null ? b.LabelShort : "空") + " ×" + (b != null ? b.stackCount : 0)
                    + " 原因=" + why);
            }
            catch { }
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

        /// <summary>
        /// 能不能叠在一起：**无法证明是"同一种东西"就不合并**。
        /// 判据三条，缺一不可：同一个 def、同一个主人、同样的原料表。
        /// 之前按"配方键"拦，拦不住"两种简单食物"（配方不同但同 def 的饭），
        /// 这次直接比原料表本身。
        /// </summary>
        private static bool SameStackIdentity(Thing a, Thing b)
        {
            if (a == null || b == null) return false;
            if (a.def != b.def) return false;
            if (!ReferenceEquals(EconomyPrivate.OwnerOf(a), EconomyPrivate.OwnerOf(b))) return false;
            CompIngredients ca = a.TryGetComp<CompIngredients>();
            CompIngredients cb = b.TryGetComp<CompIngredients>();
            if (ca == null && cb == null) return true;
            if (ca == null || cb == null) return false;
            List<ThingDef> la = ca.ingredients;
            List<ThingDef> lb = cb.ingredients;
            if (la == null || lb == null) return la == lb;
            if (la.Count != lb.Count) return false;
            // 顺序不敏感：逐个配对，配上了就划掉
            bool[] used = new bool[lb.Count];
            for (int i = 0; i < la.Count; i++)
            {
                bool hit = false;
                for (int j = 0; j < lb.Count; j++)
                {
                    if (used[j] || lb[j] != la[i]) continue;
                    used[j] = true; hit = true; break;
                }
                if (!hit) return false;
            }
            return true;
        }

        private static bool NoMergeDifferentIngredients(Thing __instance, Thing other, ref bool __result)
        {
            try
            {
                if (__instance == null || other == null) return true;
                if (SameStackIdentity(__instance, other)) return true;
                __result = false;
                MergeProbeLog(__instance, other, "拒绝叠加：身份不同");
                return false;
            }
            catch { return true; }
        }

        
        /// <summary>
        /// 【已停用】拦截合并。
        /// 实测原版并堆绕过了这里的两个入口（CanStackWith / TryAbsorbStack），
        /// 拦不到目标，反而让"背包里放不进第二堆"等行为异常，故整体放行，交给原版。
        /// </summary>
        private static bool NoAbsorbDifferentIngredients(Thing __instance, Thing other, ref bool __result)
        {
            try
            {
                if (__instance == null || other == null) return true;
                if (SameStackIdentity(__instance, other)) return true;
                __result = false;
                MergeProbeLog(__instance, other, "拒绝合并：身份不同");
                return false;
            }
            catch { return true; }
        }

        private static void MergeProbeLog(Thing a, Thing b, string why)
        {
            try
            {
                if (Find.TickManager == null) return;
                DevLog.Msg("[MERGE] " + why + "：" + (a.def != null ? a.def.defName : "?") + " + " + (b.def != null ? b.def.defName : "?"));
            }
            catch { }
        }

        // ---------------- 存储界面按实际价格显示 ----------------

        /// <summary>
        /// 存储界面（内容列表）那一列价钱用的是原版 MarketValue，和我们的现价对不上。
        /// 只在"正在画这一行"的时候，把 MarketValue 换成我们的现价，其他地方一律不动。
        /// </summary>
        private static void HookStoragePriceRows(HarmonyLib.Harmony harmony)
        {
            try
            {
                int rows = 0;
                MethodInfo[] ms = typeof(ITab_ContentsBase).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < ms.Length; i++)
                {
                    MethodInfo m = ms[i];
                    if (m == null || m.Name != "DrawThingRow") continue;
                    try
                    {
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(StorageRowStart), BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(StorageRowEnd), BindingFlags.Static | BindingFlags.NonPublic)));
                        rows++;
                    }
                    catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 存储行价格：挂绘制失败 " + e.Message); }
                }
                MethodInfo mv = AccessTools.PropertyGetter(typeof(Thing), "MarketValue");
                if (mv != null)
                {
                    harmony.Patch(mv, prefix: new HarmonyMethod(
                        typeof(MarketPatch).GetMethod(nameof(StorageRowMarketValue), BindingFlags.Static | BindingFlags.NonPublic)));
                }
                // 有些列表用的是 def 级基础价，这一条一起接管。
                MethodInfo bmv = AccessTools.PropertyGetter(typeof(ThingDef), "BaseMarketValue");
                if (bmv != null)
                {
                    harmony.Patch(bmv, prefix: new HarmonyMethod(
                        typeof(MarketPatch).GetMethod(nameof(StorageRowBaseMarketValue), BindingFlags.Static | BindingFlags.NonPublic)));
                }
                int pane = 0;
                MethodInfo[] pms = typeof(MainTabWindow_Inspect).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < pms.Length; i++)
                {
                    MethodInfo m = pms[i];
                    if (m == null || m.Name != "DoWindowContents") continue;
                    try
                    {
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(StorageRowStart), BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(StorageRowEnd), BindingFlags.Static | BindingFlags.NonPublic)));
                        pane++;
                    }
                    catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 存储行价格：挂检视面板失败 " + e.Message); }
                }
                // 存储页签自己的绘制入口，比"检视面板"更贴近 —— 列表就是在 FillTab 里画出来的。
                int tabs = 0;
                MethodInfo[] tms = typeof(ITab_Storage).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < tms.Length; i++)
                {
                    MethodInfo m = tms[i];
                    if (m == null || m.Name != "FillTab") continue;
                    try
                    {
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(StorageRowStart), BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(StorageRowEnd), BindingFlags.Static | BindingFlags.NonPublic)));
                        tabs++;
                    }
                    catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 存储行价格：挂页签失败 " + e.Message); }
                }
                // 真正的主角：原版列表读的是"物品的市场价值属性"（GetStatValue）。
                // 它是 StatExtension 里的**静态扩展方法**，不在 Thing 自己身上，所以上面那轮找到 0 个。
                int statHooks = 0;
                string statNames = "";
                Type se = AccessTools.TypeByName("StatExtension") ?? AccessTools.TypeByName("RimWorld.StatExtension");
                if (se != null)
                {
                    MethodInfo[] sms = se.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
                    for (int i = 0; i < sms.Length; i++)
                    {
                        MethodInfo m = sms[i];
                        if (m == null || m.Name != "GetStatValue") continue;
                        ParameterInfo[] ps = m.GetParameters();
                        statNames += " " + m.Name + "(" + (ps.Length > 0 ? ps[0].ParameterType.Name : "") + "," + (ps.Length > 1 ? ps[1].ParameterType.Name : "") + ")";
                        if (ps.Length < 2 || ps[0].ParameterType != typeof(Thing) || ps[1].ParameterType != typeof(StatDef)) continue;
                        try
                        {
                            harmony.Patch(m, prefix: new HarmonyMethod(
                                typeof(MarketPatch).GetMethod(nameof(StorageRowStatValue), BindingFlags.Static | BindingFlags.NonPublic)));
                            statHooks++;
                        }
                        catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 存储行价格：挂属性取值失败 " + e.Message); }
                    }
                }
                DevLog.Msg("[DSH-ECONOMY] 属性取值候选：" + (statNames.Length > 0 ? statNames : " 没找到 StatExtension"));
                // 有些列表在"打开页签"那一下就把数字算好存起来了，画的时候只是照着存好的值填。
                // 所以开页签、内容变化也各挂一次，让开关在这期间保持打开。
                int opens = 0;
                MethodInfo[] oms = typeof(ITab_Storage).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < oms.Length; i++)
                {
                    MethodInfo m = oms[i];
                    if (m == null) continue;
                    if (m.Name != "OnOpen" && m.Name != "NotifyContentsChanged") continue;
                    try
                    {
                        harmony.Patch(m,
                            prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(StorageRowStart), BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(StorageRowEnd), BindingFlags.Static | BindingFlags.NonPublic)));
                        opens++;
                    }
                    catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 存储行价格：挂开页签失败 " + e.Message); }
                }
                DevLog.Msg("[DSH-ECONOMY] 存储界面按现价显示：已挂 " + rows + " 个绘制方法、" + pane + " 个检视面板、" + tabs + " 个存储页签、" + opens + " 个开页签、" + statHooks + " 个属性取值"
                    + (mv != null ? "，Thing.MarketValue 已接管" : "，没找到 Thing.MarketValue")
                    + (bmv != null ? "，BaseMarketValue 已接管" : "，没找到 BaseMarketValue"));
            }
            catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 存储行价格挂载失败：" + e.Message); }
        }

        private static bool drawingStorageRow;
        private static bool loggedMarketCaller;
        // 防递归：算价本身会去读 def 的 MarketValue，不挡住会自己调自己。
        private static bool computingPrice;

        private static void StorageRowStart() { drawingStorageRow = true; }
        private static void StorageRowEnd() { drawingStorageRow = false; }

        /// <summary>
        /// 物品的"市场价值"一律换成我们的现价。
        /// 原因：存储界面那列黄字是原版画的，它读的是 Thing.MarketValue，
        /// 而原版在绘制之前就已经把值算好（换页签/置标志位都拦不住），
        /// 所以只能从这个最底层换掉。
        /// 副作用：殖民地的"财富"和袭击点数会跟着按我们的价格走。
        /// </summary>
        private static bool StorageRowMarketValue(Thing __instance, ref float __result)
        {
            if (__instance != null && __instance.def != null
                && __instance.def.category == ThingCategory.Item
                && !computingPrice && !loggedMarketCaller && !drawingStorageRow)
            {
                loggedMarketCaller = true;
                string st = "无";
                try
                {
                    st = System.Environment.StackTrace;
                    string[] lines = st.Split('\n');
                    st = "";
                    for (int i = 2; i < lines.Length && i < 14; i++) st += lines[i].Trim() + " <- ";
                }
                catch { }
                DevLog.Msg("[DSH-ECONOMY] 谁在读物品市价：" + st);
            }
            if (!loggedMarketCaller && __instance != null && __instance.def != null
                && __instance.def.category == ThingCategory.Item)
            {
                loggedMarketCaller = true;
                DevLog.Msg("[DSH-ECONOMY] 市价属性被读：开关=" + drawingStorageRow + " 物品=" + __instance.def.defName);
            }
            if (computingPrice || __instance == null) return true;
            if (!drawingStorageRow && !InStorageBuilding(__instance)) return true;
            try
            {
                Map map = __instance.Map;
                if (map == null) return true;
                computingPrice = true;
                __result = EconomyPrice.UnitPrice(__instance, map);
                return false;
            }
            catch { return true; }
            finally { computingPrice = false; }
        }

        private static int statLogs;
        /// <summary>这件东西是不是正躺在一个存储建筑的格子上（仓库/货架/摊位）。</summary>
        private static bool InStorageBuilding(Thing t)
        {
            try
            {
                Map map = t.Map;
                if (map == null || !t.Spawned) return false;
                var group = map.haulDestinationManager.SlotGroupAt(t.Position);
                return group != null && group.parent is Building_Storage;
            }
            catch { return false; }
        }

        private static bool StorageRowStatValue(Thing __instance, StatDef stat, ref float __result)
        {
            if (__instance != null && stat == StatDefOf.MarketValue && __instance.def != null
                && __instance.def.category == ThingCategory.Item && statLogs < 6)
            {
                statLogs++;
                DevLog.Msg("[DSH-ECONOMY] 市价取值：开关=" + drawingStorageRow + " 物品=" + __instance.def.defName
                    + " 原值=" + __result + " 位置=" + (__instance.Spawned ? __instance.Position.ToString() : "未生成"));
            }
            if (!drawingStorageRow || computingPrice || __instance == null || stat != StatDefOf.MarketValue) return true;
            if (__instance.def == null || __instance.def.category != ThingCategory.Item) return true;
            try
            {
                Map map = __instance.Map;
                if (map == null) return true;
                computingPrice = true;
                __result = EconomyPrice.UnitPrice(__instance, map);
                return false;
            }
            catch { return true; }
            finally { computingPrice = false; }
        }

        private static bool StorageRowBaseMarketValue(ThingDef __instance, ref float __result)
        {
            if (!drawingStorageRow || computingPrice || __instance == null) return true;
            try
            {
                Map map = Find.CurrentMap;
                if (map == null) return true;
                computingPrice = true;
                __result = EconomyPrice.UnitPrice(__instance, map);
                return false;
            }
            catch { return true; }
            finally { computingPrice = false; }
        }

        // ---------------- 只搬不捡 ----------------

        /// <summary>
        /// 禁止"把地上的公共物品捡进背包"。
        /// 这个 mod 的定价和归属都按订单走：捡起来的东西没有订单，
        /// 就会变成"公有物品躺在私人背包里"，既算不出价也判不出归属。
        /// 搬运（地面→地面）不受影响，因为那本来就没有归属问题。
        /// </summary>
        private static bool NoFreePickup(Pawn_JobTracker __instance, Job __0)
        {
            Job job = __0;
            try
            {
                // 功能已移除（"只搬不捡"会让 AI 反复重试、看起来卡原地）。
                // 现在一律放行：捡起来的东西由 PackFood 记账或 [PUB] 收回处理。
                return true;

                Pawn p = __instance != null ? __instance.pawn : null;
                if (p == null || !p.IsColonist) return true;
                if (EconomyMarket.HasPending(p)) return true;         // 有订单 = 购买/救济，放行

                Thing t = job.targetA.Thing;
                if (t == null || t.Destroyed) return true;
                if (IsAmmo(t.def)) return true;                        // 弹药豁免

                return false;                                          // 无订单的"白捡"，拒绝
            }
            catch { return true; }
        }

        /// <summary>
        /// 是不是弹药。CE 之类的弹药分类是 mod 加的，不能写死 ThingCategoryDefOf，
        /// 所以按"分类名或 defName 里含 Ammo/Bullet/Shell/弹药"判断；查不到就当作不是。
        /// </summary>
        private static bool IsAmmo(ThingDef def)
        {
            if (def == null) return false;
            try
            {
                if (def.defName != null)
                {
                    string n = def.defName;
                    if (n.IndexOf("Ammo", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Bullet", System.StringComparison.OrdinalIgnoreCase) >= 0
                        || n.IndexOf("Shell", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
                if (def.thingCategories != null)
                {
                    for (int i = 0; i < def.thingCategories.Count; i++)
                    {
                        ThingCategoryDef c = def.thingCategories[i];
                        if (c == null || c.defName == null) continue;
                        if (c.defName.IndexOf("Ammo", System.StringComparison.OrdinalIgnoreCase) >= 0
                            || c.defName.IndexOf("弹药", System.StringComparison.Ordinal) >= 0) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private static int lastPickupTraceTick = -99999;

        /// <summary>
        /// 路径反查：谁派发了 TakeInventory(拾取) 任务。
        /// 只在真的派了拾取时打印调用栈，每 30 tick 最多一次，避免刷屏。
        /// </summary>
        private static void TracePickupSource(Pawn_JobTracker __instance, Job __0)
        {
            try
            {
                if (__0 == null || __0.def != JobDefOf.TakeInventory) return;
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                if (now - lastPickupTraceTick < 30) return;
                lastPickupTraceTick = now;

                Pawn p = __instance != null ? __instance.pawn : null;
                StringBuilder sb = new StringBuilder();
                sb.Append("[TRACE] 谁派的拾取：").Append(p != null ? p.LabelShort : "?");
                sb.Append(" 目标=").Append(__0.targetA.Thing != null ? __0.targetA.Thing.LabelShort : "无");
                sb.Append(" 数量=").Append(__0.count);

                System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(1, false);
                int n = 0;
                for (int i = 0; i < st.FrameCount && n < 14; i++)
                {
                    System.Diagnostics.StackFrame f = st.GetFrame(i);
                    MethodBase mb = f != null ? f.GetMethod() : null;
                    if (mb == null || mb.DeclaringType == null) continue;
                    string tn = mb.DeclaringType.Name;
                    if (tn == "StackTrace") continue;
                    sb.Append(" ← ").Append(tn).Append('.').Append(mb.Name);
                    n++;
                }
                DevLog.Msg(sb.ToString());
            }
            catch { }
        }

        private static void HookNoFreePickup(Harmony harmony)
        {
            try
            {
                int hooked = 0;
                MethodInfo[] ms = typeof(Pawn_JobTracker).GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name != "StartJob" && ms[i].Name != "TryTakeOrderedJob") continue;
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ps.Length < 1 || ps[0].ParameterType != typeof(Job)) continue;
                    try
                    {
                        harmony.Patch(ms[i],
                            prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                                nameof(NoFreePickup), BindingFlags.Static | BindingFlags.NonPublic)),
                            postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                                nameof(TracePickupSource), BindingFlags.Static | BindingFlags.NonPublic)));
                        hooked++;
                    }
                    catch { }
                }
                DevLog.Msg("[DSH-ECONOMY] 只搬不捡：已挂 " + hooked + " 个派活入口");
            }
            catch (Exception e) { Log.Warning("[DSH-ECONOMY] 只搬不捡挂载失败：" + e.Message); }
        }

        // ---------------- 思考树点名：谁派的拾取 ----------------
        // 栈追踪会被 JIT 内联吃掉调用层，"谁派的"查不出来。
        // 但每个 JobGiver 出任务时都会经过 TryIssueJobPackage，this 就是那个 JobGiver 实例，
        // 类名直接给出答案，不受内联影响。

        /// <summary>
        /// 弹出路径探测：私人物品被 DeSpawn 时打印调用栈。
        /// 不猜是哪段代码弹的，让栈自己说话。
        /// </summary>
        /// <summary>???????"??????"??????????????</summary>
        private static void MergeProbePostfix(Thing __instance, Thing other, bool __result)
        {
            try
            {
                if (!__result) return;
                Thing a = __instance;
                Thing b = other;
                if (a == null || b == null || b.Destroyed) return;
                if (a.def == null) return;
                int now = Find.TickManager.TicksGame;
                if (now - lastMergeProbe < 30) return;
                lastMergeProbe = now;
                Pawn oa = EconomyPrivate.OwnerOf(a);
                Pawn ob = EconomyPrivate.OwnerOf(b);
                DevLog.Msg("[MERGE] ???" + a.LabelShort + " ?" + a.stackCount
                            + " ? " + (b.LabelShort ?? "?") + " ?" + b.stackCount
                            + " ??=" + (oa == null ? "?" : oa.LabelShort)
                            + "/" + (ob == null ? "?" : ob.LabelShort)
                            + (oa != ob ? "  ?????" : ""));
            }
            catch { }
        }

        private static int lastMergeProbe = -99999;

        private static void HookDespawnProbe(Harmony harmony)
        {
            try
            {
                MethodInfo despawn = AccessTools.Method(typeof(Thing), "DeSpawn", new[] { typeof(DestroyMode) });
                if (despawn == null)
                {
                    Log.Warning("[DSH-ECONOMY] 弹出探测：没找到 Thing.DeSpawn(DestroyMode)");
                    return;
                }
                harmony.Patch(despawn, prefix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                    nameof(DespawnProbePrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                DevLog.Msg("[DSH-ECONOMY] 弹出探测：已挂 Thing.DeSpawn(DestroyMode)");
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 弹出探测挂载失败：" + e.Message);
            }
        }

        private static void DespawnProbePrefix(Thing __instance)
        {
            try
            {
                if (__instance == null || __instance.def == null) return;
                if (__instance.def.category != ThingCategory.Item) return;

                // 只关心"本来在储物格里、现在要被拿走/弹出"的物品。
                // 地上普通堆（拿饭、切堆）的 SlotGroup 是 null，自然不会刷。
                SlotGroup group = __instance.GetSlotGroup();
                if (group == null) return;

                Pawn owner = EconomyPrivate.OwnerOf(__instance);

                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("[EJECT-PATH] 储物格里的物品被 DeSpawn：").Append(__instance.LabelShort)
                  .Append(" ×").Append(__instance.stackCount)
                  .Append(" 归属=").Append(owner != null ? owner.LabelShort : "公共")
                  .Append(" 位置=").Append(__instance.PositionHeld);
                bool benign = false;
                try
                {
                    StackTrace st = new StackTrace(1, false);
                    int frames = Mathf.Min(st.FrameCount, 10);
                    for (int i = 0; i < frames; i++)
                    {
                        MethodBase m = st.GetFrame(i).GetMethod();
                        if (m == null) continue;
                        string typeName = m.DeclaringType != null ? m.DeclaringType.Name : "?";
                        // 正常行为：从整堆里切一份（SplitOff）或往背包里拿（TakeToInventory），
                        // 这两条都会 DeSpawn/重建物品，不是"被弹出去"。
                        if (typeName.IndexOf("SplitOff", StringComparison.OrdinalIgnoreCase) >= 0
                            || m.Name.IndexOf("SplitOff", StringComparison.OrdinalIgnoreCase) >= 0
                            || typeName.IndexOf("TakeToInventory", StringComparison.OrdinalIgnoreCase) >= 0
                            || m.Name.IndexOf("TakeToInventory", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            benign = true;
                        }
                        sb.Append(" ← ").Append(typeName).Append(".").Append(m.Name);
                    }
                }
                catch { }
                if (benign) return;   // 切堆/拿取，不打日志
                DevLog.Msg(sb.ToString());
            }
            catch { }
        }

        private static void HookJobGiverProbe(Harmony harmony)
        {
            try
            {
                // 只挂基类不够：绝大多数 JobGiver 都 override 了 TryIssueJobPackage，
                // 必须把基类和所有子类各自声明的那个方法都挂上。
                List<Type> types = new List<Type>();
                types.Add(typeof(ThinkNode_JobGiver));
                try { types.AddRange(typeof(ThinkNode_JobGiver).AllSubclasses()); }
                catch { }

                int hooked = 0;
                for (int t = 0; t < types.Count; t++)
                {
                    MethodInfo[] ms;
                    try
                    {
                        ms = types[t].GetMethods(BindingFlags.Public | BindingFlags.NonPublic
                            | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                    }
                    catch { continue; }

                    for (int i = 0; i < ms.Length; i++)
                    {
                        if (ms[i].Name != "TryIssueJobPackage") continue;
                        if (ms[i].ReturnType == null || ms[i].ReturnType.Name != "ThinkResult") continue;
                        try
                        {
                            harmony.Patch(ms[i], postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(
                                nameof(JobGiverProbePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                            hooked++;
                        }
                        catch { }
                    }
                }
                DevLog.Msg("[DSH-ECONOMY] 思考树点名：已挂 " + hooked + " 个 JobGiver 出口（类型 " + types.Count + " 个）");
            }
            catch (Exception e) { Log.Warning("[DSH-ECONOMY] 思考树点名挂载失败：" + e.Message); }
        }

        // 欠账的口粮：小人是"没付钱拿的"，但东西归他，同时要扣工作满意度。
        // key = pawn.GetUniqueLoadID()，value = 物品 defName（到手后按这个找背包里那件）。
        private static readonly Dictionary<string, string> packFoodDebt = new Dictionary<string, string>();

        /// <summary>
        /// 原版 PackFood 拿的东西到手后：打私有标签 + 记一条"道德指责（无力支付）"。
        /// 这样"没付钱"是有代价的，但东西仍然归他，不会出现"拿了却不属于任何人"。
        /// </summary>
        private static void SettlePackFoodDebt(Pawn p)
        {
            try
            {
                if (p == null) return;
                string key = p.GetUniqueLoadID();
                string defName;
                if (!packFoodDebt.TryGetValue(key, out defName)) return;
                if (defName == null) { packFoodDebt.Remove(key); return; }

                Thing found = null;

                if (p.inventory != null && p.inventory.innerContainer != null)
                {
                    for (int i = 0; i < p.inventory.innerContainer.Count; i++)
                    {
                        Thing t = p.inventory.innerContainer[i];
                        if (t == null || t.Destroyed) continue;
                        if (t.def == null || t.def.defName != defName) continue;
                        if (EconomyPrivate.OwnerOf(t) != null) continue;
                        found = t;
                        break;
                    }
                }

                // 还在"手上"的情况：原版 PackFood 取货期间东西在 carryTracker，
                // 不进背包容器，这里同样要认。
                if (found == null && p.carryTracker != null && p.carryTracker.CarriedThing != null)
                {
                    Thing carried = p.carryTracker.CarriedThing;
                    if (carried.def != null && carried.def.defName == defName
                        && EconomyPrivate.OwnerOf(carried) == null)
                        found = carried;
                }

                if (found == null)
                {
                    // 没找到就**不要**清账：可能是还没拿完，或者被打断后他又去拿了一次。
                    // 清掉的话这件东西就永远留在"公有空间"里了。
                    DevLog.Msg("[TRACE] 随身口粮待结算：没找到 " + defName + "，账留着");
                    return;
                }

                packFoodDebt.Remove(key);
                // 扣分规则只在 EconomyPurchase.ChargeUnpaidPickup 一处定义：
                // 它会打私有标签、按这件东西的价钱扣满意度，并按同名事件叠层。
                EconomyPurchase.ChargeUnpaidPickup(p, found);
                DevLog.Msg("[TRACE] 随身口粮结算 " + p.LabelShort + "：" + defName + " → 私有");
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 随身口粮结算失败：" + e.Message);
            }
        }

        private static void JobGiverProbePostfix(ThinkNode_JobGiver __instance, Pawn __0, ref ThinkResult __result)
        {
            try
            {
                Job job = __result.Job;
                if (job == null || job.def != JobDefOf.TakeInventory) return;

                Thing t = job.targetA.Thing;
                string giver = __instance == null ? "?" : __instance.GetType().Name;

                // 只接管原版"随身带口粮"这一条路径。
                if (giver != "JobGiver_PackFood") return;

                Pawn p = __0;
                if (p == null || !p.IsColonist || t == null) return;
                if (t.Destroyed || t.stackCount <= 0) return;

                // 已经有主的（他自己的私有物品）就别管，让原版照常。
                if (EconomyPrivate.OwnerOf(t) != null) return;

                Map map = p.MapHeld;
                if (map == null) return;

                int n = Mathf.Clamp(job.count < 1 ? 1 : job.count, 1, t.stackCount);
                float unit = EconomyPrice.UnitPrice(t, map);
                int cost = Mathf.CeilToInt(unit * n);

                // 钱不够：不拦原版（拦了队伍准备不了行装），但记一笔账，
                // 到手后由 EndJobPostfix 打私有标签并扣"道德指责（无力支付）"。
                if (EconomyBank.PrivateSilver(map, p) < cost)
                {
                    packFoodDebt[p.GetUniqueLoadID()] = t.def.defName;
                    DevLog.Msg("[TRACE] 随身口粮 " + p.LabelShort + "：" + t.LabelShort + "×" + n
                        + " 买不起（要 " + cost + "，有 " + EconomyBank.PrivateSilver(map, p)
                        + "），让他拿，拿到后转私有并扣满意度");
                    return;
                }

                // 能买得起：取消原版的免费拿，改成 mod 的订单（到手时扣钱并打私有标签）。
                // toInventory: true —— 随身口粮必须真的塞进背包容器，
                // 否则它会被抱着走去仓库，背包里永远是 0 件，结算找不到东西。
                if (EconomyMarket.StartPurchase(map, p, t, n, unit, false, true))
                {
                    __result = ThinkResult.NoJob;
                    DevLog.Msg("[TRACE] 随身口粮 " + p.LabelShort + "：" + t.LabelShort + "×" + n
                        + " 转为私有购买（预算 " + cost + " 银）");
                }
            }
            catch { }
        }

        /// <summary>
        /// 拾取（没有订单）落地后的补结算：背包里的那份食物转私有，并按件值扣满意度。
        /// 触发场景：本来是搬运任务，执行时被 AI 换成了"装进背包"，于是既不算买、也没标签。
        /// 只处理食物；已经被打过标签的不管（避免和 PackFood / 购买结算重复扣）。
        /// </summary>
        private static void SettleUnpaidPickup(Pawn p, Job job)
        {
            try
            {
                if (p == null || job == null) return;
                DevLog.Msg("[TRACE] 补结算入口 " + p.LabelShort
                    + " 任务=" + (job.def == null ? "无" : job.def.defName)
                    + " 有目标=" + (job.targetA.Thing != null));
                if (job.def != JobDefOf.TakeInventory) return;
                if (!job.targetA.HasThing) return;

                Thing target = job.targetA.Thing;
                if (target == null) return;

                // 注意：不检查 target.Destroyed。
                // 拾取完成时，地上那一堆已经被并进背包并销毁，
                // 这个引用还在、def 还能读；一旦检查 Destroyed 就会在这里直接返回，
                // 于是"补结算"永远不触发（这就是上一版没生效的原因）。
                ThingDef def = target.def;
                if (def == null || def.ingestible == null)
                {
                    DevLog.Msg("[TRACE] 补结算跳过：" + (def == null ? "def=空" : def.defName + " 不是食物"));
                    return;
                }
                if (p.inventory == null || p.inventory.innerContainer == null) return;

                IList<Thing> inner = p.inventory.innerContainer;
                for (int i = 0; i < inner.Count; i++)
                {
                    Thing t = inner[i];
                    if (t == null || t.Destroyed || t.def != def) continue;
                    if (EconomyPrivate.OwnerOf(t) != null)
                    {
                        DevLog.Msg("[TRACE] 补结算跳过：" + def.defName + " 已经有主（" + EconomyPrivate.OwnerOf(t).LabelShort + "）");
                        return;
                    }

                    EconomyPurchase.ChargeUnpaidPickup(p, t);
                    DevLog.Msg("[TRACE] 拾取补结算 " + p.LabelShort + "：" + def.defName + "×" + t.stackCount);
                    return;
                }
                // 东西可能还在"手上"（haulMode = ToCellStorage 时是抱着走的，
                // 不会进背包容器），这时背包里是 0 件，要去 carryTracker 找。
                if (p.carryTracker != null && p.carryTracker.CarriedThing != null
                    && p.carryTracker.CarriedThing.def == def
                    && EconomyPrivate.OwnerOf(p.carryTracker.CarriedThing) == null)
                {
                    EconomyPurchase.ChargeUnpaidPickup(p, p.carryTracker.CarriedThing);
                    DevLog.Msg("[TRACE] 拾取补结算（手上）" + p.LabelShort + "：" + def.defName);
                    return;
                }

                DevLog.Msg("[TRACE] 补结算跳过：背包里找不到未打标签的 " + def.defName
                    + "（背包共 " + inner.Count + " 件）");
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 拾取补结算失败：" + e.Message);
            }
        }

        /// <summary>
        /// 价格小窗的入口：挂住所有带 Thing 参数的 Widgets.InfoCardButton（信息卡 ⓘ 按钮），
        /// 点一下就记下那件东西，右下角小窗跟着换。
        /// </summary>
        private static void HookPriceBoard(HarmonyLib.Harmony harmony)
        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder("[DSH-ECONOMY] 价格小窗入口候选：");
                int hooked = 0;
                MethodInfo[] ms = typeof(Widgets).GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < ms.Length; i++)
                {
                    MethodInfo m = ms[i];
                    if (m == null || m.Name != "InfoCardButton") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    bool hasThing = false;
                    for (int k = 0; k < ps.Length; k++)
                        if (ps[k].ParameterType == typeof(Thing)) { hasThing = true; break; }
                    if (!hasThing) continue;
                    sb.Append(" ").Append(m.Name).Append('(');
                    for (int k = 0; k < ps.Length; k++)
                    {
                        if (k > 0) sb.Append(", ");
                        sb.Append(ps[k].ParameterType.Name);
                    }
                    sb.Append(")");
                    try
                    {
                        harmony.Patch(m, postfix: new HarmonyMethod(
                            typeof(MarketPatch).GetMethod(nameof(PriceBoardButtonPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        hooked++;
                    }
                    catch (System.Exception e) { sb.Append("(失败:").Append(e.Message).Append(')'); }
                }
                sb.Append(" → 已挂 ").Append(hooked).Append(" 个");
                DevLog.Msg(sb.ToString());
            }
            catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 价格小窗入口挂载失败：" + e.Message); }
        }

        private static void PriceBoardButtonPostfix(Thing thing, bool __result)
        {
            try
            {
                // 这个按钮是「画」的时候调的，每帧都会走一遍；
                // 只有真的被点中时 __result 才是 true，否则会把小窗一直弹出来。
                if (!__result) return;
                if (thing == null) return;
                if (thing.def == null || thing.def.category != ThingCategory.Item) return;
                Window_PriceBoard.ShowFor(thing);
            }
            catch { }
        }

        /// <summary>摊位里的货物不画出来（跟箱子一样：只看得到箱子贴图）。</summary>
        /// <summary>
        /// 给市场摊位一个"看不见的额外优先级"。
        /// 原版 StoreUtility.TryFindBestBetterStoreCellFor 只认「严格更高」的格子，
        /// 所以当摊位和仓库同为最高级时，永远轮不到摊位。
        /// 这里在"原版已经找不到更好格子"之后，再单独挑一个市场摊位的空格兜底，
        /// 相当于把摊位当成「比当前高一级」，但不改动任何可见的优先级数值。
        /// </summary>
        private static void HookMarketHaulBoost(HarmonyLib.Harmony harmony)
        {
            try
            {
                MethodInfo m = AccessTools.Method(typeof(StoreUtility), "TryFindBestBetterStoreCellFor", new[]
                {
                    typeof(Thing), typeof(Pawn), typeof(Map), typeof(StoragePriority),
                    typeof(Faction), typeof(IntVec3).MakeByRefType(), typeof(bool)
                });
                if (m == null)
                {
                    DevLog.Msg("[DSH-ECONOMY] 市场搬运加成：没找到 TryFindBestBetterStoreCellFor");
                    return;
                }
                harmony.Patch(m, prefix: new HarmonyMethod(
                    typeof(MarketPatch).GetMethod(nameof(MarketHaulBoostPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                DevLog.Msg("[DSH-ECONOMY] 市场搬运加成：已挂 TryFindBestBetterStoreCellFor");
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 市场搬运加成挂载失败：" + e.Message);
            }
        }

        /// <summary>原版找不到"更好格"时，用这个把货兜到市场摊位。</summary>
        private static bool MarketHaulBoostPrefix(Thing t, Pawn carrier, Map map, StoragePriority currentPriority,
            Faction faction, out IntVec3 foundCell, bool needAccurateResult, ref bool __result)
        {
            foundCell = IntVec3.Invalid;
            try
            {
                if (t == null || carrier == null || map == null) return true;
                if (!t.Spawned || t.def == null) return true;

                // 已经在摊位里了，别再折腾
                SlotGroup here = map.haulDestinationManager.SlotGroupAt(t.Position);
                if (here != null && here.parent is Building_Market) return true;

                // 只从「仓库里」往摊位搬：地上的散货、地块都不抢，
                // 否则摊位会变成吸尘器，把地图上所有东西都吸过来。
                if (here == null) return true;
                if (!carrier.CanReach(t, PathEndMode.ClosestTouch, Danger.Deadly)) return true;

                IntVec3 cell;
                if (!TryFindMarketCell(t, carrier, map, out cell)) return true;

                foundCell = cell;
                __result = true;
                return false;
            }
            catch (System.Exception)
            {
                return true;
            }
        }

        /// <summary>在所有市场摊位里挑一个最近、能收、有位置的格子。</summary>
        private static bool TryFindMarketCell(Thing t, Pawn carrier, Map map, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            List<Building> all = map.listerBuildings.allBuildingsColonist;
            float best = float.MaxValue;
            for (int i = 0; i < all.Count; i++)
            {
                Building_Market m = all[i] as Building_Market;
                if (m == null || !m.Spawned || m.Destroyed) continue;
                SlotGroup g = m.GetSlotGroup();
                if (g == null || g.CellsList == null) continue;

                StorageSettings st = m.GetStoreSettings();
                if (st == null || !st.AllowedToAccept(t)) continue;

                List<IntVec3> cells = g.CellsList;
                for (int c = 0; c < cells.Count; c++)
                {
                    IntVec3 cand = cells[c];
                    if (!StoreUtility.IsGoodStoreCell(cand, map, t, carrier, carrier.Faction)) continue;
                    float d = cand.DistanceToSquared(t.Position);
                    if (d < best) { best = d; cell = cand; }
                }
            }
            return cell.IsValid;
        }

        private static void HookHideMarketItems(HarmonyLib.Harmony harmony)        {
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder("[DSH-ECONOMY] 绘制方法候选：");
                int hooked = 0;
                MethodInfo[] ms = typeof(Thing).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < ms.Length; i++)
                {
                    MethodInfo m = ms[i];
                    if (m == null) continue;
                    // 只挂"画"的方法：名字以 Draw 开头，或叫 Print。
                    // 千万别按 GUI/Overlay/Label 模糊匹配——那会把 get_Label() 一起拦掉，物品名就空了。
                    bool isDraw = m.Name == "Print" || m.Name.StartsWith("Draw", System.StringComparison.Ordinal);
                    if (!isDraw) continue;
                    ParameterInfo[] ps = m.GetParameters();
                    sb.Append(" ").Append(m.Name).Append('(');
                    for (int k = 0; k < ps.Length; k++)
                    {
                        if (k > 0) sb.Append(", ");
                        sb.Append(ps[k].ParameterType.Name);
                    }
                    sb.Append(")");
                    try
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(
                            typeof(MarketPatch).GetMethod(nameof(HideMarketItemPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                        hooked++;
                    }
                    catch (System.Exception e2) { sb.Append("[挂钩失败:").Append(e2.Message).Append(']'); }
                }
                DevLog.Msg(sb.ToString());
                DevLog.Msg("[DSH-ECONOMY] 摊位货物隐藏：已挂 " + hooked + " 个绘制方法");

                // 让它点不到：只拦"返回 bool 且名字里带 Select"的属性（上次把 bool 结果写进 float 属性导致检视面板崩）
                System.Text.StringBuilder sb2 = new System.Text.StringBuilder("[DSH-ECONOMY] 选中相关成员：");
                int sel = 0;
                PropertyInfo[] props = typeof(Thing).GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < props.Length; i++)
                {
                    PropertyInfo pi = props[i];
                    if (pi == null || pi.Name.IndexOf("Select", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    MethodInfo g = pi.GetGetMethod(true);
                    sb2.Append(" ").Append(pi.Name).Append('(').Append(pi.PropertyType.Name).Append(')');
                    if (g == null || pi.PropertyType != typeof(bool)) continue;
                    try
                    {
                        harmony.Patch(g, prefix: new HarmonyMethod(
                            typeof(MarketPatch).GetMethod(nameof(MarketItemNotSelectable), BindingFlags.Static | BindingFlags.NonPublic)));
                        sel++;
                    }
                    catch (System.Exception e3) { sb2.Append("[失败:").Append(e3.Message).Append(']'); }
                }
                DevLog.Msg(sb2.ToString());
                DevLog.Msg("[DSH-ECONOMY] 摊位货物点不到：已挂 " + sel + " 个选中入口");

                // 点摊位时把"选中的货"换成"摊位本体"：鼠标点击最终都走 Selector.SelectableUnderMouse
                System.Text.StringBuilder sb3 = new System.Text.StringBuilder("[DSH-ECONOMY] Selector 方法：");
                int sel2 = 0;
                MethodInfo[] sms = typeof(Selector).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                for (int i = 0; i < sms.Length; i++)
                {
                    MethodInfo m = sms[i];
                    if (m == null) continue;
                    if (m.Name.IndexOf("Select", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    sb3.Append(" ").Append(m.Name).Append('(').Append(m.ReturnType.Name).Append(')');
                    if (m.Name != "SelectableUnderMouse" || m.GetParameters().Length != 0) continue;
                    try
                    {
                        harmony.Patch(m, postfix: new HarmonyMethod(
                            typeof(MarketPatch).GetMethod(nameof(SelectMarketBuildingPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                        sel2++;
                    }
                    catch (System.Exception e4) { sb3.Append("[失败:").Append(e4.Message).Append(']'); }
                }
                DevLog.Msg(sb3.ToString());
                DevLog.Msg("[DSH-ECONOMY] 点摊位选本体：已挂 " + sel2 + " 个");

                // 真正的落点：Selector.SelectUnderMouse（1.7 里没有 SelectableUnderMouse 了）
                MethodInfo sum = AccessTools.Method(typeof(Selector), "SelectUnderMouse", new System.Type[0]);
                if (sum != null)
                {
                    harmony.Patch(sum, prefix: new HarmonyMethod(
                        typeof(MarketPatch).GetMethod(nameof(SelectMarketUnderMousePrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                    DevLog.Msg("[DSH-ECONOMY] 点摊位选本体：已挂 SelectUnderMouse");
                }
                else Log.Warning("[DSH-ECONOMY] 点摊位选本体：没找到 Selector.SelectUnderMouse");

                // 更保险的一层：直接把"摊位上的货"从候选列表里剔掉，鼠标再怎么点都选不到它。
                System.Text.StringBuilder sb4 = new System.Text.StringBuilder("[DSH-ECONOMY] 候选列表过滤：");
                int filtered = 0;
                MethodInfo[] cms = typeof(Selector).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < cms.Length; i++)
                {
                    MethodInfo m = cms[i];
                    if (m == null) continue;
                    if (m.Name.IndexOf("SelectableObjects", System.StringComparison.Ordinal) < 0) continue;
                    if (m.GetParameters().Length != 0) continue;
                    if (!typeof(System.Collections.Generic.IEnumerable<Thing>).IsAssignableFrom(m.ReturnType)) continue;
                    sb4.Append(" ").Append(m.Name);
                    try
                    {
                        harmony.Patch(m, postfix: new HarmonyMethod(
                            typeof(MarketPatch).GetMethod(nameof(FilterMarketCandidates), BindingFlags.Static | BindingFlags.NonPublic)));
                        filtered++;
                    }
                    catch (System.Exception e5) { sb4.Append("[失败:").Append(e5.Message).Append(']'); }
                }
                DevLog.Msg(sb4.ToString());
                DevLog.Msg("[DSH-ECONOMY] 摊位货物不可点：已过滤 " + filtered + " 个候选列表");

                // 最后一道：不管从哪条路进来，最后都要走 Selector.Select / SelectInternal。
                // 只要选的是"摊位上的货"，就直接改选摊位本体。
                System.Text.StringBuilder sb5 = new System.Text.StringBuilder("[DSH-ECONOMY] 选中入口：");
                int sel3 = 0;
                MethodInfo[] fms = typeof(Selector).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                for (int i = 0; i < fms.Length; i++)
                {
                    MethodInfo m = fms[i];
                    if (m == null) continue;
                    if (m.Name != "Select" && m.Name != "SelectInternal") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length < 1) continue;
                    sb5.Append(" ").Append(m.Name).Append('(').Append(ps[0].ParameterType.Name).Append(')');
                    if (ps[0].ParameterType != typeof(Thing) && ps[0].ParameterType != typeof(object)) continue;
                    try
                    {
                        harmony.Patch(m, prefix: new HarmonyMethod(
                            typeof(MarketPatch).GetMethod(nameof(SelectMarketBuildingPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                        sel3++;
                    }
                    catch (System.Exception e6) { sb5.Append("[失败:").Append(e6.Message).Append(']'); }
                }
                DevLog.Msg(sb5.ToString());
                DevLog.Msg("[DSH-ECONOMY] 摊位货物不可点：已挂 " + sel3 + " 个 Select 入口");
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 隐藏摊位货物挂载失败：" + e.Message);
            }
        }

        private static bool HideMarketItemPrefix(Thing __instance)
        {
            return !IsOnMarket(__instance);
        }

        /// <summary>摊位里的货点不到（选不中）。只挂 bool 属性，绝不能挂 float 的。</summary>
        private static bool MarketItemNotSelectable(Thing __instance, ref bool __result)
        {
            if (IsOnMarket(__instance))
            {
                __result = false;
                return false;
            }
            return true;
        }

        private static int lastSelectLog;

        /// <summary>
        /// 最后一道闸：不管从哪条路进来，只要想选中的是"摊位上的货"，就改成选摊位本体。
        /// 日志证明点选拦截生效后货仍被选中，说明还有第二条路直接调 Select，这里一并堵住。
        /// </summary>
        private static bool SelectMarketBuildingPrefix(object obj, Selector __instance)
        {
            try
            {
                if (__instance == null) return true;
                Thing t = obj as Thing;
                if (t == null) return true;
                if (!IsOnMarket(t)) return true;
                Building b = MarketOf(t);
                if (b == null) return true;
                __instance.ClearSelection();
                __instance.Select((Thing)b);
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                if (now - lastSelectLog2 > 120)
                {
                    lastSelectLog2 = now;
                    DevLog.Msg("[MARKET] 选中改道：把货物换成了摊位 @ " + t.Position);
                }
                return false;
            }
            catch { return true; }
        }

        private static int lastSelectLog2;

        /// <summary>这堆货所在的摊位；不在摊位上就是 null。</summary>
        private static Building_Market MarketOf(Thing t)
        {
            try
            {
                if (t == null || !t.Spawned) return null;
                Map m = t.Map;
                if (m == null || m.haulDestinationManager == null) return null;
                SlotGroup g = m.haulDestinationManager.SlotGroupAt(t.Position);
                if (g == null) return null;
                return g.parent as Building_Market;
            }
            catch { return null; }
        }

        /// <summary>把"摊位上的货"从鼠标候选里剔掉：看不见的货也不该被点到。</summary>
        private static void FilterMarketCandidates(ref System.Collections.Generic.IEnumerable<Thing> __result)
        {
            try
            {
                if (__result == null) return;
                System.Collections.Generic.List<Thing> src = __result as System.Collections.Generic.List<Thing>;
                if (src == null) src = new System.Collections.Generic.List<Thing>(__result);
                bool any = false;
                for (int i = src.Count - 1; i >= 0; i--)
                {
                    if (IsOnMarket(src[i])) { src.RemoveAt(i); any = true; }
                }
                if (any) __result = src;
            }
            catch { }
        }

        private static bool SelectMarketUnderMousePrefix()
        {
            try
            {
                if (Find.DesignatorManager != null && Find.DesignatorManager.SelectedDesignator != null) return true;
                Map map = Find.CurrentMap;
                if (map == null) return true;
                IntVec3 c = UI.MouseCell();
                if (!c.InBounds(map)) return true;
                SlotGroup g = map.haulDestinationManager.SlotGroupAt(c);
                if (g == null || !(g.parent is Building_Market)) return true;
                bool hasItem = false;
                System.Collections.Generic.List<Thing> list = c.GetThingList(map);
                for (int i = 0; i < list.Count; i++)
                {
                    Thing t = list[i];
                    if (t != null && t.def != null && t.def.category == ThingCategory.Item) { hasItem = true; break; }
                }
                if (!hasItem) return true;
                Building b = g.parent as Building;
                if (b == null) return true;
                Find.Selector.ClearSelection();
                Find.Selector.Select((Thing)b);
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                if (now - lastSelectLog > 120)
                {
                    lastSelectLog = now;
                    DevLog.Msg("[MARKET] 点选拦截生效：把货物换成了摊位 @ " + c);
                }
                return false;
            }
            catch { return true; }
        }

        /// <summary>鼠标指到摊位的货上时，把目标换成摊位本体（点下去选的是摊位）。</summary>
        private static void SelectMarketBuildingPostfix(ref TargetInfo __result)
        {
            try
            {
                Thing t = __result.Thing;
                if (t == null || !IsOnMarket(t)) return;
                SlotGroup g = t.Map.haulDestinationManager.SlotGroupAt(t.Position);
                Building b = (g != null) ? (g.parent as Building) : null;
                if (b == null) return;
                __result = new TargetInfo(b);
            }
            catch { }
        }

        /// <summary>这件东西是不是摆在我们市场摊位的格子上。</summary>
        private static bool IsOnMarket(Thing t)
        {
            try
            {
                if (t == null || !t.Spawned) return false;
                ThingDef d = t.def;
                if (d == null || d.category != ThingCategory.Item) return false;
                Map m = t.Map;
                if (m == null || m.haulDestinationManager == null) return false;
                SlotGroup g = m.haulDestinationManager.SlotGroupAt(t.Position);
                if (g == null) return false;
                return g.parent is Building_Market;
            }
            catch { return false; }
        }

        /// <summary>任务正常成功结束后，就地结算订单。</summary>
        private static void EndJobPostfix(Pawn_JobTracker __instance, JobCondition condition)
        {
            Pawn p = __instance.pawn;
            if (p != null && p.IsColonist) TryNoteAnimalGather(__instance.curJob);
            EconomyRestock.NoteEnd(__instance.curJob, condition, p);

            if (p != null && p.IsColonist)
            {
                DevLog.Msg("[TRACE] 任务结束 " + p.LabelShort
                    + " 任务=" + (__instance.curJob == null ? "无" : __instance.curJob.def.defName)
                    + " 原因=" + condition + " 有订单=" + EconomyMarket.HasPending(p));
            }

            if (p == null || !p.IsColonist) return;

            // 不管成功还是被打断，只要东西已经在背包里就结账。
            // 以前这里有个洞：有订单、但任务被思考树打断 →
            //   - SettleUnpaidPickup 因为"有订单"被跳过
            //   - SettleFromInventory 因为"不是 Succeeded"也被跳过
            //   → 食物永远留在公有空间，标签永远打不上。
            bool settled = EconomyMarket.HasPending(p) && EconomyMarket.SettleFromInventory(p);

            if (!settled)
            {
                // 没有订单（或者订单结算失败/被打断）：这是"原版免费拿了"的路径，
                // 转私有 + 按件值扣满意度。
                SettlePackFoodDebt(p);
                SettleUnpaidPickup(p, __instance.curJob);
            }

            if (condition != JobCondition.Succeeded) return;

            if (EconomyMarket.HasPending(p)) EconomyMarket.SettleFromInventory(p);
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

        // ---------------- 食物去向：事件级探针 ----------------
        private static int lastFoodTrace = -99999;

        private static void FoodTraceLog(string text)
        {
            try
            {
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                if (now - lastFoodTrace < 30) return;
                lastFoodTrace = now;
                DevLog.Msg("[FOODT] " + text);
            }
            catch { }
        }

        private static void HookFoodTrace(Harmony harmony)
        {
            int hooked = 0;

            // A. 物品进入容器（背包/手上/箱子）—— 记录并堆后的实际堆
            try
            {
                foreach (MethodInfo m in typeof(ThingOwner<Thing>).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "TryAdd" || m.GetParameters().Length != 2) continue;
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(TryAddFoodTracePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    hooked++;
                }
            }
            catch { }

            // B. 物品落到地上 —— 搬运失败时会走这里
            try
            {
                foreach (MethodInfo m in typeof(GenPlace).GetMethods(BindingFlags.Public | BindingFlags.Static))
                {
                    if (m.Name != "TryPlaceThing") continue;
                    ParameterInfo[] ps = m.GetParameters();
                    if (ps.Length != 4) continue;
                    if (ps[0].ParameterType != typeof(Thing) || ps[1].ParameterType != typeof(IntVec3)) continue;
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(PlaceFoodTracePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    hooked++;
                }
            }
            catch { }

            // C. 物品从容器里被移除 —— 口粮消失会走这里
            try
            {
                foreach (MethodInfo m in typeof(ThingOwner<Thing>).GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (m.Name != "Remove" || m.GetParameters().Length != 1) continue;
                    if (m.GetParameters()[0].ParameterType != typeof(Thing)) continue;
                    harmony.Patch(m, postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(RemoveFoodTracePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                    hooked++;
                }
            }
            catch { }

            DevLog.Msg("[DSH-ECONOMY] 食物去向探针：已挂 " + hooked + " 个");
        }

        private static void TryAddFoodTracePostfix(ThingOwner<Thing> __instance, Thing __0, bool __result)
        {
            try
            {
                if (!__result || __instance == null || __0 == null) return;
                if (__0.def == null || !__0.def.IsIngestible) return;
                string who = "未知";
                if (__instance.Owner is Pawn_InventoryTracker it && it.pawn != null) who = "背包:" + it.pawn.LabelShort;
                else if (__instance.Owner is Pawn_CarryTracker ct && ct.pawn != null) who = "手上:" + ct.pawn.LabelShort;
                else if (__instance.Owner is Thing th) who = "容器:" + th.LabelShort;
                FoodTraceLog("进入 " + who + " " + __0.LabelShort + " ×" + __0.stackCount
                    + " 归属=" + OwnerName(__0));
            }
            catch { }
        }

        private static void PlaceFoodTracePostfix(Thing __0, IntVec3 __1, Map __2, bool __result)
        {
            try
            {
                if (!__result || __0 == null || __0.def == null || !__0.def.IsIngestible) return;
                FoodTraceLog("落地 " + __0.LabelShort + " ×" + __0.stackCount + " @ " + __1
                    + " 归属=" + OwnerName(__0));
            }
            catch { }
        }

        private static void RemoveFoodTracePostfix(ThingOwner<Thing> __instance, Thing __0)
        {
            try
            {
                if (__instance == null || __0 == null) return;
                if (__0.def == null || !__0.def.IsIngestible) return;
                if (!(__instance.Owner is Pawn_InventoryTracker it) || it.pawn == null) return;
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                sb.Append("离开 背包:").Append(it.pawn.LabelShort).Append(" ").Append(__0.LabelShort)
                  .Append(" ×").Append(__0.stackCount).Append(" 归属=").Append(OwnerName(__0));
                try
                {
                    System.Diagnostics.StackTrace st = new System.Diagnostics.StackTrace(1, false);
                    int frames = 0;
                    for (int f = 0; f < st.FrameCount && frames < 8; f++)
                    {
                        System.Reflection.MethodBase mb = st.GetFrame(f) == null ? null : st.GetFrame(f).GetMethod();
                        if (mb == null) continue;
                        if (mb.Name == "RemoveFoodTracePostfix") continue;
                        sb.Append(" <- ").Append(mb.Name);
                        frames++;
                    }
                }
                catch { }
                FoodTraceLog(sb.ToString());
            }
            catch { }
        }

        private static string OwnerName(Thing t)
        {
            try
            {
                Pawn o = EconomyPrivate.OwnerOf(t);
                return o == null ? "公共" : o.LabelShort;
            }
            catch { return "?"; }
        }

    }
}

