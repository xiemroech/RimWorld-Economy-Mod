using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 找储存格的兜底：原版只挑“最优的一格”，那一格若被产权规则跳过就整条任务失败，
    /// 表现就是“明明有空箱子却提示未配置空余的、可到达的储存点”。
    /// 这里在失败后把**所有**存放组扫一遍，按归属规则换一个能收它的格子。
    /// </summary>
    public static class StoreFallback
    {
        /// <summary>最近一次 TryFindAnyStorage 失败的原因摘要（只读，供日志用）。</summary>
        public static string Why = "";

        /// <summary>兜底时只按容器的原始过滤器判断，忽略可能过期的产权缓存。</summary>
        private static bool FilterAccepts(SlotGroup g, Thing t)
        {
            if (g == null || g.Settings == null) return true;
            // 兜底只认容器自己的 ThingFilter；产权由上面的 OwnerOfGroup 显式判断，
            // 不再经过 AllowedToAccept 上的其他 Harmony Postfix。
            return g.Settings.filter == null || g.Settings.filter.Allows(t);
        }

        public static bool TryFindPublicCell(Thing t, Pawn carrier, Map map, out IntVec3 cell)
        {
            string why;
            return TryFindPublicCell(t, carrier, map, out cell, out why);
        }

        public static bool TryFindPublicCell(Thing t, Pawn carrier, Map map, out IntVec3 cell, out string why)
        {
            cell = IntVec3.Invalid;
            why = "";
            if (t == null || carrier == null || map == null || map.haulDestinationManager == null)
            {
                why = "参数为空";
                return false;
            }

            int groups = 0, filterNo = 0, unreach = 0, notGood = 0;
            foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (g == null || g.Settings == null) continue;
                if (EconomyPrivateStorageBehavior.OwnerOfGroup(g) != null) continue;
                groups++;
                if (!FilterAccepts(g, t)) { filterNo++; continue; }
                if (g.CellsList == null) continue;

                for (int i = 0; i < g.CellsList.Count; i++)
                {
                    IntVec3 c = g.CellsList[i];
                    if (!c.InBounds(map)) continue;
                    if (!carrier.CanReach(c, PathEndMode.ClosestTouch, Danger.Some)) { unreach++; continue; }
                    if (!StoreUtility.IsGoodStoreCell(c, map, t, carrier, carrier.Faction)) { notGood++; continue; }
                    if (!c.IsValidStorageFor(map, t)) { notGood++; continue; }
                    cell = c;
                    return true;
                }
            }

            why = "公共组 " + groups + " 个：过滤器拒 " + filterNo + "，走不到 " + unreach + "，格子不合格 " + notGood;
            return false;
        }

        /// <summary>仅手动强制搬运的最后兜底：无视容器过滤器，只要求是公共组且有可用格。</summary>
        public static bool TryFindPublicCellIgnoringFilter(Thing t, Pawn carrier, Map map, out IntVec3 cell)
        {
            cell = IntVec3.Invalid;
            if (t == null || carrier == null || map == null || map.haulDestinationManager == null) return false;
            foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (g == null || g.Settings == null || g.CellsList == null) continue;
                if (EconomyPrivateStorageBehavior.OwnerOfGroup(g) != null) continue;
                for (int i = 0; i < g.CellsList.Count; i++)
                {
                    IntVec3 c = g.CellsList[i];
                    if (!c.InBounds(map)) continue;
                    if (!carrier.CanReach(c, PathEndMode.ClosestTouch, Danger.Some)) continue;
                    if (!StoreUtility.IsGoodStoreCell(c, map, t, carrier, carrier.Faction)) continue;
                    // JobDriver_HaulToCell 会在出发前检查 IsValidStorageFor；只忽略“选择过滤器”
                    // 而目标格本身不接受物品时，任务会在原地直接失败，形成“假搬运”。
                    if (!c.IsValidStorageFor(map, t)) continue;
                    if (!carrier.CanReserve(c)) continue;
                    cell = c;
                    return true;
                }
            }
            return false;
        }

        /// <summary>公共“非 SlotGroup 容器”兜底：箱子/冰箱等 IHaulDestination 的 Thing 容器。</summary>
        public static bool TryFindPublicContainer(Thing t, Pawn carrier, Map map, out Thing container)
        {
            container = null;
            if (t == null || carrier == null || map == null || map.haulDestinationManager == null) return false;
            bool old = StorageSettingsOwnerIndex.Bypass;
            StorageSettingsOwnerIndex.Bypass = true;
            try
            {
                foreach (IHaulDestination d in map.haulDestinationManager.AllHaulDestinationsListInPriorityOrder)
                {
                    // 不能因为它是 ISlotGroupParent 就跳过：很多 mod 箱子既是 SlotGroup，
                    // 又有自己的 inner ThingOwner（12 组容量）。真正的判据是有没有 inner owner。
                    if (d == null || !d.HaulDestinationEnabled) continue;
                    Thing thing = d as Thing;
                    if (thing == null || thing.Faction != carrier.Faction || thing.IsForbidden(carrier)) continue;
                    StorageSettings ss = d.GetStoreSettings();
                    if (ss == null || ss.Priority <= StoragePriority.Unstored) continue;
                    if (StorageSettingsOwnerIndex.OwnerOf(ss) != null) continue;
                    if (!d.Accepts(t)) continue;
                    ThingOwner inner = thing.TryGetInnerInteractableThingOwner();
                    if (inner == null || inner.GetCountCanAccept(t) <= 0) continue;
                    if (!carrier.CanReach(thing, PathEndMode.ClosestTouch, Danger.Some)) continue;
                    if (!carrier.CanReserveNew(thing)) continue;
                    container = thing;
                    return true;
                }
            }
            finally { StorageSettingsOwnerIndex.Bypass = old; }
            return false;
        }

        public static bool TryFindAnyCell(Thing t, Pawn carrier, Map map, out IntVec3 cell)
        {
            IHaulDestination dest;
            return TryFindAnyStorage(t, carrier, map, out cell, out dest);
        }

        /// <summary>
        /// 无视“最优格”限制，把所有存放组扫一遍；按归属规则：
        /// 公共货 → 公共组；私人物 → 物主自己的组。
        /// </summary>
        public static bool TryFindAnyStorage(Thing t, Pawn carrier, Map map, out IntVec3 cell, out IHaulDestination dest)
        {
            cell = IntVec3.Invalid;
            dest = null;
            Why = "";
            if (t == null || carrier == null || map == null || map.haulDestinationManager == null)
            {
                Why = "参数为空";
                return false;
            }

            bool old = PrivateStorageHaulPatch.Bypass;
            PrivateStorageHaulPatch.Bypass = false;
            try
            {
                Pawn itemOwner = EconomyPrivate.OwnerOf(t);
                int nGroups = 0, nOwned = 0, nFilter = 0, nUnreach = 0, nNotGood = 0;

                foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                {
                    if (g == null || g.Settings == null) continue;
                    nGroups++;

                    Pawn gOwner = EconomyPrivateStorageBehavior.OwnerOfGroup(g);
                    if (itemOwner == null) { if (gOwner != null) { nOwned++; continue; } }
                    else if (gOwner != itemOwner) { nOwned++; continue; }

                    if (!FilterAccepts(g, t)) { nFilter++; continue; }
                    if (g.CellsList == null) continue;

                    for (int i = 0; i < g.CellsList.Count; i++)
                    {
                        IntVec3 c = g.CellsList[i];
                        if (!c.InBounds(map)) continue;
                        if (!carrier.CanReach(c, PathEndMode.ClosestTouch, Danger.Some)) { nUnreach++; continue; }
                        if (!StoreUtility.IsGoodStoreCell(c, map, t, carrier, carrier.Faction)) { nNotGood++; continue; }

                        // 用与 CellPrefix 完全相同的归属判定再确认一次（组归属可能识别不出 mod 容器）
                        Pawn cellOwner = EconomyRealEstate.OwnerAtCell(map, c);
                        if (itemOwner == null) { if (cellOwner != null) { nOwned++; continue; } }
                        else if (cellOwner != itemOwner) { nOwned++; continue; }

                        IHaulDestination d = g.parent as IHaulDestination;
                        if (d == null) continue;
                        cell = c;
                        dest = d;
                        return true;
                    }
                }

                Why = "组 " + nGroups + " 个：有主跳过 " + nOwned + "，过滤器拒 " + nFilter
                    + "，走不到 " + nUnreach + "，格子不合格 " + nNotGood;
            }
            finally { PrivateStorageHaulPatch.Bypass = old; }
            return false;
        }

        /// <summary>只读诊断：兜底也找不到时，把每个存放组的真实情况打出来。</summary>
        public static void DumpFailure(Thing t, Pawn p, Map map)
        {
            try
            {
                if (t == null || p == null || map == null) return;
                Pawn itemOwner = EconomyPrivate.OwnerOf(t);
                DevLog.Msg("[HAULFAIL] " + p.LabelShort + " 搬 " + t.LabelShort + " def=" + t.def.defName
                    + " 物主=" + (itemOwner != null ? itemOwner.LabelShort : "公共"));
                foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                {
                    if (g == null || g.CellsList == null) continue;
                    Pawn gOwner = EconomyPrivateStorageBehavior.OwnerOfGroup(g);
                    string allowed = "?";
                    try { allowed = g.Settings != null && g.Settings.AllowedToAccept(t) ? "是" : "否"; } catch { }
                    int okCell = -1;
                    for (int i = 0; i < g.CellsList.Count; i++)
                    {
                        IntVec3 c = g.CellsList[i];
                        if (!c.InBounds(map)) continue;
                        if (StoreUtility.IsGoodStoreCell(c, map, t, p, p.Faction)) { okCell = i; break; }
                    }
                    string cellWhy = "";
                    if (okCell >= 0)
                    {
                        IntVec3 c = g.CellsList[okCell];
                        Thing first = c.GetFirstItem(map);
                        cellWhy = " 有效=" + c.IsValidStorageFor(map, t)
                            + " 可预定=" + p.CanReserve(c)
                            + " 格内物=" + (first != null ? first.LabelShort : "空");
                    }
                    else if (g.CellsList.Count > 0)
                    {
                        IntVec3 c = g.CellsList[0];
                        Thing first = c.GetFirstItem(map);
                        cellWhy = " 首格有效=" + c.IsValidStorageFor(map, t)
                            + " 可预定=" + p.CanReserve(c)
                            + " 可达=" + p.CanReach(c, PathEndMode.ClosestTouch, Danger.Some)
                            + " 格内物=" + (first != null ? first.LabelShort : "空");
                    }
                    Pawn cachedOwner = g.Settings != null ? StorageSettingsOwnerIndex.OwnerOf(g.Settings) : null;
                    bool rawFilter = g.Settings == null || g.Settings.filter == null || g.Settings.filter.Allows(t);
                    bool bypassAllowed = false;
                    if (g.Settings != null)
                    {
                        bool oldBypass = StorageSettingsOwnerIndex.Bypass;
                        StorageSettingsOwnerIndex.Bypass = true;
                        try { bypassAllowed = g.Settings.AllowedToAccept(t); }
                        finally { StorageSettingsOwnerIndex.Bypass = oldBypass; }
                    }
                    DevLog.Msg("[HAULFAIL]   组 " + (g.parent != null ? g.parent.ToString() : "?")
                        + " 主=" + (gOwner != null ? gOwner.LabelShort : "公共")
                        + " 缓存主=" + (cachedOwner != null ? cachedOwner.LabelShort : "无")
                        + " 优先级=" + (g.Settings != null ? g.Settings.Priority.ToString() : "?")
                        + " 格=" + g.CellsList.Count
                        + " 过滤器允许=" + allowed
                        + " 原生过滤器=" + rawFilter
                        + " 绕过产权=" + bypassAllowed
                        + " 好格=" + (okCell >= 0 ? "有" : "无")
                        + cellWhy);
                }
            }
            catch (System.Exception e) { Log.Warning("[HAULFAIL] 诊断异常：" + e.Message); }
        }
}
}
