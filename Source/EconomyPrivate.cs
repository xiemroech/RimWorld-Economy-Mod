using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 私有物品的读写。归属标签存在物品自己的 CompItemTag 上，
    /// 不再依赖外部登记表，所以标签会跟着物品移动、跟着存档保存。
    /// </summary>
    public static class EconomyPrivate
    {
        public const string ConfiscateThought = "DshEco_PropertyConfiscated";

        public static CompItemTag TagOf(Thing t)
        {
            if (t == null) return null;
            try { return t.TryGetComp<CompItemTag>(); }
            catch { return null; }   // comps 列表被外部破坏时不要连累整帧
        }

        public static bool IsPrivate(Thing t)
        {
            CompItemTag c = TagOf(t);
            return c != null && c.owner != null;
        }

        public static Pawn OwnerOf(Thing t)
        {
            CompItemTag c = TagOf(t);
            return c != null ? c.owner : null;
        }

        /// <summary>给物品贴私有标签；owner 传 null 即撕掉标签变回公共。</summary>
        public static void SetPrivate(Thing t, Pawn owner)
        {
            CompItemTag c = TagOf(t);
            if (c == null)
            {
                // 旧存档里的物品没有这个组件，先补挂再打标签，否则点了没反应
                int dummy = 0;
                CompInjector.EnsureTag(t, ref dummy);
                c = TagOf(t);
            }
            if (c != null) c.owner = owner;   // 只改字段，绝不从 comps 列表里删元素
            if (owner == null && t.def == ThingDefOf.Silver
                && t.ParentHolder is Pawn_InventoryTracker it && it.pawn != null)
            {
                // 白银从私有变回公共、又正好在某个人背包里：交给公共白银逻辑送回仓库
                EconomyPublicSilver.MarkDirty(it.pawn);
            }
            if (owner != null && t.def != null && t.def.category == ThingCategory.Item
                && !EconomyRegistry.IsCurrency(t.def))
            {
                EconomyPrivateStorageBehavior.MarkDirty(owner);
            }
        }

        /// <summary>扫一遍地图与殖民者背包，按 owner 归并所有带私有标签的物品。</summary>
        public static Dictionary<Pawn, List<Thing>> BuildIndex(Map map)
        {
            Dictionary<Pawn, List<Thing>> dict = new Dictionary<Pawn, List<Thing>>();
            if (map == null) return dict;

            if (map.listerThings != null)
            {
                List<Thing> all = map.listerThings.AllThings;
                for (int i = 0; i < all.Count; i++) Collect(dict, all[i]);
            }

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];

                if (p.inventory != null && p.inventory.innerContainer != null)
                {
                    IList<Thing> inner = p.inventory.innerContainer;
                    for (int j = 0; j < inner.Count; j++) Collect(dict, inner[j]);
                }
                if (p.apparel != null && p.apparel.WornApparel != null)
                {
                    List<Apparel> worn = p.apparel.WornApparel;
                    for (int j = 0; j < worn.Count; j++) Collect(dict, worn[j]);
                }
                if (p.equipment != null && p.equipment.Primary != null)
                {
                    Collect(dict, p.equipment.Primary);
                }
            }
            return dict;
        }

        private static void Collect(Dictionary<Pawn, List<Thing>> dict, Thing t)
        {
            if (!IsPrivate(t)) return;
            CompItemTag c = TagOf(t);

            List<Thing> list;
            if (!dict.TryGetValue(c.owner, out list))
            {
                list = new List<Thing>();
                dict[c.owner] = list;
            }
            list.Add(t);
        }

        public static List<Thing> OwnedItems(Pawn pawn, Map map)
        {
            List<Thing> list;
            if (pawn == null) return new List<Thing>();
            return BuildIndex(map).TryGetValue(pawn, out list) ? list : new List<Thing>();
        }

        /// <summary>某人身上、但仍属公共的东西：背包 + 穿戴 + 手持。</summary>
        /// <summary>
        /// 修复被早期版本（直接 comps.RemoveAt）破坏的 comps 列表。
        /// 做法：对每个 ThingWithComps 试读一次组件，抛异常就重建 comps。
        /// </summary>
        public static int PruneEmptyTags(Map map)
        {
            if (map == null || map.listerThings == null) return 0;
            int fixedCount = 0;
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                ThingWithComps twc = all[i] as ThingWithComps;
                if (twc == null || twc.Destroyed) continue;
                try { twc.TryGetComp<CompItemTag>(); }
                catch
                {
                    // 重建组件表会把 owner 一起抹掉 —— 先记住主人，重建完再打回去。
                    // 这是"私有标签偶尔集体消失"的根源之一。
                    Pawn oldOwner = null;
                    try { oldOwner = OwnerOf(twc); } catch { }

                    try
                    {
                        twc.comps.Clear();
                        twc.InitializeComps();
                        if (oldOwner != null) SetPrivate(twc, oldOwner);
                        fixedCount++;
                    }
                    catch { }
                }
            }
            return fixedCount;
        }

        public static void PruneAllMaps()
        {
            int n = 0;
            if (Find.Maps != null)
            {
                for (int i = 0; i < Find.Maps.Count; i++) n += PruneEmptyTags(Find.Maps[i]);
            }
            if (n > 0) Log.Message("[DSH-ECONOMY] 组件表修复：重建 " + n + " 个物品的 comps");
        }

        public static List<Thing> PublicHeld(Pawn pawn)
        {
            List<Thing> result = new List<Thing>();
            if (pawn == null) return result;

            if (pawn.inventory != null && pawn.inventory.innerContainer != null)
            {
                IList<Thing> inner = pawn.inventory.innerContainer;
                for (int i = 0; i < inner.Count; i++)
                {
                    if (!IsPrivate(inner[i])) result.Add(inner[i]);
                }
            }
            if (pawn.apparel != null && pawn.apparel.WornApparel != null)
            {
                List<Apparel> worn = pawn.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    if (!IsPrivate(worn[i])) result.Add(worn[i]);
                }
            }
            if (pawn.equipment != null && pawn.equipment.Primary != null && !IsPrivate(pawn.equipment.Primary))
            {
                result.Add(pawn.equipment.Primary);
            }
            return result;
        }

        /// <summary>某人背包里、但没有私有标签的物品（兼容旧调用）。</summary>
        public static List<Thing> CarriedPublic(Pawn pawn)
        {
            return PublicHeld(pawn);
        }

        public static float Value(IEnumerable<Thing> things, Map map)
        {
            float total = 0f;
            if (things == null) return 0f;
            foreach (Thing t in things) total += EconomyPrice.UnitPrice(t, map);
            return total;
        }

        /// <summary>
        /// 一键社会化并丢弃：从背包取出丢到地上、撕掉标签、物主不满。
        /// 供界面按钮和物品 gizmo 共用。
        /// </summary>
        public static void SocializeAndDrop(Thing t, Map map)
        {
            if (t == null) return;
            Pawn owner = OwnerOf(t);
            Map m = map;
            if (m == null && owner != null) m = owner.MapHeld;
            if (m == null) m = t.MapHeld;

            if (owner != null && owner.inventory != null && owner.inventory.innerContainer != null
                && owner.inventory.innerContainer.Contains(t) && m != null)
            {
                owner.inventory.innerContainer.Remove(t);
                GenPlace.TryPlaceThing(t, owner.PositionHeld, m, ThingPlaceMode.Near, out Thing placed);
            }

            SetPrivate(t, null);

            if (owner != null && owner.needs != null && owner.needs.mood != null)
            {
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(ConfiscateThought);
                if (thought != null) owner.needs.mood.thoughts.memories.TryGainMemory(thought);
            }
        }

        /// <summary>征用某人名下的私有物品：背包里的丢到地上，全部撕掉私有标签，返回件数。</summary>
        public static int Confiscate(Pawn pawn, Map map)
        {
            if (pawn == null || map == null) return 0;

            List<Thing> owned = OwnedItems(pawn, map);
            if (owned.Count == 0) return 0;

            int count = 0;
            for (int i = 0; i < owned.Count; i++)
            {
                Thing t = owned[i];
                if (t == null || t.Destroyed) continue;

                if (pawn.inventory != null && pawn.inventory.innerContainer != null
                    && pawn.inventory.innerContainer.Contains(t))
                {
                    pawn.inventory.innerContainer.Remove(t);
                    GenPlace.TryPlaceThing(t, pawn.PositionHeld, map, ThingPlaceMode.Near, out Thing placed);
                }
                SetPrivate(t, null);
                count++;
            }

            if (count > 0)
            {
                ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(ConfiscateThought);
                if (thought != null && pawn.needs != null && pawn.needs.mood != null)
                {
                    pawn.needs.mood.thoughts.memories.TryGainMemory(thought);
                }
            }
            return count;
        }
    }
}
