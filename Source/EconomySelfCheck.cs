using System.Collections.Generic;
using System.Text;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>读档后的自检：只打印，不改任何数据，用日志定位老存档的问题。</summary>
    public static class EconomySelfCheck
    {
        // 读档时地图还没生完，诊断要推迟到第一帧之后再跑
        public static bool PendingDump;

        public static void Run(Map map)
        {
            if (map == null) return;
            int tagged = 0, emptyTag = 0, ground = 0, stored = 0;
            int reservedStale = 0;
            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed || !t.Spawned) continue;
                CompItemTag tag = t.TryGetComp<CompItemTag>();
                if (tag != null)
                {
                    tagged++;
                    if (tag.owner != null)
                    {
                        if (t.GetSlotGroup() != null) stored++; else ground++;
                    }
                    else emptyTag++;
                }
            }

            int roomsNoRole = 0;
            StringBuilder coords = new StringBuilder();
            foreach (Room r in map.regionGrid.AllRooms)
            {
                if (r == null || r.Role != null) continue;
                roomsNoRole++;
                if (roomsNoRole <= 8)
                {
                    IntVec3 c = IntVec3.Invalid;
                    foreach (IntVec3 cc in r.Cells) { c = cc; break; }
                    if (c.IsValid) coords.Append("(").Append(c.x).Append(",").Append(c.z).Append(") ");
                }
            }

            Log.Message("[DSH-SELF] 地图 " + map.uniqueID + "：物品 " + all.Count
                + " / 带标签 " + tagged + "（私有落地 " + ground + "，私有在库 " + stored + "，空标签 " + emptyTag + "）"
                + " / 幽灵预留 " + reservedStale
                + " / 无角色房间 " + roomsNoRole + " " + coords
                + " / 存放组 " + map.haulDestinationManager.AllGroupsListForReading.Count);
        }
    }
}
