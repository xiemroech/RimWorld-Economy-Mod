using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 1.7 的角色卡与需求页会遍历 pawn.needs.AllNeeds 自动画每一条 Need，
    /// 所以我们注册的原生 Need 已经是卡片/需求页里的原版行，不需要再手画。
    /// 这里只剩一件事：给角色卡左栏多留一行高度。
    /// 明细提示由 Need_Satisfaction.GetTipString() 提供。
    /// </summary>
    public static class NeedsSatisfactionPatch
    {
        public static bool Patch(Harmony harmony)
        {
            return true;
        }

        public static bool PatchCard(Harmony harmony)
        {
            try
            {
                var size = AccessTools.Method(typeof(CharacterCardUtility), "PawnCardSize", new[] { typeof(Pawn) });
                if (size == null)
                {
                    Log.Warning("[DSH-ECONOMY] 未找到 CharacterCardUtility.PawnCardSize");
                    return false;
                }
                harmony.Patch(size, postfix: new HarmonyMethod(AccessTools.Method(typeof(NeedsSatisfactionPatch), "PostfixCardSize")));
                return true;
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 角色卡高度补丁失败：" + e.Message);
                return false;
            }
        }

        public static void PostfixCardSize(Pawn pawn, ref Vector2 __result)
        {
            // 给角色卡左栏多留一行高度，放「工作满意度」
            __result = new Vector2(__result.x, __result.y + 26f);
        }
    }
}
