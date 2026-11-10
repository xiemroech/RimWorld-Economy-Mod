using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 屋内效率：按"房间是否封闭"判定，而不是看脚下有没有屋顶。
    /// 只要这个工作台所在的房间算室内（OutdoorsForWork 为 false），效率打折。
    /// </summary>
    public class StatPart_IndoorWorkbench : StatPart
    {
        private static readonly System.Collections.Generic.HashSet<int> logged = new System.Collections.Generic.HashSet<int>();

        public override void TransformValue(StatRequest req, ref float val)
        {
            if (!req.HasThing) return;
            Building b = req.Thing as Building;
            if (b == null || b.def == null) return;
            if (!b.def.HasModExtension<EconomyWorkplaceExtension>()) return;

            bool indoors = IsIndoors(b);

            if (logged.Add(b.thingIDNumber))
            {
                Room room = b.Spawned && b.Map != null ? b.GetRoom() : null;
                DevLog.Msg("[DSH-ECONOMY] 屋内判定 " + b.def.defName
                    + " 室内=" + indoors
                    + " 有房间=" + (room != null)
                    + " 露天工作=" + (room != null && room.OutdoorsForWork)
                    + " 接地图边缘=" + (room != null && room.TouchesMapEdge)
                    + " 位置=" + b.Position);
            }

            if (indoors) val *= FactorOf(b);
        }

        public override string ExplanationPart(StatRequest req)
        {
            if (!req.HasThing) return null;
            Building b = req.Thing as Building;
            if (b == null || b.def == null) return null;
            if (!b.def.HasModExtension<EconomyWorkplaceExtension>()) return null;
            if (!IsIndoors(b)) return null;
            return "屋内作业：×" + FactorOf(b).ToString("0.00");
        }

        /// <summary>
        /// 室内判定。默认"围墙围起来就算"（不接地图边缘）；
        /// 模组设置里可切回原版判定（必须有屋顶）。
        /// </summary>
        private static bool IsIndoors(Building b)
        {
            if (b == null || !b.Spawned || b.Map == null) return false;
            Room room = b.GetRoom();
            if (room == null) return false;

            DshEconomySettings s = DshEconomyMod.Settings;
            bool needsRoof = s != null && s.indoorNeedsRoof;
            return needsRoof ? !room.OutdoorsForWork : !room.TouchesMapEdge;
        }

        private static float FactorOf(Building b)
        {
            EconomyWorkplaceExtension ext = b.def.GetModExtension<EconomyWorkplaceExtension>();
            return ext != null ? ext.indoorsFactor : 0.5f;
        }
    }
}

