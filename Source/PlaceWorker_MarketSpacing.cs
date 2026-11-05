using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>市场摊位之间至少隔 30 格，避免一个殖民地摆一排市场。</summary>
    public class PlaceWorker_MarketSpacing : PlaceWorker
    {
        public const float MinDistance = 30f;

        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map,
            Thing thingToIgnore = null, Thing thing = null)
        {
            if (map == null) return true;
            List<Building> markets = map.listerBuildings.allBuildingsColonist;
            for (int i = 0; i < markets.Count; i++)
            {
                Building_Market m = markets[i] as Building_Market;
                if (m == null || m.Destroyed) continue;
                if (thing != null && m == thing) continue;
                if ((m.Position - loc).LengthHorizontal < MinDistance)
                {
                    return new AcceptanceReport("30 格内已经有市场摊位（" + m.LabelShort + "），请拉开距离再建。");
                }
            }
            return true;
        }
    }
}
