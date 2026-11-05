using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>私有物品在名字后面直接标出物主，仓库/地上都能一眼看到是谁的。</summary>
    public static class PrivateLabelPatch
    {
        public static void Postfix(Thing __instance, ref string __result)
        {
            if (__result == null) return;
            ThingWithComps twc = __instance as ThingWithComps;
            if (twc == null) return;
            CompItemTag tag = twc.TryGetComp<CompItemTag>();
            if (tag == null || tag.owner == null) return;
            __result = __result + "（" + tag.owner.LabelShort + "）";
        }
    }

    /// <summary>储物建筑直接收货时也做一次归属检查（有些路径不走 SlotGroup.Notify_Added）。</summary>
    public static class StorageReceivedEjectPatch
    {
        public static void Postfix(Building_Storage __instance, Thing newItem)
        {
            if (__instance == null || newItem == null || !newItem.Spawned) return;

            Pawn owner = EconomyRealEstate.OwnerOfBuilding(__instance);
            if (owner == null) return;

            Pawn itemOwner = EconomyPrivate.OwnerOf(newItem);
            if (itemOwner == owner) return;

            Map map = newItem.MapHeld;
            IntVec3 cell = newItem.PositionHeld;
            if (map == null) return;

            // 不能在这里直接 DeSpawn：调用方（例如 Adaptive Storage）返回后还会继续用这个 Thing
            StorageEjectQueue.Enqueue(newItem);
        }
    }
}
