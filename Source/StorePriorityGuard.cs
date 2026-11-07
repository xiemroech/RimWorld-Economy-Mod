using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 原版 CurrentStoragePriorityOf(t)：物品躺在优先级更高的容器里时，这个优先级会成为
    /// 搬运的下限（优先级更低的公共区直接被 break 掉），于是公共货被私有箱卡死、
    /// 永远搬不到公共格。这里只在"当前存放组不是它该待的地方"时降为 Unstored。
    /// </summary>
    public static class StorePriorityGuard
    {
        public static void Postfix(Thing t, bool forced, ref StoragePriority __result)
        {
            try
            {
                if (__result == StoragePriority.Unstored || t == null) return;
                SlotGroup g = t.GetSlotGroup();
                if (g == null) { __result = StoragePriority.Unstored; return; }
                Pawn gOwner = EconomyPrivateStorageBehavior.OwnerOfGroup(g);
                Pawn itemOwner = EconomyPrivate.OwnerOf(t);
                bool wrongPlace = itemOwner == null ? gOwner != null : gOwner != itemOwner;
                if (wrongPlace) __result = StoragePriority.Unstored;
            }
            catch { }
        }
    }
}
