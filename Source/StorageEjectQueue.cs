using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 违规物品的"延后弹出"队列。
    ///
    /// 不能在 Notify_Added / Notify_ReceivedThing / SpawnSetup 里直接 DeSpawn：
    /// 那些方法返回后调用方还会继续用这个 Thing（Adaptive Storage 等 mod 会直接崩）。
    /// 所以先记下来，等 MapComponentTick 里统一处理。
    /// </summary>
    public static class StorageEjectQueue
    {
        private static readonly List<Thing> pending = new List<Thing>();

        public static void Enqueue(Thing t)
        {
            Enqueue(t, "未标注");
        }

        /// <summary>带原因入队：日志里能看出是哪条规则把人东西弹出去的。</summary>
        public static void Enqueue(Thing t, string reason)
        {
            if (t == null) return;
            if (!pending.Contains(t))
            {
                pending.Add(t);
                DevLog.Msg("[EJECT] 入队：" + t.LabelShort + " 原因=" + reason);
            }
        }

        public static void Clear()
        {
            pending.Clear();
        }

        public static void Tick(Map map)
        {
            if (pending.Count == 0) return;

            int budget = 8;
            for (int i = pending.Count - 1; i >= 0 && budget > 0; i--)
            {
                Thing t = pending[i];
                pending.RemoveAt(i);
                if (t == null || t.Destroyed || !t.Spawned) continue;
                if (t.Map != map) continue;

                Pawn itemOwner = EconomyPrivate.OwnerOf(t);
                if (itemOwner == null) continue;              // 公共物品不动

                // 判"这个容器归谁"优先于"这块地板归谁"：
                // 物主把自己的箱子摆进公共房间时，格子归属是 null，
                // 但箱子本身归他，属于正常存放，不能弹。
                SlotGroup group = t.GetSlotGroup();
                Pawn groupOwner = group != null ? EconomyPrivateStorageBehavior.OwnerOfGroup(group) : null;
                if (groupOwner == itemOwner) continue;        // 自己的箱子，正常

                Pawn storeOwner;
                if (groupOwner != null)
                {
                    storeOwner = groupOwner;                  // 别人的箱子，弹
                }
                else
                {
                    // 箱子没有主人 = 公共存储。公共存储里的私有物按设计要弹出来，
                    // 但只在"箱子确实有主人身份"之外的情况下才算公共，
                    // 所以这里不再退回看地板（看地板会把物主自己的箱子判成公共）。
                    storeOwner = null;
                }


                budget--;
                IntVec3 cell = t.PositionHeld;
                DevLog.Msg("[DSH-ECONOMY] 上锁：" + t.LabelShort + "（"
                    + itemOwner.LabelShort + "）不该在 "
                    + (storeOwner != null ? storeOwner.LabelShort : "公共") + "存储里，已移到旁边");

                t.DeSpawn();
                GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near);
            }
        }
    }
}

