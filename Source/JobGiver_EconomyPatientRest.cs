using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>病人优先躺床：能付得起治疗费时，只要身上有伤/流血，就放下工作和娱乐去床上等治疗。</summary>
    public class JobGiver_EconomyPatientRest : ThinkNode_JobGiver
    {
        private static readonly Dictionary<Pawn, int> nextTry = new Dictionary<Pawn, int>();

        /// <summary>读档时清掉冷却表，避免旧 Pawn 残留。</summary>
        public static void ResetRuntime() { nextTry.Clear(); }

        protected override Job TryGiveJob(Pawn pawn)
        {
            try
            {
                if (pawn == null || !pawn.IsColonist || pawn.Dead || pawn.Downed) return null;
                if (pawn.Map == null || pawn.InMentalState) return null;
                if (pawn.jobs != null && pawn.jobs.curJob != null
                    && pawn.jobs.curJob.def == JobDefOf.LayDown) return null;

                bool free = IsFreeCare(pawn);
                if (!NeedsRest(pawn, free)) return null;
                if (!CanAffordCare(pawn, free)) return null;

                int now = Find.TickManager.TicksGame;
                int due;
                if (nextTry.TryGetValue(pawn, out due) && now < due) return null;
                nextTry[pawn] = now + 300;   // 10 秒冷却：被打断就尽快重新躺回去

                Building_Bed bed = FindBed(pawn);
                if (bed == null) return null;

                Job job = JobMaker.MakeJob(JobDefOf.LayDown, bed);
                job.forceSleep = false;
                job.expiryInterval = 2500;
                return job;
            }
            catch { return null; }
        }

        private static bool NeedsRest(Pawn p, bool free)
        {
            List<Hediff> hs = p.health.hediffSet.hediffs;
            for (int i = 0; i < hs.Count; i++)
            {
                Hediff h = hs[i];
                if (h is Hediff_Injury && !h.IsPermanent() && h.Severity >= (free ? 0.1f : 8f)) return true;
                if (h.def.defName == "BloodLoss" && h.Severity >= (free ? 0.05f : 0.35f)) return true;
                if (h.def.defName == "WoundInfection" || h.def.defName.StartsWith("Infection")) return true;
            }
            return false;
        }

        /// <summary>社会化高（免费）就都躺；否则只有满足“付得起 + 伤得重”才躺。</summary>
        private static bool IsFreeCare(Pawn p)
        {
            MapEconomyComponent comp = p.Map.GetComponent<MapEconomyComponent>();
            if (comp == null) return true;
            if (comp.medical != null && !comp.medical.enabled) return true;
            return MedicalFree(p);
        }

        private static bool CanAffordCare(Pawn p, bool free)
        {
            MapEconomyComponent comp = p.Map.GetComponent<MapEconomyComponent>();
            if (free) return true;

            int price = comp != null && comp.medical != null
                ? UnityEngine.Mathf.RoundToInt(comp.medical.baseFee)
                : 0;
            return EconomyBank.PrivateSilver(p.Map, p) >= price;
        }

        private static bool MedicalFree(Pawn p)
        {
            EconomyCategoryDef med = DefDatabase<EconomyCategoryDef>.GetNamedSilentFail("DshEco_Medicine");
            if (med == null) return false;
            MapEconomyComponent c = p.Map.GetComponent<MapEconomyComponent>();
            if (c == null) return false;
            return c.GetPublicization(med) >= 100f;
        }

        private static Building_Bed FindBed(Pawn p)
        {
            Building_Bed best = null;
            int bestScore = int.MinValue;
            foreach (Building_Bed b in p.Map.listerBuildings.AllBuildingsColonistOfClass<Building_Bed>())
            {
                if (b.Faction != Faction.OfPlayer) continue;
                if (b.OwnersForReading != null && b.OwnersForReading.Count > 0 && !b.OwnersForReading.Contains(p) && !b.Medical) continue;
                if (!p.CanReserveAndReach(b, PathEndMode.OnCell, Danger.Some)) continue;
                int score = (b.Medical ? 100 : 0) + (b.OwnersForReading != null && b.OwnersForReading.Contains(p) ? 50 : 0);
                if (score > bestScore) { bestScore = score; best = b; }
            }
            return best;
        }
    }
}
