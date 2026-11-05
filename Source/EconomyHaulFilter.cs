using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>某个人的搬运偏好：勾选后只有列表里的物品会被自动搬运；列表为空 = 不限制。</summary>
    public class HaulFilter : IExposable
    {
        public Pawn pawn;
        public bool enabled;
        public List<ThingDef> allowed = new List<ThingDef>();

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref enabled, "enabled", false);
            Scribe_Collections.Look(ref allowed, "allowed", LookMode.Def);
            if (allowed == null) allowed = new List<ThingDef>();
        }
    }

    /// <summary>搬运白名单（按人）。</summary>
    public static class EconomyHaulFilter
    {
        public static HaulFilter For(Map map, Pawn p, bool create)
        {
            if (map == null || p == null) return null;
            MapEconomyComponent c = map.GetComponent<MapEconomyComponent>();
            if (c == null) return null;
            if (c.haulFilters == null) c.haulFilters = new List<HaulFilter>();
            for (int i = 0; i < c.haulFilters.Count; i++)
            {
                HaulFilter f = c.haulFilters[i];
                if (f != null && f.pawn == p) return f;
            }
            if (!create) return null;
            HaulFilter nf = new HaulFilter { pawn = p };
            c.haulFilters.Add(nf);
            return nf;
        }

        public static bool IsEnabled(Pawn p)
        {
            HaulFilter f = For(p != null ? (p.MapHeld ?? p.Map) : null, p, false);
            return f != null && f.enabled;
        }

        public static void SetEnabled(Pawn p, bool on)
        {
            HaulFilter f = For(p != null ? (p.MapHeld ?? p.Map) : null, p, true);
            if (f == null) return;
            f.enabled = on;
            if (f.allowed == null) f.allowed = new List<ThingDef>();
        }

        public static bool Allows(Pawn p, Thing t)
        {
            if (p == null || t == null) return true;
            HaulFilter f = For(p.MapHeld ?? p.Map, p, false);
            if (f == null || !f.enabled || f.allowed == null || f.allowed.Count == 0) return true;
            return f.allowed.Contains(t.def);
        }

        public static void Toggle(Pawn p, ThingDef def, bool on)
        {
            HaulFilter f = For(p != null ? (p.MapHeld ?? p.Map) : null, p, true);
            if (f == null || def == null) return;
            if (f.allowed == null) f.allowed = new List<ThingDef>();
            if (on) { if (!f.allowed.Contains(def)) f.allowed.Add(def); }
            else f.allowed.Remove(def);
        }

        public static void Clear(Pawn p)
        {
            HaulFilter f = For(p != null ? (p.MapHeld ?? p.Map) : null, p, false);
            if (f != null && f.allowed != null) f.allowed.Clear();
        }

        /// <summary>补丁：该人不在白名单里的东西，不允许自动搬运（仍可手动指派）。</summary>
        public static bool Prefix(Pawn p, Thing t, bool forced, ref bool __result)
        {
            try
            {
                if (t == null || p == null) return true;
                if (forced) return true;
                if (!Allows(p, t))
                {
                    __result = false;
                    return false;
                }
            }
            catch { }
            return true;
        }
    }
}
