using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 把"该不该为了养伤去躺床"这件事**独占**下来：
    /// 所有 mod（包括原版）里叫 WorkGiver_PatientGoToBed* 的派发器一律让它交白卷，
    /// 只剩本 mod 的 JobGiver_EconomyPatientRest 说了算。
    /// 这样别的 mod 加的"有伤就躺"选项不会再把小人拽上床。
    /// </summary>
    public static class BedRestOverride
    {
        private static readonly List<string> patched = new List<string>();

        public static void Apply(Harmony harmony)
        {
            int count = 0;
            foreach (Type t in AccessTools.AllTypes())
            {
                if (t == null) continue;
                if (!t.Name.StartsWith("WorkGiver_PatientGoToBed", StringComparison.Ordinal)) continue;

                MethodInfo m = AccessTools.Method(t, "NonScanJob");
                if (m == null || m.ReturnType != typeof(Job)) continue;

                try
                {
                    harmony.Patch(m, prefix: new HarmonyMethod(
                        AccessTools.Method(typeof(BedRestOverride), "ReturnNoJob")));
                    patched.Add(t.Name);
                    count++;
                }
                catch (Exception e)
                {
                    DevLog.Msg("[MED] 压制 " + t.Name + " 失败：" + e.Message);
                }
            }

            if (count > 0)
            {
                Log.Message("[DSH-ECONOMY] 卧床判定已独占：压掉了 " + count + " 个外部躺床派发器（"
                    + string.Join("、", patched.ToArray()) + "）");
            }
            else
            {
                DevLog.Msg("[MED] 没找到需要压制的躺床派发器");
            }
        }

        /// <summary>让这些派发器直接返回"没活"。</summary>
        public static bool ReturnNoJob(ref Job __result)
        {
            __result = null;
            return false;
        }
    }
}
