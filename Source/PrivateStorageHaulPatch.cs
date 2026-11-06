using System.Collections.Generic;
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
        private static int rejPrivateCell, rejPrivateItem, allowCount, lastLogTick = -99999;
        private static readonly System.Collections.Generic.Dictionary<string, int> rejSamples = new System.Collections.Generic.Dictionary<string, int>();

        public static void TickLog(Map map)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastLogTick < 2500) return;
            lastLogTick = now;
            if (rejPrivateCell + rejPrivateItem + allowCount == 0) return;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[HAULBLOCK] 近一小时：允许 ").Append(allowCount)
              .Append("，私有格拒收 ").Append(rejPrivateCell)
              .Append("，私有物拒收 ").Append(rejPrivateItem);
            foreach (var kv in rejSamples) sb.Append(" | ").Append(kv.Key).Append(" x").Append(kv.Value);
            Log.Message(sb.ToString());
            rejPrivateCell = rejPrivateItem = allowCount = 0;
            rejSamples.Clear();
        }

        public static bool CellPrefix(IntVec3 c, Map map, Thing t, Pawn carrier, ref bool __result)
        {
            if (t == null || map == null) return true;
            allowCount++;

            Pawn storeOwner = OwnerAt(map, c);

            // 无主（公共）货物：只能进公共格
            if (EconomyPrivate.OwnerOf(t) == null)
            {
                if (storeOwner == null) return true;
                rejPrivateCell++;
                string k0 = "私有格(" + (storeOwner != null ? storeOwner.LabelShort : "?") + ")不收公共货(" + t.LabelShort + ")";
                rejSamples[k0] = rejSamples.TryGetValue(k0, out int v0) ? v0 + 1 : 1;
                __result = false;
                return false;
            }

            // 私有物进公共格：允许。原版只会试"最优的那一格"，一旦这里否掉，
            // 任务就直接失败、不会退回去试别的格子——皮革、食材卡住都是这么来的。
            if (storeOwner == null) return true;

            // 私有格子：只收物主自己的私有物品
            if (EconomyPrivate.OwnerOf(t) == storeOwner) return true;
            rejPrivateCell++;
            string k2 = "私有格(" + (storeOwner != null ? storeOwner.LabelShort : "?") + ")不收(" + t.LabelShort + ")";
            rejSamples[k2] = rejSamples.TryGetValue(k2, out int v2) ? v2 + 1 : 1;
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
            // 与最终校验（StoreUtilityPrivatePatch.Postfix）用完全相同的解析顺序，
            // 这样"搜索阶段排除掉的格子"和"最终会被否决的格子"永远一致，
            // 不会出现"选中了私有格 → 被否决 → 整条任务失败、不回头试别的格"。
            if (map == null) return null;
            if (map.zoneManager != null)
            {
                Zone zone = map.zoneManager.ZoneAt(cell);
                if (zone != null)
                {
                    Pawn z = EconomyRealEstate.OwnerOf(zone);
                    if (z != null) return z;
                }
            }
            if (map.haulDestinationManager != null)
            {
                SlotGroup group = map.haulDestinationManager.SlotGroupAt(cell);
                Building b = group != null ? group.parent as Building : null;
                if (b != null)
                {
                    Pawn o = EconomyRealEstate.OwnerOfBuilding(b);
                    if (o != null) return o;
                }
            }
            return EconomyRealEstate.OwnerAtCell(map, cell);
        }
    }
}
