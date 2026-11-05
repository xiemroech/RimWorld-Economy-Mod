using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 复用原版狂暴的攻击逻辑，只把"敌对目标"筛一遍：
    /// 只对"当前锁定的富人"敌对，其他人都不是敌人，于是原版 AI 会自然去攻击那个人。
    /// </summary>
    public static class EconomyBerserkTargetPatch
    {
        public static bool Patch(Harmony harmony)
        {
            try
            {
                var target = AccessTools.Method(typeof(MentalState_Berserk), "ForceHostileTo", new[] { typeof(Thing) });
                if (target == null)
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 MentalState_Berserk.ForceHostileTo(Thing)");
                    return false;
                }
                harmony.Patch(target, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyBerserkTargetPatch), "Postfix")));

                var fw = AccessTools.Method(typeof(PawnUtility), "ForceWait");
                if (fw != null)
                {
                    harmony.Patch(fw, prefix: new HarmonyMethod(AccessTools.Method(typeof(EconomyBerserkTargetPatch), "PrefixForceWait")));
                }
                return true;
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 狂暴目标过滤补丁失败：" + e.Message);
                return false;
            }
        }

        public static bool PrefixForceWait(Pawn pawn)
        {
            // 暴动/革命者拒绝被强制等待（仪式、看守、穿衣等），否则会被按在原地
            return !(pawn != null && pawn.InMentalState && pawn.MentalStateDef != null
                && pawn.MentalStateDef.defName.StartsWith("DshEco_"));
        }

        public static void Postfix(MentalState_Berserk __instance, Thing t, ref bool __result)
        {
            if (!__result) return;
            Pawn self = __instance != null ? __instance.pawn : null;
            Pawn other = t as Pawn;
            if (self == null) return;
            if (other == null) { __result = false; return; }   // 建筑/物品/炮塔一律不敌对

            // 硬排除同志：即使目标选错，也绝不打其他暴动/革命者
            if (other.InMentalState && other.MentalStateDef != null
                && other.MentalStateDef.defName.StartsWith("DshEco_"))
            {
                __result = false;
                return;
            }

            // 只在社会性冲突状态里生效（暴动/革命），普通狂暴不动
            // 直接看精神状态定义名，不依赖我们自己的集合
            MentalStateDef msDef = self.MentalStateDef;
            if (msDef == null || !msDef.defName.StartsWith("DshEco_")) return;

            Pawn target = EconomyUnrest.LockedTargetOf(self);
            if (target == null) { __result = false; return; }   // fail-closed：没目标就谁都不敌对
            if (t != target) __result = false;                  // 建筑/炮塔/其他人一律不敌对
        }
    }
}
