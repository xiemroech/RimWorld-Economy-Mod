using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>暴动：攻击富人、破坏建筑；打不过（血量低）就逃离殖民地，逃跑时可被拘捕。</summary>
    public class MentalState_Riot : MentalState_Berserk
    {
        private int counter;

        public override void PostStart(string reason)
        {
            base.PostStart(reason);
            if (pawn != null && pawn.jobs != null) pawn.jobs.StopAll();   // 立刻打断手上的活
            if (pawn != null && pawn.playerSettings != null)
            {
                EconomyUnrest.DriverBackup[pawn] = pawn.playerSettings.hostilityResponse;
                pawn.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
            }
            EconomyUnrest.OnRiotStart(pawn, false);
        }

        public override void PostEnd()
        {
            base.PostEnd();
            EconomyUnrest.RestoreDriver(pawn);
        }

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            counter += delta;
            if (counter < 120) return;
            counter = 0;
            EconomyUnrest.RiotTick(pawn, false);
        }
    }

    /// <summary>革命：绝不逃跑，战至最后；只有被打晕才可能被拘捕。</summary>
    public class MentalState_Revolution : MentalState_Berserk
    {
        private int counter;

        public override void PostStart(string reason)
        {
            base.PostStart(reason);
            if (pawn != null && pawn.jobs != null) pawn.jobs.StopAll();
            if (pawn != null && pawn.playerSettings != null)
            {
                EconomyUnrest.DriverBackup[pawn] = pawn.playerSettings.hostilityResponse;
                pawn.playerSettings.hostilityResponse = HostilityResponseMode.Attack;
            }
            EconomyUnrest.OnRiotStart(pawn, true);
        }

        public override void PostEnd()
        {
            base.PostEnd();
            EconomyUnrest.RestoreDriver(pawn);
        }

        public override void MentalStateTick(int delta)
        {
            base.MentalStateTick(delta);
            counter += delta;
            if (counter < 120) return;
            counter = 0;
            EconomyUnrest.RiotTick(pawn, true);
        }
    }
}
