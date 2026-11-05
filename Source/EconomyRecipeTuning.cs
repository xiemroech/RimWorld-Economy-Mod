using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 把配方工时接到模组设置上。设置里填的是"显示时长"（秒），内部乘 60 转成 tick。
    /// 启动时套用一次；改完设置关窗口时再套用一次。
    /// </summary>
    public static class EconomyRecipeTuning
    {
        public static void Apply()
        {
            DshEconomySettings s = DshEconomyMod.Settings;
            if (s == null) return;

            Set("DshEco_MineRawSilver", s.mineWork);
            Set("DshEco_MineRawSilver_x4", s.mine4Work);
            Set("DshEco_RefineSilver", s.refineWork);
        }

        private static void Set(string defName, float seconds)
        {
            RecipeDef recipe = DefDatabase<RecipeDef>.GetNamedSilentFail(defName);
            if (recipe == null) return;
            recipe.workAmount = UnityEngine.Mathf.Max(1f, seconds) * 60f;
        }
    }
}
