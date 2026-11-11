using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 装备筛选器：挑衣服时按产权排序。
    ///
    /// 规则（从强到弱）：
    /// 1. 别人的私有衣物——直接判最低分，思考阶段就不会去挑（否则会反复穿衣）；
    /// 2. 自己的私有衣物——加一笔偏好分，优先穿自己的；
    /// 3. 公有衣物——只有在"这个人还没有同层私有衣物"时才照常参与评比，
    ///    一旦有了私有衣物就让位，避免刚发的新衣服躺在仓库里、人还穿着公家的。
    /// </summary>
    public static class ApparelFilter
    {
        private const float OwnBonus = 30f;
        private const float PublicPenalty = 30f;

        public static bool Patch(Harmony harmony)
        {
            int hooked = 0;
            foreach (MethodInfo m in typeof(JobGiver_OptimizeApparel).GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (m == null || m.ReturnType != typeof(float)) continue;
                if (m.Name.IndexOf("ApparelScore", System.StringComparison.Ordinal) < 0) continue;
                ParameterInfo[] ps = m.GetParameters();
                if (ps.Length != 2) continue;
                if (ps[0].ParameterType != typeof(Pawn) || ps[1].ParameterType != typeof(Apparel)) continue;

                harmony.Patch(m, postfix: new HarmonyMethod(
                    typeof(ApparelFilter).GetMethod(nameof(ScorePostfix), BindingFlags.Static | BindingFlags.NonPublic)));
                hooked++;
            }
            DevLog.Msg("[DSH-ECONOMY] 装备筛选器：已接管 " + hooked + " 个穿衣打分方法");
            return hooked > 0;
        }

        private static void ScorePostfix(Pawn pawn, Apparel ap, ref float __result)
        {
            if (pawn == null || ap == null || ap.def == null || ap.def.apparel == null) return;
            Pawn owner = EconomyPrivate.OwnerOf(ap);

            if (owner != null && owner != pawn)
            {
                if (__result > -100f) __result = -1000f;
                return;
            }

            if (owner == pawn)
            {
                __result += OwnBonus;
                return;
            }

            // 公有衣物：这个人已经有同层的私有衣物时，让位
            if (HasPrivateOfLayer(pawn, ap.def.apparel.LastLayer))
            {
                __result -= PublicPenalty;
            }
        }

        /// <summary>这个人手上/仓库里有没有同一层（同部位）的私有衣物。</summary>
        private static bool HasPrivateOfLayer(Pawn p, ApparelLayerDef layer)
        {
            if (layer == null) return false;
            Map map = p.Map ?? p.MapHeld;
            if (map == null) return false;
            List<Thing> list = EconomyPrivate.OwnedItems(p, map);
            if (list == null) return false;
            for (int i = 0; i < list.Count; i++)
            {
                Thing t = list[i];
                if (t == null || t.Destroyed) continue;
                Apparel a = t as Apparel;
                if (a == null || a.def == null || a.def.apparel == null) continue;
                if (a.def.apparel.LastLayer == layer) return true;
            }
            return false;
        }
    }
}
