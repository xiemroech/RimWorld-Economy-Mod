using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 市场摊位：本质就是一个储物建筑（套皮存储箱），但
    /// 1) 里面的货会被居民优先买走；
    /// 2) 容量按"组"算，100 组，超出的货退到地上。
    /// </summary>
    public class Building_Market : Building_Storage
    {
        /// <summary>容量：100 组（堆），跟玩家的存储 mod 习惯一致。</summary>
        public const int CapacityStacks = 100;

        public override void SpawnSetup(Map map, bool respawningAfterLoad)
        {
            base.SpawnSetup(map, respawningAfterLoad);
            EnsureStorageSettings();
        }

        private int lastCheck;

        protected override void Tick()
        {
            base.Tick();
            int now = Find.TickManager.TicksGame;
            if (now - lastCheck < 2500) return;
            lastCheck = now;
            // 某些存储 mod 会重建过滤器，定期自检：连钢都不收就说明被清空了
            if (GetStoreSettings() != null && GetStoreSettings().filter != null
                && !GetStoreSettings().filter.Allows(ThingDefOf.Steel))
                EnsureStorageSettings();
        }

        /// <summary>
        /// XML 里的 fixedStorageSettings 在部分 mod 环境下会被解析成空过滤器，
        /// 导致"什么都放不进去"。这里在建筑生成时直接把过滤器改成"全部允许"，
        /// 再单独关掉砖块/植物/建筑这些不该进摊位的类别。
        /// </summary>
        private void EnsureStorageSettings()
        {
            try
            {
                StorageSettings s = GetStoreSettings();
                if (s == null || s.filter == null) return;

                // 先整体允许：SetAllowAll(null) 在某些版本/mod 环境下不生效，
                // 所以再逐条 ThingDef 兜底打开一次。
                try { s.filter.SetAllowAll(null); } catch (System.Exception ex) { EcoLog.WarnOnce("Building_Market", ex); }
                int n = 0;
                foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
                {
                    if (d.category == null) continue;
                    if (d.IsCorpse) continue;
                    s.filter.SetAllow(d, true);
                    n++;
                }
                Disallow("Chunks");
                Disallow("Plants");
                Disallow("Buildings");
                s.Priority = StoragePriority.Preferred;

                if (!loggedFilter)
                {
                    loggedFilter = true;
                    Log.Message("[DSH-ECONOMY] 市场摊位过滤器：允许 " + n + " 种定义，优先级 " + s.Priority
                        + "，槽组=" + (GetSlotGroup() != null ? "有" : "无"));
                }
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 市场摊位过滤器设置失败：" + e.Message);
            }
        }

        private bool loggedFilter;

        private void Disallow(string categoryDefName)
        {
            ThingCategoryDef cat = DefDatabase<ThingCategoryDef>.GetNamedSilentFail(categoryDefName);
            if (cat != null) GetStoreSettings().filter.SetAllow(cat, false);
        }

        public int StockStacks()
        {
            SlotGroup group = GetSlotGroup();
            if (group == null) return 0;
            int n = 0;
            foreach (Thing t in group.HeldThings)
            {
                if (t != null && !t.Destroyed) n++;
            }
            return n;
        }

        public override void Notify_ReceivedThing(Thing newItem)
        {
            base.Notify_ReceivedThing(newItem);
            if (newItem == null) return;
            if (StockStacks() > CapacityStacks)
            {
                // 不能在这里直接 DeSpawn：会和在存储回调里继续用这个 Thing 的 mod 打架。
                StorageEjectQueue.Enqueue(newItem);
            }
        }
    }
}
