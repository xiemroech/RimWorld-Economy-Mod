using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 真正的 Need：注册进 pawn.needs 之后，原版和所有 UI mod 的需求列表都会自动显示它，
    /// 不需要去 patch 任何一个绘制函数。
    /// </summary>
    public class Need_Satisfaction : Need
    {
        public Need_Satisfaction(Pawn pawn) : base(pawn)
        {
        }

        public override void SetInitialLevel()
        {
            CurLevel = 0.5f;
        }

        public override void NeedInterval()
        {
            CurLevel = Mathf.Clamp01(EconomyLedger.GetSatisfaction(pawn) / 100f);
        }

        /// <summary>
        /// 直接把数值推到 UI 上。NeedInterval 的调用时机由原版控制（并非每次变化都会被调到），
        /// 所以满意度一改就主动同步一次，避免明细已经变了、进度条还停在 50%。
        /// </summary>
        public static void Sync(Pawn p)
        {
            if (p == null || p.needs == null) return;
            List<Need> needs = p.needs.AllNeeds;
            if (needs == null) return;
            for (int i = 0; i < needs.Count; i++)
            {
                Need_Satisfaction ns = needs[i] as Need_Satisfaction;
                if (ns != null)
                {
                    ns.CurLevel = Mathf.Clamp01(EconomyLedger.GetSatisfaction(p) / 100f);
                    return;
                }
            }
        }

        // 鼠标移到这一条上时显示的明细（和心情一样列出每一项的影响）
        public override string GetTipString()
        {
            return EconomySatisfaction.Breakdown(pawn);
        }
    }

    /// <summary>给每个殖民者挂上满意度需求。</summary>
    public static class SatisfactionNeedInjector
    {
        public static bool Patch(Harmony harmony)
        {
            try
            {
                var ctor = AccessTools.Constructor(typeof(Pawn_NeedsTracker), new[] { typeof(Pawn) });
                if (ctor == null)
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 Pawn_NeedsTracker 构造函数");
                    return false;
                }
                harmony.Patch(ctor, postfix: new HarmonyMethod(AccessTools.Method(typeof(SatisfactionNeedInjector), "Postfix")));

                // 老存档/已存在的小人不走构造函数，补一个生成时兜底
                var spawn = AccessTools.Method(typeof(Pawn), "SpawnSetup", new[] { typeof(Map), typeof(bool) });
                if (spawn != null)
                {
                    harmony.Patch(spawn, postfix: new HarmonyMethod(AccessTools.Method(typeof(SatisfactionNeedInjector), "PostfixSpawn")));
                }
                return true;
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 满意度需求注入失败：" + e.Message);
                return false;
            }
        }

        public static void Postfix(Pawn_NeedsTracker __instance, Pawn newPawn)
        {
            Ensure(__instance, newPawn);
        }

        public static void PostfixSpawn(Pawn __instance)
        {
            if (__instance?.needs != null) Ensure(__instance.needs, __instance);
        }

        private static void Ensure(Pawn_NeedsTracker tracker, Pawn pawn)
        {
            if (pawn == null || tracker == null || !pawn.IsColonist) return;

            NeedDef def = DefDatabase<NeedDef>.GetNamedSilentFail("DshEco_Satisfaction");
            if (def == null) return;

            List<Need> needs = tracker.AllNeeds;
            for (int i = 0; i < needs.Count; i++)
            {
                if (needs[i] != null && needs[i].def == def) return;   // 已有
            }

            Need need = new Need_Satisfaction(pawn);
            need.def = def;
            need.CurLevel = Mathf.Clamp01(EconomyLedger.GetSatisfaction(pawn) / 100f);
            tracker.AllNeeds.Add(need);
        }
    }
}
