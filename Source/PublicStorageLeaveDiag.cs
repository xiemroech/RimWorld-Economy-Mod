using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 只读诊断：公共物品被从公共存储里拿走/弹出时，打印调用栈，用来定位是谁在弹。
    /// 不改任何行为，只在少数情况下打印。
    /// </summary>
    public static class PublicStorageLeaveDiag
    {
        private static int lastTick = -99999;
        private static int budget = 12;

        public static void Prefix(Thing __instance)
        {
            try
            {
                if (__instance == null || __instance.Destroyed || !__instance.Spawned) return;
                if (__instance.def == null || __instance.def.category != ThingCategory.Item) return;
                if (EconomyPrivate.OwnerOf(__instance) != null) return;   // 只看公共物

                SlotGroup g = __instance.GetSlotGroup();
                if (g == null) return;
                Building b = g.parent as Building;
                if (b == null) return;
                if (EconomyRealEstate.OwnerOfBuilding(b) != null) return; // 只看公共箱

                int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
                if (now - lastTick < 180) return;
                lastTick = now;
                if (budget-- <= 0) return;

                DevLog.Msg("[STORLEAVE] 公共货 " + __instance.LabelShort + " 离开公共箱 "
                    + b.LabelShort + "，调用栈：\n" + System.Environment.StackTrace);
            }
            catch { }
        }
    }
}

