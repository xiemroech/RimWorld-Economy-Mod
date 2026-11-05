using System;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// Combat Extended 兼容层。只用反射，不引用 CE 程序集：
    /// 1) 读取 CompAmmoUser，空弹时主动生成换弹 Job；
    /// 2) CE 的 JobDriver_Reload 会在“不是原版 Berserk”的精神状态下直接失败，
    ///    这里放行 DshEco_ 状态，否则暴动/革命者永远卡在空弹。
    /// </summary>
    public static class CECompat
    {
        private const string AmmoUserTypeName = "CombatExtended.CompAmmoUser";
        private const string ReloadDriverTypeName = "CombatExtended.JobDriver_Reload";

        public static object GetAmmoUser(Pawn pawn)
        {
            try
            {
                ThingWithComps weapon = pawn != null && pawn.equipment != null ? pawn.equipment.Primary : null;
                if (weapon == null || weapon.AllComps == null) return null;
                for (int i = 0; i < weapon.AllComps.Count; i++)
                {
                    ThingComp comp = weapon.AllComps[i];
                    if (comp != null && comp.GetType().FullName == AmmoUserTypeName) return comp;
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[DSH-ECONOMY] 读取 CE 弹药组件失败：" + e.Message, 0x5E001);
            }
            return null;
        }

        public static bool CanFire(object ammoUser)
        {
            return GetBool(ammoUser, "CanBeFiredNow", true);
        }

        public static bool HasAmmo(object ammoUser)
        {
            return GetBool(ammoUser, "HasAmmo", false);
        }

        /// <summary>从武器的 CE 弹药组件里读出它实际使用的第一种弹药（不是弹壳）。</summary>
        public static ThingDef GetAmmoTypeForWeapon(Pawn pawn)
        {
            object ammoUser = GetAmmoUser(pawn);
            if (ammoUser == null) return null;

            try
            {
                object props = GetMember(ammoUser, "Props");
                object ammoSet = GetMember(props, "ammoSet");
                object ammoTypes = GetMember(ammoSet, "ammoTypes");
                if (ammoTypes is System.Collections.IEnumerable list)
                {
                    foreach (object link in list)
                    {
                        object ammo = GetMember(link, "ammo");
                        ThingDef def = ammo as ThingDef;
                        if (def == null) continue;
                        // 排除弹壳类
                        string n = def.defName ?? "";
                        if (n.IndexOf("Casing", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (n.IndexOf("Empty", System.StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        return def;
                    }
                }
            }
            catch (Exception e)
            {
                Log.WarningOnce("[DSH-ECONOMY] 读取 CE 弹药类型失败：" + e.Message, 0x5E003);
            }
            return null;
        }

        private static object GetMember(object obj, string name)
        {
            if (obj == null || name == null) return null;
            try
            {
                Type t = obj.GetType();
                PropertyInfo p = t.GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return p.GetValue(obj, null);
                FieldInfo f = t.GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f.GetValue(obj);
            }
            catch
            {
            }
            return null;
        }

        public static Job TryMakeReloadJob(object ammoUser)        {
            if (ammoUser == null) return null;
            try
            {
                MethodInfo m = ammoUser.GetType().GetMethod("TryMakeReloadJob", BindingFlags.Public | BindingFlags.Instance);
                return m != null ? m.Invoke(ammoUser, null) as Job : null;
            }
            catch (Exception e)
            {
                Log.WarningOnce("[DSH-ECONOMY] CE 生成换弹 Job 失败：" + e.Message, 0x5E002);
                return null;
            }
        }

        private static bool GetBool(object obj, string property, bool fallback)
        {
            if (obj == null) return fallback;
            try
            {
                PropertyInfo p = obj.GetType().GetProperty(property, BindingFlags.Public | BindingFlags.Instance);
                if (p == null) return fallback;
                object v = p.GetValue(obj, null);
                return v is bool b ? b : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        public static bool Patch(Harmony harmony)
        {
            try
            {
                Type reloadDriver = AccessTools.TypeByName(ReloadDriverTypeName);
                if (reloadDriver == null) return false;   // 没装 CE

                MethodInfo fail = AccessTools.Method(typeof(ToilFailConditions), "FailOnMentalState");
                if (fail == null || !fail.IsGenericMethodDefinition) return false;

                MethodInfo closed = fail.MakeGenericMethod(reloadDriver);
                harmony.Patch(closed,
                    prefix: new HarmonyMethod(AccessTools.Method(typeof(CECompat), nameof(SkipMentalStateFail))));
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] CE 换弹兼容补丁失败：" + e.Message);
                return false;
            }
        }

        /// <summary>只对 DshEco_ 状态跳过 CE 的“换弹目标处于精神状态就失败”。</summary>
        public static bool SkipMentalStateFail(IJobEndable f)
        {
            try
            {
                Pawn actor = f != null ? f.GetActor() : null;
                if (actor == null || actor.MentalStateDef == null) return true;
                return !actor.MentalStateDef.defName.StartsWith("DshEco_");
            }
            catch
            {
                return true;
            }
        }
    }
}
