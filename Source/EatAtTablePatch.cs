using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 私有食物常常放在离餐厅很远的自家箱子里，原版只在 pawn 附近 chairSearchRadius 格内找座位，
    /// 找不到就退回"站着吃"——注意它给的是一个**有效的空地格**，所以 __result 仍然是 true，
    /// 不能靠返回值判断"没找到椅子"。
    /// 这里改成判断"返回的格子到底是不是座位/桌边"，不是的话再补一次全图找椅子+桌子。
    /// </summary>
    public static class EatAtTablePatch
    {
        /// <summary>这一格的上下左右是否有一格带"桌面"（桌子）。</summary>
        private static bool HasTableNextTo(IntVec3 c, Map map)
        {
            for (int i = 0; i < 4; i++)
            {
                IntVec3 n = c + GenAdj.CardinalDirections[i];
                if (!n.InBounds(map)) continue;
                if (n.HasEatSurface(map)) return true;
            }
            return false;
        }

        /// <summary>这一格上是否摆着可坐的家具（椅子/凳子）。</summary>
        private static bool IsSeatCell(IntVec3 c, Map map)
        {
            if (!c.InBounds(map)) return false;
            var things = map.thingGrid.ThingsListAt(c);
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t.def != null && t.def.building != null && t.def.building.isSittable) return true;
            }
            return false;
        }

        /// <summary>这座椅子四周有没有桌子——没有桌子的椅子坐了也白坐。</summary>
        private static bool ChairNearTable(Thing chair, Map map)
        {
            foreach (IntVec3 c in chair.OccupiedRect().ExpandedBy(1).Cells)
            {
                if (!c.InBounds(map)) continue;
                if (c.HasEatSurface(map)) return true;
            }
            return false;
        }

        public static void Postfix(Pawn pawn, Thing ingestible, ref bool __result, ref IntVec3 cell)
        {
            if (pawn == null || ingestible == null || pawn.Map == null) return;
            if (ingestible.def == null || ingestible.def.ingestible == null) return;
            if (!ingestible.def.ingestible.tableDesired) return;

            // 原版已经给了座位，或者给的空地本身就挨着桌子（站着也能就着桌面吃）→ 不动
            if (cell.IsValid)
            {
                if (IsSeatCell(cell, pawn.Map)) return;
                if (HasTableNextTo(cell, pawn.Map)) return;
            }

            try
            {
                Map map = pawn.Map;
                Thing chair = GenClosest.ClosestThingReachable(
                    pawn.Position, map,
                    ThingRequest.ForGroup(ThingRequestGroup.BuildingArtificial),
                    PathEndMode.OnCell, TraverseParms.For(pawn),
                    9999f,
                    t => t.def != null && t.def.building != null && t.def.building.isSittable
                         && (t.Faction == null || pawn.Faction == null || t.Faction == pawn.Faction)
                         && t.Position.GetDangerFor(pawn, map) == Danger.None
                         && pawn.CanReserveSittableOrSpot(t.Position)
                         && ChairNearTable(t, map));

                if (chair == null) return;
                if (!Toils_Ingest.TryFindFreeSittingSpotOnThing(chair, pawn, out IntVec3 spot)) return;

                // 座位旁边（上下左右）必须真有桌子，否则坐了也一样吃不到桌面
                if (!HasTableNextTo(spot, map)) return;

                cell = spot;
                __result = true;
            }
            catch
            {
                // 找不到就维持原版行为
            }
        }
    }
}
