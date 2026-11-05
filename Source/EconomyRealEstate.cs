using System.Collections.Generic;
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

        private void Sanitize()
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
        }

        /// <summary>这个格子在不在某个私有仓库里；在的话返回它的主人。</summary>
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

        public static bool IsForSale(Building b)
        {
            EconomyRealEstate inst = Inst;
            if (inst == null || b == null) return false;
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
        public static void Postfix(Thing t, Pawn carrier, ref IntVec3 foundCell, ref bool __result)
        {
            if (!__result || t == null || carrier == null) return;
            Map map = carrier.Map;
            if (map == null || map.zoneManager == null) return;

            Zone zone = map.zoneManager.ZoneAt(foundCell);
            Pawn zoneOwner = zone != null ? EconomyRealEstate.OwnerOf(zone) : null;

            // 存储建筑（货架/冰箱）的产权
            if (zoneOwner == null && map.haulDestinationManager != null)
            {
                SlotGroup group = map.haulDestinationManager.SlotGroupAt(foundCell);
                Building b = group != null ? group.parent as Building : null;
                if (b != null) zoneOwner = EconomyRealEstate.OwnerOfBuilding(b);
            }

            if (zoneOwner == null) return;
            Pawn itemOwner = EconomyPrivate.OwnerOf(t);

            // 私有物品不能进公共仓库，也不能进别人的仓库：
            // 否则小人会把私房钱和私人物品全搬进公共存储区，看起来像"钱没了"。
            if (itemOwner != null && zoneOwner != itemOwner)
            {
                __result = false;
                return;
            }

            // 私人仓库只收物主自己的东西，且只让物主本人搬运
            if (zoneOwner != null && (zoneOwner != itemOwner || zoneOwner != carrier))
            {
                __result = false;
            }
        }
    }
}
