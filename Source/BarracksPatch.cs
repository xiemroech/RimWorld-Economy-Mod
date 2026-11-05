using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 撤销原版「睡在多人宿舍」的心情惩罚：多人宿舍不再影响心情，
    /// 改成工作满意度里的「多人宿舍 −2」（见 EconomySatisfaction.BunkBedCount）。
    /// </summary>
    public static class BarracksPatch
    {
        public static bool Patch(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(MemoryThoughtHandler), "TryGainMemory",
                new[] { typeof(ThoughtDef), typeof(Pawn), typeof(Precept) });
            if (target == null)
            {
                Log.Warning("[DSH-ECONOMY] 多人宿舍：没找到 MemoryThoughtHandler.TryGainMemory");
                return false;
            }
            harmony.Patch(target,
                prefix: new HarmonyMethod(typeof(BarracksPatch).GetMethod(nameof(Prefix), BindingFlags.Static | BindingFlags.NonPublic)));
            return true;
        }

        private static bool Prefix(ThoughtDef def)
        {
            return def != ThoughtDefOf.SleptInBarracks;
        }
    }
}
