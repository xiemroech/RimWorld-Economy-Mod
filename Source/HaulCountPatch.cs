using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 「无法拾取…（物品栏已满）」：原版给搬运任务的数量只按堆叠空间算，不看重量，
    /// 200 多份肉一次全塞必然超重、拾取失败。这里按负重上限把数量压到搬得动的范围。
    /// </summary>
    public static class HaulCountPatch
    {
        public static void Postfix(Pawn p, Thing t, ref Job __result)
        {
            try
            {
                if (__result == null || p == null || t == null) return;
                int max = CarryableCount(p, t);
                if (max <= 0)
                {
                    // 一件都拿不动：别给一个必然失败的任务（会不停重试、看起来"有时能搬有时不能"）
                    __result = null;
                    return;
                }
                if (__result.count > max) __result.count = max;
            }
            catch { }
        }

        /// <summary>一次最多能搬几件：受堆叠空间与负重上限双重限制。</summary>
        public static int CarryableCount(Pawn p, Thing t)
        {
            if (t.def == null) return 0;
            int n = Mathf.Min(t.stackCount, p.carryTracker != null ? p.carryTracker.AvailableStackSpace(t.def) : t.stackCount);
            if (n <= 0) return 0;

            float unitMass = t.def.GetStatValueAbstract(StatDefOf.Mass);
            if (unitMass <= 0.0001f) return n;

            float free = MassUtility.Capacity(p) - MassUtility.GearAndInventoryMass(p);
            if (free <= 0f) return 0;

            int byMass = Mathf.FloorToInt(free / unitMass);
            return Mathf.Clamp(Mathf.Min(n, byMass), 0, n);
        }

        /// <summary>只读：原版找不到目的地时，把每个存放组的真实情况写进日志。</summary>
        public static void DumpOnFail(Pawn p, Thing t, Map map)
        {
            try
            {
                if (p == null || t == null || map == null) return;
                StoreFallback.DumpFailure(t, p, map);
            }
            catch { }
        }

        public static void DumpOnFailPostfix(Pawn p, Thing t, ref Job __result)
        {
            if (__result != null) return;
            try { DumpOnFail(p, t, p != null ? p.Map : null); } catch { }
        }
    }
}
