using System;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 诊断日志开关。正式游玩时 Enabled 为 false，所有调试输出静默；
    /// 只有在设置里勾了「开发者模式」才打印，避免刷爆 Player.log。
    /// 报错和警告一律直接走 Log.Error / Log.Warning，不经过这里。
    /// </summary>
    public static class DevLog
    {
        public static bool Enabled = false;

        /// <summary>每次启动刷新一次开关状态。</summary>
        public static void Refresh()
        {
            try { Enabled = DshEconomyMod.Settings != null && DshEconomyMod.Settings.verboseLog; }
            catch { Enabled = false; }
        }

        public static void Msg(string text)
        {
            if (!Enabled) return;
            try { Log.Message(text); } catch { }
        }
    }
}
