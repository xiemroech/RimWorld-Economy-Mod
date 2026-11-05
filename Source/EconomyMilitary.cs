using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 军人制度。
    /// 勾选后，被标记的殖民者：
    ///  · 禁掉全部工作类型（只保留战斗/征召），几乎完全脱产；
    ///  · 对罢工/暴动/革命的传染有 98% 免疫；
    ///  · 不主动传播动乱；
    ///  · 工作满意度低于 15 时免疫指数衰减，越低越容易被传染（最低可到负，几乎必中）。
    /// </summary>
    public static class EconomyMilitary
    {
        public const float BaseImmunity = 0.98f;
        public const float BreakdownThreshold = 15f;

        private static MapEconomyComponent Comp(Map map)
        {
            return map != null ? map.GetComponent<MapEconomyComponent>() : null;
        }

        public static bool Enabled(Map map)
        {
            MapEconomyComponent c = Comp(map);
            return c != null && c.militaryEnabled;
        }

        public static bool IsSoldier(Pawn p)
        {
            if (p == null || p.Map == null) return false;
            MapEconomyComponent c = Comp(p.Map);
            if (c == null || !c.militaryEnabled) return false;
            return c.soldiers != null && c.soldiers.Contains(p);
        }

        /// <summary>传染倍率：0 = 完全免疫。满意度跌破 15 后按指数增长。</summary>
        /// <summary>军人对动乱的免疫比例（可以为负：负数表示比普通人更容易被卷进去）。</summary>
        public static float Immunity(Pawn p)
        {
            float sat = EconomyLedger.GetSatisfaction(p);
            if (sat >= BreakdownThreshold) return BaseImmunity;        // 0.98
            float depth = BreakdownThreshold - sat;                    // 15 → 0，0 → 15
            if (depth <= 10f)
            {
                // 15 → 98%，10 → 70%，5 → 42%（每点 -5.6%）
                return BaseImmunity - 0.056f * depth;
            }
            // 10 以下是崩盘段：5 → 42%，0 → -30%（每点 -14.4%）
            return 0.42f - 0.144f * (depth - 10f);
        }

        public static float ContagionMultiplier(Pawn p)
        {
            float immunity = Immunity(p);
            // 免疫 98% → 传染 0.02；免疫 0 → 传染 1.0；免疫 -30% → 传染 1.3（比常人更危险）
            return Mathf.Clamp(1f - immunity, 0.02f, 2.5f);
        }

        public static void SetSoldier(Pawn p, bool on)
        {
            if (p == null || p.Map == null) return;
            MapEconomyComponent c = Comp(p.Map);
            if (c == null) return;
            if (c.soldiers == null) c.soldiers = new List<Pawn>();
            if (on)
            {
                if (!c.soldiers.Contains(p)) c.soldiers.Add(p);
                ApplyWork(p, true);
            }
            else
            {
                c.soldiers.Remove(p);
                ApplyWork(p, false);
            }
        }

        /// <summary>军人禁掉所有工作；退出时恢复默认（全开）。</summary>
        public static void ApplyWork(Pawn p, bool soldier)
        {
            try
            {
                if (p.workSettings == null) return;
                if (soldier)
                {
                    // 真正的禁用：所有工作类型的优先级都设为 0（不是只关“当前开着”的）
                    p.workSettings.DisableAll();
                    List<WorkTypeDef> allTypes = DefDatabase<WorkTypeDef>.AllDefsListForReading;
                    for (int i = 0; i < allTypes.Count; i++)
                    {
                        WorkTypeDef wt = allTypes[i];
                        if (wt == null) continue;
                        if (wt == WorkTypeDefOf.Firefighter) continue;
                        if (wt.defName == "BasicWorker") continue;
                        p.workSettings.SetPriority(wt, 0);
                    }
                    // 军人保留：灭火 + 基础（开关、清理等）
                    p.workSettings.SetPriority(WorkTypeDefOf.Firefighter, 3);
                    WorkTypeDef basic = DefDatabase<WorkTypeDef>.GetNamedSilentFail("BasicWorker");
                    if (basic != null) p.workSettings.SetPriority(basic, 3);
                }
                else p.workSettings.EnableAndInitialize();
            }
            catch { }
        }

        /// <summary>真正的禁用：军人的工作类型对外一律视为“未启用”（除了灭火与基础）。</summary>
        public static bool WorkIsActivePrefix(Pawn_WorkSettings __instance, WorkTypeDef w, ref bool __result)
        {
            try
            {
                Pawn p = __instance.pawn;
                if (p == null || !IsSoldier(p) || w == null) return true;
                if (w == WorkTypeDefOf.Firefighter || w.defName == "BasicWorker") return true;
                __result = false;
                return false;
            }
            catch { }
            return true;
        }



        /// <summary>用原版自己的“禁用工作类型”机制：军人除灭火/基础外全部禁用。
        /// 返回新的 List，避免污染 Pawn 内部缓存，取消军人后自动恢复。</summary>
        public static void GetDisabledWorkTypesPostfix(Pawn __instance, ref List<WorkTypeDef> __result)
        {
            try
            {
                if (__instance == null || !IsSoldier(__instance)) return;
                List<WorkTypeDef> all = DefDatabase<WorkTypeDef>.AllDefsListForReading;
                List<WorkTypeDef> list = __result != null ? new List<WorkTypeDef>(__result) : new List<WorkTypeDef>();
                for (int i = 0; i < all.Count; i++)
                {
                    WorkTypeDef wt = all[i];
                    if (wt == null) continue;
                    if (wt == WorkTypeDefOf.Firefighter || wt.defName == "BasicWorker") continue;
                    if (!list.Contains(wt)) list.Add(wt);
                }
                __result = list;
            }
            catch { }
        }

        /// <summary>读优先级时对军人返回 0（界面/工作分配器都会读这里）。</summary>
        public static void GetPriorityPostfix(Pawn_WorkSettings __instance, WorkTypeDef w, ref int __result)
        {
            try
            {
                if (__result <= 0) return;
                Pawn p = __instance.pawn;
                if (p == null || !IsSoldier(p) || w == null) return;
                if (w == WorkTypeDefOf.Firefighter || w.defName == "BasicWorker") return;
                __result = 0;
            }
            catch { }
        }

        /// <summary>写优先级时对军人一律改写成 0（保留灭火/基础），防止被 UI 或其它 mod 重新打开。</summary>
        public static bool SetPriorityPrefix(Pawn_WorkSettings __instance, WorkTypeDef w, int priority)
        {
            try
            {
                Pawn p = __instance.pawn;
                if (priority <= 0 || p == null || !IsSoldier(p) || w == null) return true;
                if (w == WorkTypeDefOf.Firefighter || w.defName == "BasicWorker") return true;
                __instance.SetPriority(w, 0);
                return false;
            }
            catch { }
            return true;
        }

        /// <summary>读档/换图后重新套用（workSettings 可能被重建）。</summary>
        public static void ReapplyAll(Map map)
        {
            MapEconomyComponent c = Comp(map);
            if (c == null || !c.militaryEnabled || c.soldiers == null) return;
            for (int i = c.soldiers.Count - 1; i >= 0; i--)
            {
                Pawn p = c.soldiers[i];
                if (p == null || p.Dead || p.Map != map) { c.soldiers.RemoveAt(i); continue; }
                ApplyWork(p, true);
            }
        }
    }
}
