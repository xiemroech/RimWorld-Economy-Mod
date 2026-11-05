using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 军人对其它精神状态也有强免疫（默认 98%），满意度跌破 15 后同样指数衰减。
    /// </summary>
    public static class EconomyMentalGuard
    {
        public static bool Patch(Harmony harmony)
        {
            System.Reflection.MethodInfo m = AccessTools.Method(
                typeof(MentalStateHandler), "TryStartMentalState",
                new[]
                {
                    typeof(MentalStateDef), typeof(string), typeof(Pawn), typeof(bool),
                    typeof(bool), typeof(bool), typeof(Pawn), typeof(string), typeof(bool)
                });
            if (m == null)
            {
                // 参数表变了就退回按名字找
                m = AccessTools.Method(typeof(MentalStateHandler), "TryStartMentalState");
            }
            if (m == null) return false;
            harmony.Patch(m, prefix: new HarmonyMethod(typeof(EconomyMentalGuard), nameof(Prefix)));
            return true;
        }

        private static bool Prefix(MentalStateHandler __instance, MentalStateDef stateDef, ref bool __result)
        {
            try
            {
                Pawn p = __instance != null ? __instance.pawn : null;
                if (p == null || stateDef == null) return true;
                if (!EconomyMilitary.IsSoldier(p)) return true;

                // 动乱状态本身（罢工/暴动/革命）允许，其余精神状态按免疫概率拦
                if (stateDef.defName != null && stateDef.defName.StartsWith("DshEco_")) return true;

                float leak = EconomyMilitary.ContagionMultiplier(p);   // 0.02 起步
                if (Rand.Chance(1f - leak))
                {
                    __result = false;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
