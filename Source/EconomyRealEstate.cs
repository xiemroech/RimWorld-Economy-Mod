using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 不动产的雏形：存储区（堆放区）可以划归某个小人私有。
    /// 私有存储区只允许物主自己的东西、且由物主本人搬运进去。
    /// </summary>
    public class EconomyRealEstate : GameComponent
    {
        public static EconomyRealEstate Instance;

        private List<Zone> zones = new List<Zone>();
        private List<Pawn> owners = new List<Pawn>();

        // 存储建筑（货架、冰箱、储物箱）的产权：Building → Pawn
        private List<Building> buildings = new List<Building>();
        private List<Pawn> buildingOwners = new List<Pawn>();

        // 禁止被小人购买的容器（玩家上锁）
        private List<Building> notForSale = new List<Building>();

        // 容器出售总开关（默认关闭）
        public bool containerSaleEnabled = false;

        /// <summary>把每个存放组的归属打出来，用于确认"公共箱子到底被谁认领了"。</summary>
        public static void Diagnose(Map map)
        {
            if (map == null || map.haulDestinationManager == null) return;
            if (Find.TickManager != null && Find.TickManager.TicksGame > 30000 && !DebugSettings.godMode) { }
            StringBuilder sb = new StringBuilder();
            int n = 0, pub = 0, priv = 0, pubFree = 0;
            foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
            {
                if (g?.parent == null) continue;
                n++;
                Pawn o = EconomyPrivateStorageBehavior.OwnerOfGroup(g);
                int free = 0;
                foreach (IntVec3 c in g.CellsList)
                {
                    if (map.thingGrid.ThingsListAt(c).Any(x => x.def.category == ThingCategory.Item) == false) free++;
                }
                if (o == null) { pub++; if (free > 0) pubFree++; }
                else priv++;
                if (n <= 12)
                {
                    bool acceptsSilver = false;
                    try
                    {
                        StorageSettings ss = null;
                        Building_Storage bs = g.parent as Building_Storage;
                        if (bs != null) ss = bs.GetStoreSettings();
                        else
                        {
                            Zone_Stockpile zp = g.parent as Zone_Stockpile;
                            if (zp != null) ss = zp.GetStoreSettings();
                        }
                        if (ss != null) acceptsSilver = ss.AllowedToAccept(ThingDefOf.Silver);
                    }
                    catch { }
                    sb.Append("\n#").Append(n).Append(' ').Append((g.parent as Thing)?.LabelShort ?? g.parent.ToString())
                      .Append(" 主=").Append(o == null ? "公共" : o.LabelShort)
                      .Append(" 空=").Append(free).Append('/').Append(g.CellsList.Count)
                      .Append(" 收银=").Append(acceptsSilver ? "是" : "否");
                }
            }
            DevLog.Msg("[DSH-ECONOMY] 存放组诊断：共 " + n + "，公共 " + pub + "（其中有空位 " + pubFree + "），私有 " + priv + sb);
        }

        public EconomyRealEstate(Game game)
        {
            Instance = this;
        }

        private static EconomyRealEstate Inst
        {
            get
            {
                if (Instance != null) return Instance;
                if (Current.Game != null) Instance = Current.Game.GetComponent<EconomyRealEstate>();
                return Instance;
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Instance = this;
            Sanitize();
        }

        /// <summary>定期清掉已拆除/销毁的仓库产权，避免 OwnerAtCell 继续认旧主人。</summary>
        public static void Prune(Map map)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null) return;
            bool changed = false;
            for (int i = inst.buildings.Count - 1; i >= 0; i--)
            {
                Building b = inst.buildings[i];
                if (b == null || b.Destroyed || !b.Spawned || (map != null && b.Map != map))
                {
                    inst.buildings.RemoveAt(i);
                    inst.buildingOwners.RemoveAt(i);
                    changed = true;
                }
            }
            if (inst.notForSale != null)
            {
                int n = inst.notForSale.RemoveAll(b => b == null || b.Destroyed || !b.Spawned);
                if (n > 0) changed = true;
            }
            if (changed) InvalidateCells();
        }

        /// <summary>把所有存储建筑的所有权收回公共（关闭容器出售时用）。</summary>
        public int ClearAllBuildingOwners()
        {
            int n = buildings != null ? buildings.Count : 0;
            if (n > 0)
            {
                buildings.Clear();
                buildingOwners.Clear();
                InvalidateCells();
            }
            return n;
        }

        public void Sanitize()
        {
            if (zones == null) zones = new List<Zone>();
            if (owners == null) owners = new List<Pawn>();
            if (buildings == null) buildings = new List<Building>();
            if (buildingOwners == null) buildingOwners = new List<Pawn>();
            if (notForSale == null) notForSale = new List<Building>();
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                InvalidateCells();
                notForSale.RemoveAll(b => b == null || b.Destroyed);
            }
            while (owners.Count < zones.Count) owners.Add(null);
            if (owners.Count > zones.Count) owners.RemoveRange(zones.Count, owners.Count - zones.Count);

            for (int i = zones.Count - 1; i >= 0; i--)
            {
                if (zones[i] == null || owners[i] == null)
                {
                    zones.RemoveAt(i);
                    owners.RemoveAt(i);
                }
            }

            while (buildingOwners.Count < buildings.Count) buildingOwners.Add(null);
            if (buildingOwners.Count > buildings.Count)
            {
                buildingOwners.RemoveRange(buildings.Count, buildingOwners.Count - buildings.Count);
            }
            for (int i = buildings.Count - 1; i >= 0; i--)
            {
                if (buildings[i] == null || buildingOwners[i] == null)
                {
                    buildings.RemoveAt(i);
                    buildingOwners.RemoveAt(i);
                }
            }
        }

        /// <summary>这个人名下有没有任何不动产（堆放区或存储建筑）。</summary>
        public static bool HasAnyRealEstate(Pawn pawn)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null || pawn == null) return false;
            for (int i = 0; i < inst.owners.Count; i++)
            {
                if (inst.owners[i] == pawn) return true;
            }
            for (int i = 0; i < inst.buildingOwners.Count; i++)
            {
                if (inst.buildingOwners[i] == pawn) return true;
            }
            return false;
        }

        // ── 格子 → 私有仓库主人 的缓存（产权变化时清空）──
        private static readonly Dictionary<Map, Dictionary<IntVec3, Pawn>> cellCache
            = new Dictionary<Map, Dictionary<IntVec3, Pawn>>();

        public static void InvalidateCells()
        {
            cellCache.Clear();
            try { StorageSettingsOwnerIndex.Rebuild(Find.CurrentMap); } catch { }
        }

        /// <summary>这个格子在不在某个私有仓库里；在的话返回它的主人。</summary>
        /// <summary>把地图上每个储物建筑都列出来（不管有没有登记归属），排查"少认一个箱子"。</summary>
        public static void DumpStorages(Map map)
        {
            if (map == null) return;
            try
            {
                System.Text.StringBuilder sb = new System.Text.StringBuilder();
                int n = 0;
                foreach (Building_Storage st in map.listerBuildings.AllBuildingsColonistOfClass<Building_Storage>())
                {
                    if (st == null) continue;
                    n++;
                    Pawn o = OwnerOfBuilding(st);
                    SlotGroup g = st.GetSlotGroup();
                    bool allowsSilver = false;
                    try { allowsSilver = st.GetStoreSettings().AllowedToAccept(ThingDefOf.Silver); } catch { }
                    int freeGroups = 0;
                    try
                    {
                        SlotGroup sg = st.GetSlotGroup();
                        if (sg != null) freeGroups = sg.CellsList.Count(c => !c.GetThingList(map).Any(x => x.def == ThingDefOf.Silver));
                    }
                    catch { }
                    sb.Append("\n  #").Append(n).Append(" ").Append(st.LabelShort)
                      .Append(" (").Append(st.def.defName).Append(")")
                      .Append(" 主=").Append(o == null ? "公共" : o.LabelShort)
                      .Append(" 组=").Append(g == null ? "无" : g.CellsList.Count.ToString())
                      .Append(" 收银=").Append(allowsSilver ? "是" : "否").Append(" 空位=").Append(freeGroups)
                      .Append(" 尺寸=").Append(st.def.size.x).Append("x").Append(st.def.size.z);
                }
                DevLog.Msg("[DSH-ECONOMY] 储物建筑清点：共 " + n + " 个" + sb);
            }
            catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 清点失败：" + e.Message); }
        }

        /// <summary>只读：检查每个储物建筑登记的归属，与它占据格子的"格子主人"是否一致。</summary>
        public static void CellOwnerAudit(Map map)
        {
            if (map == null || map.listerBuildings == null) return;
            foreach (Building_Storage st in map.listerBuildings.AllBuildingsColonistOfClass<Building_Storage>())
            {
                if (st == null) continue;
                Pawn declared = OwnerOfBuilding(st);
                int cells = 0, mismatch = 0, ownedCells = 0;
                Pawn sample = null;
                SlotGroup g = st.GetSlotGroup();
                List<IntVec3> list = new List<IntVec3>();
                if (g != null) list.AddRange(g.CellsList);
                else foreach (IntVec3 cc in st.OccupiedRect().Cells) list.Add(cc);
                for (int i = 0; i < list.Count; i++)
                {
                    cells++;
                    Pawn cellOwner = OwnerAtCell(map, list[i]);
                    if (cellOwner != null) { ownedCells++; if (sample == null) sample = cellOwner; }
                    if (cellOwner != declared) mismatch++;
                }
                DevLog.Msg("[CELLCHK] " + st.LabelShort + " 登记=" + (declared == null ? "公共" : declared.LabelShort)
                    + " 格=" + cells + " 有主格=" + ownedCells + " 不一致=" + mismatch
                    + (sample != null ? " 例=" + sample.LabelShort : ""));
            }
        }

        public static Pawn OwnerAtCell(Map map, IntVec3 cell)
        {
            if (map == null) return null;
            Dictionary<IntVec3, Pawn> dict;
            if (!cellCache.TryGetValue(map, out dict) || dict == null)
            {
                dict = BuildCellMap(map);
                cellCache[map] = dict;
            }
            Pawn owner;
            if (dict.TryGetValue(cell, out owner)) return owner;

            // 兜底：这个格子上有没有带主人的储物建筑（多格建筑/智能仓储）
            if (map.thingGrid != null)
            {
                List<Thing> things = map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Building_Storage st = things[i] as Building_Storage;
                    if (st == null) continue;
                    Pawn o = OwnerOfBuilding(st);
                    if (o != null) return o;
                }
            }
            return null;
        }

        private static Dictionary<IntVec3, Pawn> BuildCellMap(Map map)
        {
            var dict = new Dictionary<IntVec3, Pawn>();
            EconomyRealEstate inst = Inst;
            if (inst == null || map == null) return dict;

            for (int i = 0; i < inst.zones.Count; i++)
            {
                Zone_Stockpile zone = inst.zones[i] as Zone_Stockpile;
                if (zone == null || zone.cells == null) continue;
                Pawn owner = inst.owners[i];
                if (owner == null) continue;
                for (int c = 0; c < zone.cells.Count; c++) dict[zone.cells[c]] = owner;
            }

            for (int i = 0; i < inst.buildings.Count; i++)
            {
                Building_Storage st = inst.buildings[i] as Building_Storage;
                if (st == null || st.Map != map) continue;
                Pawn owner = inst.buildingOwners[i];
                if (owner == null) continue;
                SlotGroup g = st.GetSlotGroup();
                if (g != null && g.CellsList != null)
                {
                    for (int c = 0; c < g.CellsList.Count; c++) dict[g.CellsList[c]] = owner;
                }
                else
                {
                    // 智能仓储等 mod 的建筑可能没有 SlotGroup，用占用格子兜底
                    foreach (IntVec3 c in st.OccupiedRect().Cells)
                    {
                        dict[c] = owner;
                    }
                }
            }
            return dict;
        }


        /// <summary>全地图可购买（无主且在售）的储物建筑数量，用于界面提示。</summary>
        public static int CountForSale(Map map)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null || map == null) return 0;
            int n = 0;
            List<SlotGroup> groups = map.haulDestinationManager.AllGroupsListForReading;
            for (int i = 0; i < groups.Count; i++)
            {
                Building b = groups[i].parent as Building;
                if (b == null || b is Building_Storage == false) continue;
                if (OwnerOfBuilding(b) != null) continue;
                if (!IsForSale(b)) continue;
                n++;
            }
            return n;
        }

        /// <summary>手动把某个箱子设为“可购买”时，允许这次操作绕过全局默认关闭。</summary>
        public static void ForceEnableSale()
        {
            EconomyRealEstate inst = Inst;
            if (inst != null) inst.containerSaleEnabled = true;
        }

        public static bool IsForSale(Building b)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null || b == null) return false;
            if (!inst.containerSaleEnabled) return false;   // 默认不出售容器
            return !inst.notForSale.Contains(b);
        }

        public static void SetForSale(Building b, bool sale)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null || b == null) return;
            if (sale) inst.notForSale.Remove(b);
            else if (!inst.notForSale.Contains(b)) inst.notForSale.Add(b);
        }

        public static Pawn OwnerOfBuilding(Building b)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null || b == null) return null;
            for (int i = 0; i < inst.buildings.Count; i++)
            {
                if (inst.buildings[i] == b) return inst.buildingOwners[i];
            }
            return null;
        }

        public static void SetBuildingOwner(Building b, Pawn owner)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null || b == null) return;

            int index = -1;
            for (int i = 0; i < inst.buildings.Count; i++)
            {
                if (inst.buildings[i] == b) { index = i; break; }
            }

            Pawn previous = index >= 0 ? inst.buildingOwners[index] : null;
            if (previous != null && previous != owner)
            {
                // 容器被社会化/转手：原主人掉心情 + 工作满意度
                try
                {
                    MemoryThoughtHandler mem = previous.needs?.mood?.thoughts?.memories;
                    if (mem != null) mem.TryGainMemory(ThoughtDef.Named("DshEco_PropertyConfiscated"), null);
                    EconomySatisfactionEvents.Add(previous, "容器所有权被社会化（" + b.LabelShort + "）", -3f, 5);
                    DevLog.Msg("[DSH-ECONOMY] 容器社会化：" + previous.LabelShort + " 失去 " + b.LabelShort);
                }
                catch (System.Exception e) { Log.Warning("[DSH-ECONOMY] 容器社会化心情失败：" + e.Message); }
            }

            if (owner == null)
            {
                if (index >= 0)
                {
                    inst.buildings.RemoveAt(index);
                    inst.buildingOwners.RemoveAt(index);
                    InvalidateCells();
                }
                return;
            }
            if (index >= 0) inst.buildingOwners[index] = owner;
            else
            {
                inst.buildings.Add(b);
                inst.buildingOwners.Add(owner);
            }
            InvalidateCells();
        }

        public static Pawn OwnerOf(Zone zone)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null || zone == null) return null;
            for (int i = 0; i < inst.zones.Count; i++)
            {
                if (inst.zones[i] == zone) return inst.owners[i];
            }
            return null;
        }

        public static void SetOwner(Zone zone, Pawn owner)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null || zone == null) return;

            int index = -1;
            for (int i = 0; i < inst.zones.Count; i++)
            {
                if (inst.zones[i] == zone) { index = i; break; }
            }

            if (owner == null)
            {
                if (index >= 0)
                {
                    inst.zones.RemoveAt(index);
                    inst.owners.RemoveAt(index);
                    InvalidateCells();
                }
                return;
            }

            if (index >= 0) inst.owners[index] = owner;
            else
            {
                inst.zones.Add(zone);
                inst.owners.Add(owner);
            }
            InvalidateCells();
        }

        /// <summary>在当前主人之后轮换：公共 → 小明 → 小红 → … → 公共。</summary>
        public static Pawn CycleOwner(Zone zone, List<Pawn> colonists)
        {
            Pawn current = OwnerOf(zone);
            if (colonists == null || colonists.Count == 0) return current;

            int next = 0;
            if (current != null)
            {
                int idx = colonists.IndexOf(current);
                next = idx < 0 ? 0 : idx + 1;
            }

            Pawn owner = next >= colonists.Count ? null : colonists[next];
            SetOwner(zone, owner);
            return owner;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref zones, "dsh_zones", LookMode.Reference);
            Scribe_Collections.Look(ref owners, "dsh_zoneOwners", LookMode.Reference);
            Scribe_Collections.Look(ref notForSale, "dsh_notForSale", LookMode.Reference);
            Scribe_Values.Look(ref containerSaleEnabled, "dsh_containerSale", false);
            Scribe_Collections.Look(ref buildings, "dsh_storeBuildings", LookMode.Reference);
            Scribe_Collections.Look(ref buildingOwners, "dsh_storeBuildingOwners", LookMode.Reference);
            Sanitize();
            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                // 旧 Map 的格子产权缓存必须清，否则读档后会保留"幽灵私有区"
                InvalidateCells();
                if (notForSale != null) notForSale.RemoveAll(b => b == null || b.Destroyed);
            }
        }
    }

    /// <summary>搬运时拦下"私人仓库"：别人的东西不进、别人也不往里搬。</summary>
    public static class StoreUtilityPrivatePatch
    {
        public static void Postfix(Thing t, Pawn carrier, Map map, ref IntVec3 foundCell, ref bool __result)
        {
            if (t == null || carrier == null) return;
            Map cMap = carrier.Map ?? map;
            if (cMap == null || cMap.zoneManager == null) return;

            if (!__result)
            {
                // 原版（含产权过滤）找不到格子 → 无视产权再扫一遍，避免"明明有空箱子"
                IntVec3 any;
                if (StoreFallback.TryFindAnyCell(t, carrier, cMap, out any))
                {
                    foundCell = any;
                    __result = true;
                }
                return;
            }

            // 与 StoreUtilityPrivatePatch.OwnerAt 保持同一顺序：建筑优先，堆放区其次
            Pawn zoneOwner = null;
            if (cMap.haulDestinationManager != null)
            {
                SlotGroup group = cMap.haulDestinationManager.SlotGroupAt(foundCell);
                Building b = group != null ? group.parent as Building : null;
                if (b != null)
                {
                    zoneOwner = EconomyRealEstate.OwnerOfBuilding(b);
                    if (zoneOwner == null) return;   // 公共货架：就算盖在私有区上也算公共
                }
            }
            if (zoneOwner == null)
            {
                Zone zone = cMap.zoneManager.ZoneAt(foundCell);
                if (zone != null) zoneOwner = EconomyRealEstate.OwnerOf(zone);
            }

            if (zoneOwner == null) return;
            Pawn itemOwner = EconomyPrivate.OwnerOf(t);

            // 私有物品不能进公共仓库，也不能进别人的仓库：
            // 否则小人会把私房钱和私人物品全搬进公共存储区，看起来像"钱没了"。
            bool rejected = false;
            if (itemOwner != null && zoneOwner != itemOwner) rejected = true;
            // 私人仓库只收物主自己的东西，且只让物主本人搬运
            else if (zoneOwner != null && (zoneOwner != itemOwner || zoneOwner != carrier)) rejected = true;

            if (!rejected) return;

            __result = false;

            // 被否掉的这一格只是"扫描时最先遇到的那一格"（很可能是私有容器）。
            // 真正的做法：继续往后找 —— 换一个能收它的格子（公共格，或物主自己的格）。
            IntVec3 alt;
            if (StoreFallback.TryFindAnyCell(t, carrier, cMap, out alt))
            {
                foundCell = alt;
                __result = true;
                return;
            }

            if (itemOwner == null)
            {
                IntVec3 pub;
                if (StoreFallback.TryFindPublicCell(t, carrier, cMap, out pub))
                {
                    foundCell = pub;
                    __result = true;
                }
            }

            if (!__result) StoreFallback.DumpFailure(t, carrier, cMap);
        }
    }

    /// <summary>
    /// 手动「优先搬运」和部分搬运路径用的是 TryFindBestBetterStorageFor（不是 ...StoreCellFor），
    /// 它同样只返回"最优的一个"，被产权跳过后就报"未配置空余的、可到达的储存点"。
    /// </summary>
    public static class StoreUtilityStorageForPatch
    {
        private static readonly Dictionary<string, int> DiagTick = new Dictionary<string, int>();

        public static void Postfix(Thing t, Pawn carrier, Map map,
            ref IntVec3 foundCell, ref IHaulDestination haulDestination, ref bool __result)
        {
            if (__result) return;
            if (t == null || carrier == null) return;
            Map cMap = carrier.Map ?? map;
            if (cMap == null) return;

            IntVec3 cell;
            IHaulDestination dest;
            bool found = StoreFallback.TryFindAnyStorage(t, carrier, cMap, out cell, out dest);
            if (found)
            {
                foundCell = cell;
                haulDestination = dest;
                __result = true;
            }

            // 只读诊断：每次失败/成功各打一行，便于确认兜底到底有没有跑
            try
            {
                string key = t.def.defName;
                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                if (!DiagTick.TryGetValue(key, out int last) || now - last > 600)
                {
                    DiagTick[key] = now;
                    DevLog.Msg("[HAULWHY] 换格兜底 " + (found ? "成功 → " + cell : "失败 " + StoreFallback.Why)
                        + "（" + t.LabelShort + " ×" + t.stackCount + " 物主="
                        + (EconomyPrivate.OwnerOf(t) == null ? "公共" : EconomyPrivate.OwnerOf(t).LabelShort)
                        + " 搬运者=" + carrier.LabelShort + "）");
                }
            }
            catch { }
        }
}
}

