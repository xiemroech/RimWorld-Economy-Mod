using RimWorld;
using Verse;
using UnityEngine;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 手动「优先搬运」失败时的兜底：原版找不到格子就返回 null，玩家只能干瞪眼。
    /// 这里自己找一个能放的格子（公共货→公共格；私有货→物主自己的格），把任务补出来。
    /// </summary>
    public static class HaulJobFallback
    {
        /// <summary>容器只能再收 N 件时，把任务数量压到 N，避免整叠塞进去被 mod 当溢出弹出来。</summary>
        private static void ClampToContainer(Job job, Thing item, Thing container)
        {
            try
            {
                ThingOwner inner = container != null ? container.TryGetInnerInteractableThingOwner() : null;
                if (inner == null) return;
                int can = inner.GetCountCanAccept(item);
                if (can <= 0) return;
                if (job.count > can) job.count = can;
            }
            catch { }
        }

        public static void Postfix(Pawn p, Thing t, bool forced, ref Job __result)
        {
            if (!forced || __result != null || p == null || t == null) return;
            try
            {
                if (t.IsForbidden(p) && !forced) return;

                // 先按“自动搬运”的非强制路径再算一次。手动 forced 会改变
                // CurrentStoragePriorityOf/目的地筛选；自动能搬时，这里应得到同一结果。
                Job autoJob = HaulAIUtility.HaulToStorageJob(p, t, false);
                if (autoJob != null)
                {
                    __result = autoJob;
                    DevLog.Msg("[HAULFORCE] 手动失败，自动路径成功：" + t.LabelShort
                        + " -> " + autoJob.targetB.ToStringSafe());
                    return;
                }
                Map map = p.MapHeld ?? p.Map;
                if (map == null) return;

                Pawn owner = EconomyPrivate.OwnerOf(t);
                IntVec3 cell = IntVec3.Invalid;
                bool ok;

                // 私有物优先找物主名下的内部容器（mod 箱子的容量在 ThingOwner 里）。
                Thing ownContainer;
                Pawn storageOwner = owner ?? p;
                if (owner != null && owner.Map == map
                    && EconomyPrivateStorageBehavior.FindOwnContainer(storageOwner, t, out ownContainer))
                {
                    Job containerJob = HaulAIUtility.HaulToContainerJob(p, t, ownContainer);
                    if (containerJob != null)
                    {
                        ClampToContainer(containerJob, t, ownContainer);
                        __result = containerJob;
                        return;
                    }
                }

                if (owner == null) ok = StoreFallback.TryFindPublicCell(t, p, map, out cell);
                else if (owner == p) ok = EconomyPrivateStorageBehavior.FindOwnCell(p, t, out cell);
                else
                {
                    // 别人的私有物：允许别人帮它搬回**物主自己的仓位**（协作搬运），只是不能进第三人/公共格
                    ok = owner.Map == map && EconomyPrivateStorageBehavior.FindOwnCell(owner, t, out cell);
                }

                if (!ok && owner == null && forced)
                {
                    // 原版 TryFindBestBetterStorageFor 还会找非 SlotGroup 容器；
                    // 手动兜底之前只扫了 SlotGroup，可能正是这里漏掉。
                    Thing container;
                    if (StoreFallback.TryFindPublicContainer(t, p, map, out container))
                    {
                        Job containerJob = HaulAIUtility.HaulToContainerJob(p, t, container);
                        if (containerJob != null)
                        {
                            ClampToContainer(containerJob, t, container);
                            __result = containerJob;
                            DevLog.Msg("[HAULFORCE] 手动强制公共容器："
                                + t.LabelShort + " -> " + container.ToStringSafe());
                            return;
                        }
                    }

                    // 最后才允许无视过滤器，强行放进公共空格。
                    if (StoreFallback.TryFindPublicCellIgnoringFilter(t, p, map, out cell))
                    {
                        ok = true;
                        DevLog.Msg("[HAULFORCE] 过滤器无匹配，手动强制公共空格："
                            + t.LabelShort + " -> " + cell);
                    }
                }

                if (!ok)
                {
                    if (owner == null)
                    {
                        string why;
                        StoreFallback.TryFindPublicCell(t, p, map, out cell, out why);
                        DevLog.Msg("[HAULWHY] 手动兜底失败（" + t.LabelShort + "）：" + why);
                    }
                    return;
                }

                // 最后一道硬校验：JobDriver_HaulToCell 出发前会检查 targetB 是否真的能收这件物品。
                // 过滤器的“无视”不能绕过这个检查，否则任务会在原地失败，看起来就是假搬运。
                if (!cell.IsValidStorageFor(map, t) || !p.CanReserve(cell))
                {
                    DevLog.Msg("[HAULFORCE] 目标格实际不接受，已取消（" + t.LabelShort + " -> " + cell + "）");
                    return;
                }

                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, t, cell);
                job.count = Mathf.Min(t.stackCount, p.carryTracker.AvailableStackSpace(t.def));
                if (job.count <= 0) return;
                job.haulMode = HaulMode.ToCellStorage;
                __result = job;
            }
            catch (System.Exception e)
            {
                Log.Warning("[HAULWHY] 手动兜底异常（" + t.LabelShort + "）：" + e.Message);
            }
        }
    }
}

