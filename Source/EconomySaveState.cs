using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 存档版本号与迁移入口。每次给存档结构加东西，就把 CurrentVersion +1，
    /// 并在 RunMigrations 里加一段"从旧版本补齐"的逻辑。
    /// </summary>
    public class EconomySaveState : GameComponent
    {
        public const int CurrentVersion = 1;

        public static EconomySaveState Instance;

        // 新游戏从当前版本开始；旧存档没有这个字段，读出来是 0
        private int version = CurrentVersion;

        public int Version
        {
            get { return version; }
        }

        public EconomySaveState(Game game)
        {
            Instance = this;
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Instance = this;
            RunMigrations();
        }

        private void RunMigrations()
        {
            int from = version;
            if (from >= CurrentVersion) return;

            if (from < 1) Migrate_AddItemTags();

            version = CurrentVersion;
            Log.Message("[DSH-ECONOMY] 存档已从 v" + from + " 适配到 v" + CurrentVersion);
        }

        /// <summary>
        /// v0 -> v1：旧存档里已经存在的物品没有归属标签组件，补挂上（默认公共）。
        /// 新产出的物品在挂标签时就有，不受影响。
        /// </summary>
        private void Migrate_AddItemTags()
        {
            try
            {
                int added = 0;
                List<Map> maps = Find.Maps;
                for (int m = 0; m < maps.Count; m++)
                {
                    Map map = maps[m];
                    List<Thing> all = map.listerThings.AllThings;
                    for (int i = 0; i < all.Count; i++)
                    {
                        CompInjector.EnsureTag(all[i], ref added);
                    }

                    IReadOnlyList<Pawn> pawns = map.mapPawns.AllPawnsSpawned;
                    for (int i = 0; i < pawns.Count; i++)
                    {
                        Pawn p = pawns[i];
                        if (p.inventory == null || p.inventory.innerContainer == null) continue;
                        IList<Thing> inner = p.inventory.innerContainer;
                        for (int j = 0; j < inner.Count; j++)
                        {
                            CompInjector.EnsureTag(inner[j], ref added);
                        }
                    }
                }
                if (added > 0) Log.Message("[DSH-ECONOMY] 为 " + added + " 件旧物品补挂了归属标签");
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 旧物品补标签失败（可忽略）：" + e.Message);
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref version, "dsh_saveVersion", 0);

            if (Scribe.mode == LoadSaveMode.LoadingVars || Scribe.mode == LoadSaveMode.PostLoadInit)
            {
                EconomyPrivateStorageBehavior.ResetRuntime();
                EconomyWorkTypes.ResetRuntime();
                EconomyPublicSilver.ResetRuntime();
                JobGiver_EconomyPatientRest.ResetRuntime();
                EconomyUnrest.ClearRuntimeState();
                EconomySatisfactionEvents.Clear();
                EconomyConsumption.ResetRuntime();
                EconomyMarket.ClearRuntime();
                EconomyValue.Reset();
                EconomyEmotionSpread.ResetRuntime();
                if (Scribe.mode == LoadSaveMode.PostLoadInit && Current.Game != null)
                {
                    foreach (Map m in Current.Game.Maps) EconomyMilitary.ReapplyAll(m);
                    // 老存档的文化里没有「财产制度」戒律，这里补一条
                    Map any = Current.Game.Maps != null && Current.Game.Maps.Count > 0 ? Current.Game.Maps[0] : null;
                    EconomyPreceptForcer.Enforce(any);
                }
                EconomySatisfaction.ClearBarracksRuntime();
            }
        }
    }

    /// <summary>往已经存在的 ThingWithComps 上补一个新的 Comp。</summary>
    internal static class CompInjector
    {
        private static readonly FieldInfo compsField =
            typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static bool warned;

        public static void EnsureTag(Thing thing, ref int added)
        {
            if (thing == null || thing.Destroyed) return;
            if (thing.def == null || thing.def.category != ThingCategory.Item) return;

            ThingWithComps twc = thing as ThingWithComps;
            if (twc == null) return;
            if (twc.GetComp<CompItemTag>() != null) return;

            try
            {
                if (compsField == null)
                {
                    if (!warned)
                    {
                        warned = true;
                        Log.Warning("[DSH-ECONOMY] 找不到 ThingWithComps.comps，旧物品无法补标签");
                    }
                    return;
                }

                List<ThingComp> list = compsField.GetValue(twc) as List<ThingComp>;
                if (list == null) return;

                CompItemTag comp = new CompItemTag();
                comp.parent = twc;
                list.Add(comp);
                added++;
            }
            catch (Exception e)
            {
                if (!warned)
                {
                    warned = true;
                    Log.Warning("[DSH-ECONOMY] 补标签出错：" + e.Message);
                }
            }
        }
    }
}
