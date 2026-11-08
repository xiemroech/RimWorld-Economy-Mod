using System;
using System.Collections.Generic;
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

        /// <summary>
        /// 原料不同的食物不许合并：价格是按原料算的，混堆会让两种价格变成一种。
        /// 原版只看 def 相同就合并，正好和我们的定价依据冲突。
        /// </summary>
        private static void NoMergeDifferentIngredients(Thing __instance, Thing other, ref bool __result)
        {
            try
            {
                string a = EconomyValue.IngredientKey(__instance);
                if (a == null) return;                       // 没原料记录（不是食物）→ 交给原版
                string b = EconomyValue.IngredientKey(other);
                if (!string.Equals(a, b, System.StringComparison.Ordinal))
                    __result = false;                        // 用 false 覆盖原版结果
            }
            catch { }
        }
        
        /// <summary>
        /// 真正的合并动作。CanStackWith 只是"能不能"，地面物品并堆经常绕过它直接吞噬，
        /// 所以这里也要拦一次：prefix 返回 false = 不执行原方法 = 不合并。
        /// </summary>
        private static bool NoAbsorbDifferentIngredients(Thing __instance, Thing other, ref bool __result)
        {
            try
            {
                string a = EconomyValue.IngredientKey(__instance);
                if (a == null) return true;                  // 没原料记录（不是食物）→ 交给原版
                string b = EconomyValue.IngredientKey(other);
                if (!string.Equals(a, b, System.StringComparison.Ordinal))
                {
                    __result = false;
                    return false;
                }
            }
            catch { }
            return true;
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
                Log.Message(sb.ToString());
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
                Log.Message("[DSH-ECONOMY] 只搬不捡：已挂 " + hooked + " 个派活入口");
            }
            catch (Exception e) { Log.Warning("[DSH-ECONOMY] 只搬不捡挂载失败：" + e.Message); }
        }

        // ---------------- 思考树点名：谁派的拾取 ----------------
        // 栈追踪会被 JIT 内联吃掉调用层，"谁派的"查不出来。
        // 但每个 JobGiver 出任务时都会经过 TryIssueJobPackage，this 就是那个 JobGiver 实例，
        // 类名直接给出答案，不受内联影响。

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

        /// <summary>任务正常成功结束后，就地结算订单。</summary>
        private static void EndJobPostfix(Pawn_JobTracker __instance, JobCondition condition)
        {
            Pawn p = __instance.pawn;
            if (p != null && p.IsColonist) TryNoteAnimalGather(__instance.curJob);

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
    }
}