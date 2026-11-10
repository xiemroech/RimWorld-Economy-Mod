using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 「不是自己的私有物不能穿、不能拿」——和"别人的私有食物不能吃"同一套规则。
    /// 只看私有标签：公共物品照常可用，否则开局没私有衣物会光着。
    /// </summary>
    public static class PrivateUseBlock
    {
        public static bool Patch(Harmony harmony)
        {
            bool ok = false;

            MethodInfo wear = AccessTools.Method(typeof(Pawn_ApparelTracker), "Wear",
                new[] { typeof(Apparel), typeof(bool), typeof(bool) });
            if (wear != null)
            {
                harmony.Patch(wear, prefix: new HarmonyMethod(
                    typeof(PrivateUseBlock).GetMethod(nameof(ApparelPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                DevLog.Msg("[DSH-ECONOMY] 别人的私有衣物不能穿：已挂载");
                ok = true;
            }
            else Log.Warning("[DSH-ECONOMY] 没找到 Pawn_ApparelTracker.Wear");

            MethodInfo equip = AccessTools.Method(typeof(Pawn_EquipmentTracker), "AddEquipment",
                new[] { typeof(ThingWithComps) });
            if (equip != null)
            {
                harmony.Patch(equip, prefix: new HarmonyMethod(
                    typeof(PrivateUseBlock).GetMethod(nameof(EquipPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                DevLog.Msg("[DSH-ECONOMY] 别人的私有武器不能拿：已挂载");
                ok = true;
            }
            else Log.Warning("[DSH-ECONOMY] 没找到 Pawn_EquipmentTracker.AddEquipment");

            return ok;
        }

        private static bool ApparelPrefix(Pawn_ApparelTracker __instance, Thing newApparel)
        {
            Pawn p = __instance != null ? __instance.pawn : null;
            if (p == null || newApparel == null) return true;
            Pawn owner = EconomyPrivate.OwnerOf(newApparel);
            if (owner == null || owner == p) return true;
            DevLog.Msg("[USE] " + p.LabelShort + " 想穿别人的私人物品（" + newApparel.LabelShort + "，物主 "
                + (owner.LabelShort ?? "?") + "），已拒绝");
            return false;
        }

        private static bool EquipPrefix(Pawn_EquipmentTracker __instance, ThingWithComps newEq)
        {
            Pawn p = __instance != null ? __instance.pawn : null;
            if (p == null || newEq == null) return true;
            Pawn owner = EconomyPrivate.OwnerOf(newEq);
            if (owner == null || owner == p) return true;
            DevLog.Msg("[USE] " + p.LabelShort + " 想拿别人的私人物品（" + newEq.LabelShort + "，物主 "
                + (owner.LabelShort ?? "?") + "），已拒绝");
            return false;
        }
    }
}

