using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>折旧 = 生产建筑「故障」（原版 CompBreakdownable 模型，不掉耐久）。维修消耗材料，由建造工自动完成。</summary>
    public static class EconomyDepreciation
    {
        public const int Interval = 2500;
        private static int lastTick = -99999;

        public static bool IsProduction(Building b)
        {
            return b != null && b is IBillGiver giver && giver.BillStack != null;
        }

        public static IEnumerable<Building> ProductionBuildings(Map map)
        {
            foreach (Building b in map.listerBuildings.allBuildingsColonist)
            {
                if (b == null || b.Destroyed) continue;
                if (!IsProduction(b)) continue;
                yield return b;
            }
        }

        public static CompBreakdownable BreakdownOf(Thing t)
        {
            return t?.TryGetComp<CompBreakdownable>();
        }

        public static bool IsBroken(Thing t)
        {
            CompBreakdownable c = BreakdownOf(t);
            return c != null && c.BrokenDown;
        }

        public static void EnsureComps()
        {
            int n = 0;
            foreach (ThingDef def in DefDatabase<ThingDef>.AllDefs)
            {
                if (def?.thingClass == null) continue;
                // 只认真正的建筑：某些 mod 的生物/物品类也实现了 IBillGiver，
                // 之前会给动物、皮毛之类的 def 也挂上 CompBreakdownable，表现为"动物身上显示故障"
                if (def.category != ThingCategory.Building && !typeof(Building).IsAssignableFrom(def.thingClass)) continue;
                if (!typeof(IBillGiver).IsAssignableFrom(def.thingClass)) continue;
                if (def.comps == null) def.comps = new List<CompProperties>();
                if (def.comps.Any(c => c is CompProperties_Breakdownable)) continue;
                def.comps.Add(new CompProperties_Breakdownable());
                n++;
            }
            DevLog.Msg("[DSH-ECONOMY] 折旧：为 " + n + " 种生产建筑补上损坏组件");
        }

        /// <summary>清掉旧存档里被错误挂到非建筑（动物、皮毛等）上的损坏组件。</summary>
        public static void CleanupNonBuildings()
        {
            int n = 0;
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                foreach (Thing t in map.listerThings.AllThings)
                {
                    if (t is ThingWithComps twc && !(t is Building)) n += Strip(twc);
                }
                foreach (Pawn pw in map.mapPawns.AllPawnsSpawned)
                {
                    n += Strip(pw);
                }
            }
            if (n > 0) DevLog.Msg("[DSH-ECONOMY] 折旧清理：移除 " + n + " 个非建筑身上的损坏组件");
        }

        private static int Strip(ThingWithComps twc)
        {
            if (twc?.comps == null) return 0;
            int removed = twc.comps.RemoveAll(c => c is CompBreakdownable);
            return removed;
        }

        public static void Tick(Map map)
        {
            if (map == null) return;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null || !comp.depreciationEnabled) return;
            int now = Find.TickManager.TicksGame;
            if (now - lastTick < Interval) return;
            lastTick = now;

            float mtbDays = UnityEngine.Mathf.Max(0.5f, comp.depreciationMtbDays);
            foreach (Building b in ProductionBuildings(map))
            {
                CompBreakdownable c = BreakdownOf(b);
                if (c == null || c.BrokenDown) continue;
                if (b.Faction != Faction.OfPlayer) continue;
                if (Rand.MTBEventOccurs(mtbDays * GenDate.TicksPerDay, 1f, Interval))
                {
                    c.DoBreakdown();
                    DevLog.Msg("[DEPRECIATION] 故障：" + b.LabelShort + "（" + b.def.defName + "），待维修");
                }
            }
        }

        public static List<ThingDefCountClass> RepairCost(Thing t)
        {
            var list = new List<ThingDefCountClass>();
            ThingDef def = t?.def;
            if (def == null) return list;
            ThingDef stuff = t.Stuff;

            if (def.costList != null)
            {
                foreach (ThingDefCountClass c in def.costList)
                {
                    if (c?.thingDef == null) continue;
                    if (IsComponent(c.thingDef))
                    {
                        list.Add(new ThingDefCountClass(c.thingDef, 1));
                        return list;
                    }
                }
                foreach (ThingDefCountClass c in def.costList)
                {
                    if (c?.thingDef == null) continue;
                    int n = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.CeilToInt(c.count * 0.1f));
                    list.Add(new ThingDefCountClass(c.thingDef, n));
                }
            }
            if (def.costStuffCount > 0 && stuff != null)
            {
                int n = UnityEngine.Mathf.Max(1, UnityEngine.Mathf.CeilToInt(def.costStuffCount * 0.1f));
                list.Add(new ThingDefCountClass(stuff, n));
            }

            // 同一材质的条目合并（例：木屠宰台 木材10 与 材质木材8 其实是同一种东西）
            var merged = new List<ThingDefCountClass>();
            foreach (ThingDefCountClass c in list)
            {
                ThingDefCountClass hit = merged.FirstOrDefault(x => x.thingDef == c.thingDef);
                if (hit != null) hit.count += c.count;
                else merged.Add(new ThingDefCountClass(c.thingDef, c.count));
            }
            return merged;
        }

        private static bool IsComponent(ThingDef d)
        {
            return d.defName == "ComponentIndustrial" || d.defName == "ComponentSparePart"
                || d.defName == "ComponentSpacer" || d.defName.Contains("Component");
        }

        public static string DumpCostList(ThingDef def)
        {
            if (def == null) return "无";
            var parts = new List<string>();
            if (def.costList != null)
            {
                foreach (ThingDefCountClass c in def.costList)
                {
                    if (c?.thingDef != null) parts.Add(c.thingDef.defName + "x" + c.count);
                }
            }
            if (def.costStuffCount > 0) parts.Add("stuff x" + def.costStuffCount);
            return parts.Count == 0 ? "空" : string.Join(",", parts.ToArray());
        }

        public static string CostLabel(Thing t)
        {
            var cost = RepairCost(t);
            if (cost.Count == 0) return "无需材料";
            return string.Join("、", cost.Select(c => c.thingDef.LabelCap + "×" + c.count).ToArray());
        }

        private static int CountAvailable(Map map, ThingDef def)
        {
            int n = 0;
            foreach (Thing thing in map.listerThings.ThingsOfDef(def))
            {
                if (thing.IsForbidden(Faction.OfPlayer)) continue;
                if (EconomyPrivate.OwnerOf(thing) != null) continue;
                n += thing.stackCount;
            }
            return n;
        }

        public static bool CanAfford(Map map, Thing t)
        {
            foreach (ThingDefCountClass c in RepairCost(t))
            {
                if (CountAvailable(map, c.thingDef) < c.count) return false;
            }
            return true;
        }

        public static bool TryRepair(Map map, Thing t, out string reason)
        {
            return TryRepair(map, t, null, out reason);
        }

        public static bool TryRepair(Map map, Thing t, Pawn pawn, out string reason)
        {
            reason = null;
            if (map == null || t == null || t.Destroyed) { reason = "目标无效"; return false; }
            CompBreakdownable c = BreakdownOf(t);
            if (c == null) { reason = "这个建筑没有损坏组件"; return false; }
            if (!c.BrokenDown) { reason = "没有故障"; return false; }

            var cost = RepairCost(t);
            int InInv(ThingDef d)
            {
                if (pawn?.inventory == null) return 0;
                int n = 0;
                foreach (Thing x in pawn.inventory.innerContainer)
                {
                    if (x.def == d) n += x.stackCount;
                }
                return n;
            }
            foreach (ThingDefCountClass need in cost)
            {
                if (InInv(need.thingDef) + CountAvailable(map, need.thingDef) < need.count)
                {
                    reason = "缺少 " + need.thingDef.LabelCap + "×" + need.count;
                    return false;
                }
            }
            foreach (ThingDefCountClass need in cost)
            {
                int left = need.count;
                if (pawn?.inventory != null)
                {
                    var inv = pawn.inventory.innerContainer;
                    for (int i = inv.Count - 1; i >= 0 && left > 0; i--)
                    {
                        Thing x = inv[i];
                        if (x.def != need.thingDef) continue;
                        int take = UnityEngine.Mathf.Min(left, x.stackCount);
                        x.stackCount -= take;
                        if (x.stackCount <= 0) x.Destroy(DestroyMode.Vanish);
                        left -= take;
                    }
                }
                while (left > 0)
                {
                    Thing stack = map.listerThings.ThingsOfDef(need.thingDef)
                        .FirstOrDefault(x => !x.IsForbidden(Faction.OfPlayer) && EconomyPrivate.OwnerOf(x) == null);
                    if (stack == null) { reason = "缺少 " + need.thingDef.LabelCap; return false; }
                    int take = UnityEngine.Mathf.Min(left, stack.stackCount);
                    stack.stackCount -= take;
                    if (stack.stackCount <= 0) stack.Destroy(DestroyMode.Vanish);
                    left -= take;
                }
            }

            c.Notify_Repaired();
            DevLog.Msg("[DEPRECIATION] 修好 " + t.LabelShort + "（" + t.def.defName + "，材质=" + (t.Stuff?.defName ?? "无")
                + "）消耗：" + CostLabel(t) + "；原配方=" + DumpCostList(t.def));

            if (t.def.repairEffect != null)
            {
                Effecter eff = t.def.repairEffect.Spawn();
                eff.Trigger(t, t, -1);
                eff.Cleanup();
            }
            return true;
        }

        public static int BreakAll(Map map)
        {
            if (map == null) return 0;
            int n = 0;
            foreach (Building b in ProductionBuildings(map))
            {
                CompBreakdownable c = BreakdownOf(b);
                if (c == null || c.BrokenDown) continue;
                c.DoBreakdown();
                n++;
            }
            DevLog.Msg("[DEPRECIATION] 沙盒：已让 " + n + " 个生产建筑故障");
            return n;
        }

        public static int RepairAll(Map map)
        {
            if (map == null) return 0;
            int n = 0;
            foreach (Building b in ProductionBuildings(map))
            {
                CompBreakdownable c = BreakdownOf(b);
                if (c == null || !c.BrokenDown) continue;
                c.Notify_Repaired();
                n++;
            }
            DevLog.Msg("[DEPRECIATION] 沙盒：已修好 " + n + " 个生产建筑");
            return n;
        }

        public static void DumpStats(Map map)
        {
            if (map == null) return;
            int total = 0, broken = 0;
            foreach (Building b in ProductionBuildings(map))
            {
                total++;
                if (IsBroken(b)) broken++;
            }
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            DevLog.Msg("[DEPRECIATION] 生产建筑=" + total + " 故障中=" + broken
                + " 平均无故障天数=" + (comp?.depreciationMtbDays ?? 0f));
        }
    }

    /// <summary>故障的工作台禁止排产、不进原版维修列表；原版零部件修故障让位给本 mod。</summary>
    public static class DepreciationGizmoPatch
    {
        public static void Patch(HarmonyLib.Harmony harmony)
        {
            var dobill = HarmonyLib.AccessTools.Method(typeof(WorkGiver_DoBill), "JobOnThing",
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            if (dobill != null)
            {
                harmony.Patch(dobill, prefix: new HarmonyLib.HarmonyMethod(
                    HarmonyLib.AccessTools.Method(typeof(DepreciationGizmoPatch), nameof(DoBillPrefix))));
            }

            var repairable = HarmonyLib.AccessTools.Method(typeof(ListerBuildingsRepairable), "RepairableBuildings");
            if (repairable != null)
            {
                harmony.Patch(repairable, postfix: new HarmonyLib.HarmonyMethod(
                    HarmonyLib.AccessTools.Method(typeof(DepreciationGizmoPatch), nameof(RepairablePostfix))));
            }

            var notify = HarmonyLib.AccessTools.Method(typeof(CompBreakdownable), "Notify_Repaired");
            if (notify != null)
            {
                harmony.Patch(notify, prefix: new HarmonyLib.HarmonyMethod(
                    HarmonyLib.AccessTools.Method(typeof(DepreciationGizmoPatch), nameof(NotifyRepairedPrefix))));
            }

            var fixHas = HarmonyLib.AccessTools.Method(typeof(WorkGiver_FixBrokenDownBuilding), "HasJobOnThing",
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            var fixJob = HarmonyLib.AccessTools.Method(typeof(WorkGiver_FixBrokenDownBuilding), "JobOnThing",
                new[] { typeof(Pawn), typeof(Thing), typeof(bool) });
            if (fixHas != null)
            {
                harmony.Patch(fixHas, prefix: new HarmonyLib.HarmonyMethod(
                    HarmonyLib.AccessTools.Method(typeof(DepreciationGizmoPatch), nameof(FixBrokenHasPrefix))));
            }
            if (fixJob != null)
            {
                harmony.Patch(fixJob, prefix: new HarmonyLib.HarmonyMethod(
                    HarmonyLib.AccessTools.Method(typeof(DepreciationGizmoPatch), nameof(FixBrokenJobPrefix))));
            }

            DevLog.Msg("[DSH-ECONOMY] 折旧：故障禁产：" + (dobill != null ? "已挂载" : "未找到 WorkGiver_DoBill")
                + "，原版免费维修屏蔽：" + (repairable != null ? "已挂载" : "未找到 RepairableBuildings")
                + "，零部件修故障让位：" + (fixHas != null ? "已挂载" : "未找到 WorkGiver_FixBrokenDownBuilding"));
        }

        public static void RepairablePostfix(ref List<Thing> __result)
        {
            if (__result == null || __result.Count == 0) return;
            __result.RemoveAll(t => t is Building b && EconomyDepreciation.IsProduction(b) && EconomyDepreciation.IsBroken(b));
        }

        public static bool DoBillPrefix(Thing thing)
        {
            return !EconomyDepreciation.IsBroken(thing);
        }

        public static void NotifyRepairedPrefix(CompBreakdownable __instance)
        {
            Thing t = __instance?.parent;
            if (t == null || !EconomyDepreciation.IsProduction(t as Building)) return;
            var st = new System.Diagnostics.StackTrace(1, false);
            var sb = new StringBuilder();
            for (int i = 0; i < st.FrameCount && i < 8; i++)
            {
                var m = st.GetFrame(i)?.GetMethod();
                if (m == null) continue;
                sb.Append(m.DeclaringType?.Name).Append('.').Append(m.Name).Append(" < ");
            }
            Log.Warning("[REPAIR-TRACE] " + t.LabelShort + " 由谁修好：" + sb);
        }

        public static bool FixBrokenHasPrefix(Thing t, ref bool __result)
        {
            if (t is Building b && EconomyDepreciation.IsProduction(b) && EconomyDepreciation.IsBroken(b))
            {
                __result = false;
                return false;
            }
            return true;
        }

        public static bool FixBrokenJobPrefix(Thing t, ref Job __result)
        {
            if (t is Building b && EconomyDepreciation.IsProduction(b) && EconomyDepreciation.IsBroken(b))
            {
                __result = null;
                return false;
            }
            return true;
        }
    }
}

