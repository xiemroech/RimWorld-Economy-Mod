using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 私有仓库的"取出"限制：别人不能从私有堆放区/私有存储建筑里搬走东西。
    /// 之前只拦了"存进去"，没有拦"拿出来"。
    /// </summary>
    public static class PrivateStorageHaulPatch
    {
        /// <summary>
        /// 在"这个格子能不能放"这一层就过滤掉公共仓库，
        /// 原版扫描根本不会把公共存储当成候选，也就不会出现"选中→被拒→再选"的循环。
        /// </summary>
        public static bool CellPrefix(IntVec3 c, Map map, Thing t, Pawn carrier, ref bool __result)
        {
            if (t == null || map == null) return true;

            Pawn storeOwner = OwnerAt(map, c);
            if (storeOwner == null)
            {
                // 公共格子：只拒私有物品
                Pawn io = EconomyPrivate.OwnerOf(t);
                if (io == null) return true;
                __result = false;
                return false;
            }

            // 私有格子：只收物主自己的私有物品（公共物品也算"别人的"）
            if (EconomyPrivate.OwnerOf(t) == storeOwner) return true;
            __result = false;
            return false;
        }

        public static bool Prefix(Pawn p, Thing t, ref bool __result)
        {
            if (p == null || t == null || !t.Spawned) return true;
            Map map = p.Map;
            if (map == null) return true;

            Pawn owner = OwnerAt(map, t.Position);
            if (owner != null && owner != p)
            {
                __result = false;
                return false;
            }
            return true;
        }

        public static Pawn OwnerAt(Map map, IntVec3 cell)
        {
            // 用"格子 → 主人"表：建筑和堆放区重叠时也能取到私有主人
            return EconomyRealEstate.OwnerAtCell(map, cell);
        }
    }
}
