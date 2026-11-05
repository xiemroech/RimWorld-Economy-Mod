using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 兜底：万一有东西被塞进了不该进的私有仓库（公共物品进私有仓、或进别人的仓），
    /// 立刻把它弹出来丢回地面。前面的 StoreUtility / HaulAIUtility 拦截是"事前"，
    /// 这里是"事中"，防止别的 mod 或原版路径绕过。
    /// </summary>
    public static class PrivateStorageEjectPatch
    {
        public static void Postfix(SlotGroup __instance, Thing t)
        {
            if (__instance == null || t == null) return;
            if (!t.Spawned) return;

            Pawn owner = OwnerOf(__instance);
            if (owner == null) return;

            Pawn itemOwner = EconomyPrivate.OwnerOf(t);
            if (itemOwner == owner) return;

            Map map = t.MapHeld;
            IntVec3 cell = t.PositionHeld;
            if (map == null) return;

            string who = itemOwner != null ? itemOwner.LabelShort : "公共物品";
            Log.Message("[DSH-ECONOMY] 拦截：" + who + " 被塞进 " + owner.LabelShort
                + " 的私有仓库（" + t.LabelShort + "），已弹出");

            StorageEjectQueue.Enqueue(t);
        }

        public static Pawn OwnerOf(SlotGroup group)
        {
            if (group == null) return null;
            Building b = group.parent as Building;
            if (b != null) return EconomyRealEstate.OwnerOfBuilding(b);
            Zone z = group.parent as Zone;
            if (z != null) return EconomyRealEstate.OwnerOf(z);
            return null;
        }
    }
}
