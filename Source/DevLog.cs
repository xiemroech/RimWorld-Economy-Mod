using System;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 诊断日志开关。正式游玩时静默；勾了设置里的「打印诊断日志」才输出。
    /// 报错和警告一律直接走 Log.Error / Log.Warning，不经过这里。
    /// </summary>
    public static class DevLog
    {
        /// <summary>兼容旧调用：现在每次都直接读设置，不再依赖缓存。</summary>
        public static bool Enabled
        {
            get { return Live(); }
            set { }
        }

        private static bool Live()
        {
            try { return DshEconomyMod.Settings != null && DshEconomyMod.Settings.verboseLog; }
            catch { return false; }
        }

        /// <summary>保留给老的调用点，实际已无缓存需要刷新。</summary>
        public static void Refresh()
        {
        }

        public static void Msg(string text)
        {
            if (!Live()) return;
            try { Log.Message(text); } catch { }
        }
    }
}
