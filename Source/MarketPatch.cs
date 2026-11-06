using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 购买结算的落点：东西真的进背包那一刻才扣钱。
    /// </summary>
    public static class MarketPatch
    {
        public static bool Patch(Harmony harmony)
        {
            MethodInfo target = AccessTools.Method(typeof(ThingOwner<Thing>), "TryAdd", new[] { typeof(Thing), typeof(bool) });
            if (target == null)
            {
                Log.Warning("[DSH-ECONOMY] 市场结算：没找到 ThingOwner.TryAdd");
                return false;
            }
            harmony.Patch(target,
                postfix: new HarmonyMethod(typeof(MarketPatch).GetMethod(nameof(TryAddPostfix), BindingFlags.Static | BindingFlags.NonPublic)));
            return true;
        }

        private static void TryAddPostfix(ThingOwner<Thing> __instance, Thing item, bool canMergeWithExistingStacks, bool __result)
        {
            if (!__result || item == null || __instance == null) return;
            Pawn_InventoryTracker inv = __instance.Owner as Pawn_InventoryTracker;
            if (inv == null || inv.pawn == null) return;
            EconomyMarket.OnInventoryAdded(inv.pawn, item);

            // 公共白银进了背包：登记一下，让搬运逻辑把它送回仓库（或丢下）
            if (item.def == ThingDefOf.Silver && inv.pawn.IsColonist && EconomyPrivate.OwnerOf(item) == null)
            {
                EconomyPublicSilver.MarkDirty(inv.pawn);
            }

            // 只给"市场购买"打私有：任务奖励、拾取、搬运不该被误标。
            if (item.def != null && inv.pawn.IsColonist
                && EconomyMarket.HasPending(inv.pawn)
                && EconomyPrivate.OwnerOf(item) == null)
            {
                EconomyPrivate.SetPrivate(item, inv.pawn);
            }
        }
    }
}
