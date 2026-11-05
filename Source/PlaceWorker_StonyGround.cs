using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>银矿场只能建在石头地面上（整块都要在可磨光岩石上）。</summary>
    public class PlaceWorker_StonyGround : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(BuildableDef checkingDef, IntVec3 loc, Rot4 rot, Map map, Thing thingToIgnore = null, Thing thing = null)
        {
            if (map == null) return true;
            foreach (IntVec3 c in GenAdj.OccupiedRect(loc, rot, checkingDef.Size))
            {
                TerrainDef terrain = map.terrainGrid.TerrainAt(c);
                if (terrain == null || !terrain.affordances.Contains(TerrainAffordanceDefOf.SmoothableStone))
                {
                    return "只能建在石头地面上";
                }
            }
            return true;
        }
    }
}
