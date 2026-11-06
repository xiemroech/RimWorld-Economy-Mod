using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// Combat Extended 适配：弹药算「武器」类，只有拥有对应武器才会买，
    /// 随身组数按 CE 的装备预设（Loadout）来，最多 10 组，超出的进仓库。
    /// 全部用反射，没装 CE 时安全返回 false / 默认值。
    /// </summary>
    public static class CEAmmo
    {
        public const int MaxStacks = 10;      // 总共最多 10 组
        public const int DefaultCarry = 4;    // 没预设时随身 4 组

        private static bool ceChecked;
        private static bool ceLoaded;
        private static Type tUtil;
        private static Type tLoadout;

        private static bool EnsureCE()
        {
            if (ceChecked) return ceLoaded;
            ceChecked = true;
            try
            {
                Assembly asm = null;
                foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    if (a.GetName().Name == "CombatExtended") { asm = a; break; }
                }
                if (asm == null) return false;
                tUtil = asm.GetType("CombatExtended.Utility_Loadouts");
                tLoadout = asm.GetType("CombatExtended.Loadout");
                ceLoaded = tUtil != null;
            }
            catch { ceLoaded = false; }
            return ceLoaded;
        }

        /// <summary>CE 的弹药定义：ThingDef 不是武器，但带 Ammo 相关痕迹。</summary>
        public static bool IsAmmoDef(ThingDef def)
        {
            if (def == null || def.IsWeapon) return false;

            if (def.thingClass != null)
            {
                string tn = def.thingClass.Name;
                if (tn.IndexOf("Ammo", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            if (def.defName != null && def.defName.StartsWith("Ammo_", StringComparison.OrdinalIgnoreCase)) return true;

            if (def.thingCategories != null)
            {
                for (int i = 0; i < def.thingCategories.Count; i++)
                {
                    ThingCategoryDef tc = def.thingCategories[i];
                    if (tc == null || tc.defName == null) continue;
                    if (tc.defName.IndexOf("Ammo", StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }

            // CE 弹药带 CompProperties_AmmoUser 的是枪；弹药本身带的是弹道相关 comp
            if (def.comps != null)
            {
                for (int i = 0; i < def.comps.Count; i++)
                {
                    string cn = def.comps[i].GetType().Name;
                    if (cn == "CompProperties_Ammo" || cn == "CompProperties_AmmoDef") return true;
                }
            }
            return false;
        }

        /// <summary>这个人有没有用这种弹药的武器。</summary>
        public static bool OwnsWeaponFor(Pawn p, ThingDef ammo)
        {
            if (p == null || ammo == null || p.equipment == null) return false;
            List<ThingWithComps> eq = p.equipment.AllEquipmentListForReading;
            if (eq == null) return false;
            for (int i = 0; i < eq.Count; i++)
            {
                ThingWithComps w = eq[i];
                if (w == null) continue;
                if (WeaponUsesAmmo(w, ammo)) return true;
            }
            return false;
        }

        private static bool WeaponUsesAmmo(ThingWithComps weapon, ThingDef ammo)
        {
            try
            {
                List<ThingComp> comps = weapon.AllComps;
                if (comps == null) return false;
                for (int i = 0; i < comps.Count; i++)
                {
                    ThingComp comp = comps[i];
                    if (comp == null || comp.GetType().Name != "CompAmmoUser") continue;
                    object props = GetMember(comp, "Props");
                    if (props == null) return false;
                    object set = GetMember(props, "ammoSet");
                    if (set == null) return false;
                    object list = GetMember(set, "ammoTypes");
                    IEnumerable en = list as IEnumerable;
                    if (en == null) return false;
                    foreach (object link in en)
                    {
                        object a = GetMember(link, "ammo");
                        if (a == ammo) return true;
                    }
                }
            }
            catch { }
            return false;
        }

        /// <summary>随身带几组：优先玩家装备预设给这种弹药的件数，否则用 Loadout.adHocMags，缺省 4。</summary>
        public static int CarryStacks(Pawn p, ThingDef ammo)
        {
            int fromSlot = LoadoutCount(p, ammo);
            if (fromSlot > 0) return Mathf.Clamp(fromSlot, 1, MaxStacks);
            int mags = AdHocMags(p);
            return Mathf.Clamp(mags, 1, DefaultCarry);
        }

        private static int AdHocMags(Pawn p)
        {
            if (!EnsureCE() || tUtil == null) return DefaultCarry;
            try
            {
                MethodInfo m = tUtil.GetMethod("GetLoadout", BindingFlags.Public | BindingFlags.Static);
                if (m == null) return DefaultCarry;
                object loadout = m.Invoke(null, new object[] { p });
                if (loadout == null) return DefaultCarry;
                object v = GetMember(loadout, "adHocMags");
                if (v is int) return (int)v;
            }
            catch { }
            return DefaultCarry;
        }

        private static int LoadoutCount(Pawn p, ThingDef ammo)
        {
            if (!EnsureCE() || tUtil == null) return 0;
            try
            {
                MethodInfo m = tUtil.GetMethod("GetLoadout", BindingFlags.Public | BindingFlags.Static);
                if (m == null) return 0;
                object loadout = m.Invoke(null, new object[] { p });
                if (loadout == null) return 0;
                object slots = GetMember(loadout, "Slots");
                IEnumerable en = slots as IEnumerable;
                if (en == null) return 0;
                foreach (object slot in en)
                {
                    object def = GetMember(slot, "thingDef");
                    if (def != ammo) continue;
                    object c = GetMember(slot, "count");
                    if (c is int && (int)c > 0) return (int)c;
                }
            }
            catch { }
            return 0;
        }

        private static object GetMember(object obj, string name)
        {
            if (obj == null) return null;
            Type t = obj.GetType();
            FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (f != null) return f.GetValue(obj);
            PropertyInfo pr = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (pr != null) return pr.GetValue(obj, null);
            return null;
        }

        /// <summary>已经拥有几组（背包 + 自己仓库里），按堆算。</summary>
        public static int OwnedStacks(Pawn p, ThingDef ammo, Map map)
        {
            if (p == null || ammo == null) return 0;
            int n = 0;
            if (p.inventory != null && p.inventory.innerContainer != null)
            {
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (t != null && t.def == ammo) n++;
                }
            }
            if (map != null && map.haulDestinationManager != null)
            {
                foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                {
                    if (g == null || g.HeldThings == null) continue;
                    if (EconomyPrivateStorageBehavior.OwnerOfGroup(g) != p) continue;
                    foreach (Thing t in g.HeldThings)
                    {
                        if (t != null && t.def == ammo) n++;
                    }
                }
            }
            return n;
        }
    }
}
