using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 个人的工资参数覆盖。-1 表示"沿用全局值"。
    /// 加班开关是三态：-1 用全局，0 关，1 开。
    /// </summary>
    public class WageOverride : IExposable
    {
        public Pawn pawn;

        public float laborDayValue = -1f;
        public float wageShare = -1f;
        public float capPerDay = -1f;
        public float standardWorkday = -1f;
        public float overtimeMultiplier = -1f;
        public int overtimeEnabled = -1;

        public bool AnyCustom
        {
            get
            {
                return laborDayValue >= 0f || wageShare >= 0f || capPerDay >= 0f
                    || standardWorkday >= 0f || overtimeMultiplier >= 0f || overtimeEnabled >= 0;
            }
        }

        public void Reset()
        {
            laborDayValue = -1f;
            wageShare = -1f;
            capPerDay = -1f;
            standardWorkday = -1f;
            overtimeMultiplier = -1f;
            overtimeEnabled = -1;
        }

        public void ExposeData()
        {
            Scribe_References.Look(ref pawn, "pawn");
            Scribe_Values.Look(ref laborDayValue, "laborDayValue", -1f);
            Scribe_Values.Look(ref wageShare, "wageShare", -1f);
            Scribe_Values.Look(ref capPerDay, "capPerDay", -1f);
            Scribe_Values.Look(ref standardWorkday, "standardWorkday", -1f);
            Scribe_Values.Look(ref overtimeMultiplier, "overtimeMultiplier", -1f);
            Scribe_Values.Look(ref overtimeEnabled, "overtimeEnabled", -1);
        }
    }
}
