using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>学费政策：每个儿童每次发薪向父母收取固定学费，从父母个人白银扣给殖民地。</summary>
    public class EducationPolicy : IExposable
    {
        public bool enabled;
        public float feePerChild = 20f;

        public void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", false);
            Scribe_Values.Look(ref feePerChild, "feePerChild", 20f);
        }
    }

    public static class EconomyEducation
    {
        /// <summary>给所有儿童收一次学费，返回实收总额。</summary>
        public static int Charge(Map map, EducationPolicy policy)
        {
            if (map == null || policy == null || !policy.enabled) return 0;

            int fee = Mathf.RoundToInt(Mathf.Max(0f, policy.feePerChild));
            if (fee <= 0) return 0;

            int total = 0;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn child = colonists[i];
                if (child == null || child.Dead) continue;
                if (!IsChild(child)) continue;

                Pawn payer = FindGuardian(child);
                if (payer == null) continue;

                total += TakePrivateSilver(payer, fee);
            }
            return total;
        }

        private static bool IsChild(Pawn p)
        {
            if (p.ageTracker == null) return false;
            return p.ageTracker.AgeBiologicalYears < 13;
        }

        /// <summary>找父母之一；没有父母就找同一张图上收入最高的殖民者代付？——不，直接跳过。</summary>
        private static Pawn FindGuardian(Pawn child)
        {
            if (child.relations == null) return null;
            List<DirectPawnRelation> rels = child.relations.DirectRelations;
            for (int i = 0; i < rels.Count; i++)
            {
                DirectPawnRelation rel = rels[i];
                if (rel.def != PawnRelationDefOf.Parent) continue;
                Pawn parent = rel.otherPawn;
                if (parent != null && !parent.Dead && parent.Spawned) return parent;
            }
            return null;
        }

        /// <summary>从某人的私有银堆里扣钱：撕掉相应数量的私有标签即视为上缴。</summary>
        private static int TakePrivateSilver(Pawn payer, int amount)
        {
            Map map = payer.Map;
            // 统一走 EconomyBank：背包里的银币会被真正搬出来变成公共银
            return EconomyBank.TakePrivateSilver(map, payer, amount);
        }
    }
}
