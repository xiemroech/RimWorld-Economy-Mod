using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 增量库存统计：物品生成/销毁时直接在库存表上加减，
    /// 全量重建降到每 2500 tick 一次（用来校正漂移）。
    /// 关掉这个功能就退回每 250 tick 全量重建。
    /// </summary>
    public static class StockIncrementalPatch
    {
        public static void SpawnPostfix(Thing __instance)
        {
            if (!Enabled()) return;
            Thing t = __instance;
            if (t == null || t.def == null) return;
            if (t.def.category != ThingCategory.Item) return;
            Map map = t.Map;
            if (map == null) return;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return;
            comp.AddStock(t.def, t.stackCount);
        }

        /// <summary>销毁前先把数量扣掉（DeSpawn 期间 stackCount 还没变）。</summary>
        public static void DespawnPrefix(Thing __instance)
        {
            if (!Enabled()) return;
            Thing t = __instance;
            if (t == null || t.def == null) return;
            if (t.def.category != ThingCategory.Item) return;
            Map map = t.Map;
            if (map == null) return;
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return;
            comp.AddStock(t.def, -t.stackCount);
        }

        private static bool Enabled()
        {
            DshEconomySettings s = DshEconomyMod.Settings;
            return s != null && s.incrementalStock;
        }
    }
}
