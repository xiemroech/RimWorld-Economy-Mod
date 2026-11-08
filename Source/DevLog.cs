using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 只在开发者模式下输出的日志。
    /// 玩家环境（选项 → 开发者模式 未勾选）完全静默，不刷 Player.log。
    /// 排查问题时打开开发者模式即可看回全部探针输出。
    /// </summary>
    internal static class DevLog
    {
        public static bool On
        {
            get { return Prefs.DevMode; }
        }

        public static void Msg(string text)
        {
            if (!Prefs.DevMode) return;
            Log.Message(text);
        }
    }
}
