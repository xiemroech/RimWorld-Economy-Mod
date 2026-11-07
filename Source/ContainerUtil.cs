using RimWorld;
using Verse;
using UnityEngine;

namespace DshEconomy
{
    /// <summary>
    /// mod 容器的容量不在格子上，而在内部的 ThingOwner 里：
    /// 12 组的箱子只有 1 个 SlotGroup 格，用 CellsList 会严重低估。
    /// 一切"还能不能收 / 还能收几件"都走这里。
    /// </summary>
    public static class ContainerUtil
    {
        public static ThingOwner Inner(IHaulDestination d)
        {
            Thing t = d as Thing;
            if (t == null) return null;
            try { return t.TryGetInnerInteractableThingOwner(); } catch { return null; }
        }

        public static ThingOwner InnerOf(Building_Storage st)
        {
            if (st == null) return null;
            try { return st.TryGetInnerInteractableThingOwner(); } catch { return null; }
        }

        /// <summary>这个容器还能不能再收下 item（按实物判断）。</summary>
        public static bool CanAccept(ThingOwner inner, Thing item)
        {
            if (inner == null || item == null) return false;
            try { return inner.GetCountCanAccept(item) > 0; } catch { return false; }
        }

        /// <summary>
        /// 这个内部容器"还能再放多少槽位"：拿白银（堆叠上限最高、几乎人人可存）当探针，
        /// 问它还能再收几件白银，再除以白银每组数量 —— 得到槽位而不是件数。
        /// 这样"每槽 1 件的武器"和"每槽几百件的材料"能共用同一套百分比。
        /// </summary>
        public static int FreeSlots(ThingOwner inner)
        {
            if (inner == null) return 0;
            try
            {
                ThingDef probe = ThingDefOf.Silver;
                Thing sample = ThingMaker.MakeThing(probe, null);
                if (sample == null) return 0;
                sample.stackCount = Mathf.Max(1, probe.stackLimit);
                int items = inner.GetCountCanAccept(sample);
                if (items <= 0) return 0;
                return Mathf.Max(1, Mathf.CeilToInt(items / (float)Mathf.Max(1, probe.stackLimit)));
            }
            catch { return 0; }
        }

        /// <summary>这个容器总共有多少槽位（已用 + 空闲）。</summary>
        public static int CapacitySlots(Building_Storage st)
        {
            ThingOwner inner = InnerOf(st);
            if (inner != null)
            {
                int used = 0;
                try { used = inner.Count; } catch { }
                return Mathf.Max(1, used + FreeSlots(inner));
            }
            int cells = 0;
            try { cells = st.AllSlotCellsList().Count; } catch { }
            if (cells <= 0) return 0;
            int perCell = 1;
            try { if (st.def != null && st.def.building != null) perCell = Mathf.Max(1, st.def.building.maxItemsInCell); } catch { }
            return Mathf.Max(1, cells * perCell);
        }

        /// <summary>这个容器还能再收下多少件该实物。</summary>
        public static int CountCanAccept(ThingOwner inner, Thing item)
        {
            if (inner == null || item == null) return 0;
            try { return inner.GetCountCanAccept(item); } catch { return 0; }
        }

        /// <summary>这个容器还能再收多少件 def（用于容量估算）。</summary>
        public static int CanAcceptCount(ThingOwner inner, ThingDef def)
        {
            if (inner == null || def == null) return 0;
            try
            {
                // 注意：madeFromStuff 的定义必须给 stuff，否则每次探测都会刷一条
                // "MakeThing error: XXX is madeFromStuff but stuff=null"，几分钟就上千条。
                ThingDef stuff = null;
                try
                {
                    if (def.MadeFromStuff) stuff = GenStuff.DefaultStuffFor(def);
                }
                catch { }
                Thing sample = ThingMaker.MakeThing(def, stuff);
                if (sample == null) return 0;
                // 关键：用"满叠"去问，而不是 1 件。
                // 很多 mod 的 GetCountCanAccept 是按"还能放几叠/几件该堆"算的，
                // 拿 1 件去问会得到"还能再放 1 件"，于是把一组几百个判成只能放一个。
                sample.stackCount = Mathf.Max(1, def.stackLimit);
                return inner.GetCountCanAccept(sample);
            }
            catch { return 0; }
        }

        /// <summary>容器当前装了多少件（不是格数）。</summary>
        public static int CountIn(ThingOwner inner)
        {
            if (inner == null) return 0;
            try { return inner.Count; } catch { return 0; }
        }
    }
}
