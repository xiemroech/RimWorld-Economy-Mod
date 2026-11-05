using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>罢工：真正进入精神状态，由 ThinkTree 负责吃饭、睡觉、闲逛，直到状态结束。</summary>
    public class MentalState_Strike : MentalState
    {
        public override void PostStart(string reason)
        {
            base.PostStart(reason);
            if (pawn != null && pawn.jobs != null) pawn.jobs.StopAll();
        }
    }
}
