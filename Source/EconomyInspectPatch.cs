using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>选中殖民者时，在左下角信息栏显示工作满意度。</summary>
    public static class EconomyInspectPatch
    {
        public static bool Patch(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(Pawn), "GetInspectString");
                if (target == null)
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 Pawn.GetInspectString，满意度不显示在信息栏");
                    return false;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyInspectPatch), "Postfix")));
                return true;
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 信息栏补丁失败：" + e.Message);
                return false;
            }
        }

        public static void Postfix(Pawn __instance, ref string __result)
        {
            if (__instance == null || !__instance.IsColonist) return;
            if (EconomyLedger.Instance == null) return;

            string line = "工作满意度：" + EconomySatisfaction.Describe(__instance);
            __result = string.IsNullOrEmpty(__result) ? line : (__result + "\n" + line);
        }
    }
}
