using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 上锁：私有物品一旦被放进**公共**存储区，立刻弹出来丢到附近地面。
    /// 之前只在"选择存放点"时拦截，某些路径（存档载入、别的 mod 搬运）会绕过。
    /// </summary>
    public static class PrivateItemStorageGuard
    {
        private static void Eject(Thing t, Pawn itemOwner, Map map)
        {
            IntVec3 cell;
            if (!CellFinder.TryFindRandomCellNear(t.Position, map, 8,
                    c => c.Standable(map) && map.haulDestinationManager.SlotGroupAt(c) == null,
                    out cell))
            {
                return;
            }
            string who = itemOwner != null ? itemOwner.LabelShort : "异常物品";
            DevLog.Msg("[DSH-ECONOMY] 上锁：" + who + " " + t.LabelShort + " 不该在这里，已移到旁边");
            t.DeSpawn();
            GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near);
        }

        public static void Postfix(Thing __instance)
        {
            Thing t = __instance;
            if (t == null || t.def == null) return;
            if (t.def.category != ThingCategory.Item) return;

            Map map = t.Map;
            if (map == null || map.haulDestinationManager == null) return;
            if (map.haulDestinationManager.SlotGroupAt(t.Position) == null) return;

            // 只看"这口箱子/这个堆放区归谁"，不退回看地板：
            // 看地板会把"物主自己的箱子摆在公共/他人房间"判成公共仓，然后把他的货弹出去。
            // 另外：刚生成时 SlotGroup 可能还没登记，此时不判定（等下一次）。
            SlotGroup group = t.GetSlotGroup();
            if (group == null) return;
            Pawn storeOwner = null;
            try { storeOwner = EconomyPrivateStorageBehavior.OwnerOfGroup(group); }
            catch { }
            Pawn itemOwner = EconomyPrivate.OwnerOf(t);

            // 私有仓只收物主自己的私有物：别人的东西、公共物品都弹出
            if (storeOwner != null)
            {
                if (itemOwner != storeOwner) Eject(t, itemOwner, map);
                return;
            }

            // 公共仓：私有物按设计也弹出（保持原行为）
            if (itemOwner == null) return;
            Eject(t, itemOwner, map);
        }
    }
}

