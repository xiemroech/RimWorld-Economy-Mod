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
            Log.Message("[DSH-ECONOMY] 上锁：" + who + " " + t.LabelShort + " 不该在这里，已移到旁边");
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

            Pawn storeOwner = PrivateStorageHaulPatch.OwnerAt(map, t.Position);
            Pawn itemOwner = EconomyPrivate.OwnerOf(t);

            // 私有仓库里出现了不属于物主的东西（公共物品或别人的私有物）→ 弹出
            // 私有仓只收物主自己的私有物：无主物品、别人的物品都弹出
            if (storeOwner == null) return;
            if (itemOwner != storeOwner) Eject(t, itemOwner, map);
            return;

            // 公共仓库里的私有物 → 弹出
            if (itemOwner == null) return;

            IntVec3 cell;
            if (!CellFinder.TryFindRandomCellNear(t.Position, map, 8,
                    c => c.Standable(map) && map.haulDestinationManager.SlotGroupAt(c) == null,
                    out cell))
            {
                return;
            }

            Log.Message("[DSH-ECONOMY] 上锁：私有物品 " + t.LabelShort + "（" + itemOwner.LabelShort
                + "）被放进公共存储，已移到旁边");
            t.DeSpawn();
            GenPlace.TryPlaceThing(t, cell, map, ThingPlaceMode.Near);
        }
    }
}
