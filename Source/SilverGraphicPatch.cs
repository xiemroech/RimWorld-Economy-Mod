using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 世界贴图兜底：ThingDef.graphic 的 getter 补丁。
    /// 只要读取的是白银这个 def，就直接返回我们构造好的银锭 Graphic，
    /// 不管别的 mod 有没有把字段改回去。
    /// </summary>
    public static class SilverGraphicPatch
    {
        public static void ThingPostfix(Thing __instance, ref Graphic __result)
        {
            if (__instance == null || __instance.def == null) return;
            if (EconomySilverGraphic.Cached == null) return;
            if (__instance.def.defName != "Silver") return;
            __result = EconomySilverGraphic.Cached;
        }

        public static void Postfix(ThingDef __instance, ref Graphic __result)
        {
            if (__instance == null) return;
            if (EconomySilverGraphic.Cached == null) return;
            if (__instance.defName != "Silver") return;
            __result = EconomySilverGraphic.Cached;
        }
    }
}
