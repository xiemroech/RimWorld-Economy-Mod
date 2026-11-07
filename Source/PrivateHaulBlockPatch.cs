using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 禁用"搬运别人的私有物品"——包括右键强制搬运。
    /// 之前的 PawnCanAutomaticallyHaul 只挡自动搬运，右键指令还是能绕过去。
    /// </summary>
    public static class PrivateHaulBlockPatch
    {
        public static bool Prefix(Pawn p, Thing t, ref Job __result)
        {
            if (p == null || t == null) return true;

            Pawn owner = EconomyPrivate.OwnerOf(t);
            if (owner == null) return true;

            // 别人的私有物：一律不搬（含右键）
            if (owner != p)
            {
                __result = null;
                return false;
            }

            // 自己的私有物：优先放进自己名下的 mod 内部容器；
            // 这类容器不能用 HaulToCell 的单格判定，必须走 HaulToContainer。
            Thing container;
            if (EconomyPrivateStorageBehavior.FindOwnContainer(p, t, out container))
            {
                Job containerJob = HaulAIUtility.HaulToContainerJob(p, t, container);
                if (containerJob != null)
                {
                    __result = containerJob;
                    return false;
                }
            }

            // 没有可用的内部容器，再找普通储存格。
            IntVec3 cell;
            if (!EconomyPrivateStorageBehavior.FindOwnCell(p, t, out cell))
            {
                __result = null;
                return false;
            }

            Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, t, cell);
            job.count = Mathf.Min(t.stackCount, p.carryTracker.AvailableStackSpace(t.def));
            if (job.count <= 0)
            {
                __result = null;
                return false;
            }
            job.haulMode = HaulMode.ToCellStorage;
            __result = job;
            return false;
        }
    }
}
