using System;
using System.Collections.Generic;
using Verse;

namespace DshEconomy
{
    /// <summary>只报一次的告警：用于原本静默的空 catch，让"功能没生效"可见但不刷屏。</summary>
    public static class EcoLog
    {
        private static readonly HashSet<string> seen = new HashSet<string>();

        public static void WarnOnce(string key, Exception e)
        {
            if (key == null) key = "unknown";
            if (!seen.Add(key)) return;
            Log.Warning("[DSH-ECONOMY] " + key + " 失败（只报一次）：" + (e != null ? e.Message : "unknown"));
        }

        public static void ResetRuntime()
        {
            seen.Clear();
        }
    }
}
