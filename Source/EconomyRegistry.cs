using System;
using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 全局物品 -> 经济分类索引。整个 mod 里只有这里知道"物品属于哪个分类"，
    /// 其他系统一律通过 CategoryOf 查询，不允许出现写死的物品名单。
    /// </summary>
    public static class EconomyRegistry
    {
        private static readonly Dictionary<ThingDef, EconomyCategoryDef> lookup = new Dictionary<ThingDef, EconomyCategoryDef>();
        private static readonly Dictionary<EconomyCategoryDef, List<ThingDef>> byCategory = new Dictionary<EconomyCategoryDef, List<ThingDef>>();
        private static readonly Dictionary<ThingDef, EconomyCategoryDef> runtimeOverrides = new Dictionary<ThingDef, EconomyCategoryDef>();
        private static readonly List<EconomyCategoryDef> categories = new List<EconomyCategoryDef>();
        private static readonly List<ThingDef> currencies = new List<ThingDef>();
        private static readonly Dictionary<EconomyCategoryDef, ThingDef> samples = new Dictionary<EconomyCategoryDef, ThingDef>();

        /// <summary>重建完成后触发。其他 mod 可在这里追加运行时条目。</summary>
        public static event Action Rebuilt;

        public static bool Built { get; private set; }
        public static IReadOnlyList<EconomyCategoryDef> Categories => categories;
        public static IReadOnlyList<ThingDef> Currencies => currencies;

        /// <summary>主货币。默认是第一个声明 isCurrency 的物品，否则退回原版白银。</summary>
        public static ThingDef PrimaryCurrency => currencies.Count > 0 ? currencies[0] : null;

        public static int CoveredCount => lookup.Count;

        /// <summary>清空并重新扫描所有 ThingDef。可以反复调用。</summary>
        public static void Rebuild()
        {
            lookup.Clear();
            byCategory.Clear();
            categories.Clear();
            currencies.Clear();
            samples.Clear();

            categories.AddRange(DefDatabase<EconomyCategoryDef>.AllDefsListForReading);
            categories.RemoveAll(c => c == null);
            categories.SortByDescending(c => c.priority);

            for (int i = 0; i < categories.Count; i++)
                byCategory[categories[i]] = new List<ThingDef>();

            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def == null) continue;
                EconomyItemExtension ext = def.GetModExtension<EconomyItemExtension>();
                if (ext != null && ext.exclude) continue;
                if (IsCorpseDef(def)) continue;

                if (ext != null && ext.isCurrency)
                {
                    currencies.Add(def);
                    continue;
                }

                // 物品以外的东西（建筑、植物、小人……）只有在显式声明时才纳入
                if (ext == null && def.category != ThingCategory.Item) continue;

                EconomyCategoryDef cat = Resolve(def, ext);
                if (cat == null) continue;

                lookup[def] = cat;
                byCategory[cat].Add(def);
            }

            if (currencies.Count == 0)
            {
                ThingDef silver = DefDatabase<ThingDef>.GetNamedSilentFail("Silver");
                if (silver != null) currencies.Add(silver);
            }

            // 储物建筑单独归入「容器」分类（它们不是物品，普通扫描扫不到）
            EconomyCategoryDef containerCat = null;
            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i] != null && categories[i].defName == "DshEco_Container")
                {
                    containerCat = categories[i];
                    break;
                }
            }
            if (containerCat != null)
            {
                List<ThingDef> allDefs = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < allDefs.Count; i++)
                {
                    ThingDef d = allDefs[i];
                    if (d == null || d.thingClass == null) continue;
                    if (!typeof(Building_Storage).IsAssignableFrom(d.thingClass)) continue;
                    lookup[d] = containerCat;
                    if (!byCategory[containerCat].Contains(d)) byCategory[containerCat].Add(d);
                }
                samples.Remove(containerCat);
            }

            // 弹药：优先投到独立的「弹药」分类，没有这个分类才退回「武器」类。
            // 判据用 CEAmmo.IsAmmoDef（CE 自己也不统一，只能按分类/tradeTag/名字猜）。
            EconomyCategoryDef ammoCat = null;
            EconomyCategoryDef weaponCat = null;
            for (int i = 0; i < categories.Count; i++)
            {
                if (categories[i] == null) continue;
                if (ammoCat == null && categories[i].defName == "DshEco_Ammo") ammoCat = categories[i];
                if (weaponCat == null && categories[i].defName == "DshEco_Weapon") weaponCat = categories[i];
            }
            EconomyCategoryDef ammoTarget = ammoCat != null ? ammoCat : weaponCat;
            if (ammoTarget != null)
            {
                List<ThingDef> allDefs2 = DefDatabase<ThingDef>.AllDefsListForReading;
                for (int i = 0; i < allDefs2.Count; i++)
                {
                    ThingDef d = allDefs2[i];
                    if (d == null || !CEAmmo.IsAmmoDef(d)) continue;
                    EconomyCategoryDef prev;
                    if (lookup.TryGetValue(d, out prev) && prev != null && prev != ammoTarget)
                    {
                        List<ThingDef> old;
                        if (byCategory.TryGetValue(prev, out old) && old != null) old.Remove(d);
                    }
                    lookup[d] = ammoTarget;
                    if (!byCategory[ammoTarget].Contains(d)) byCategory[ammoTarget].Add(d);
                }
            }

            Built = true;
            Action act = Rebuilt;
            if (act != null) act();
        }

        private static EconomyCategoryDef Resolve(ThingDef def, EconomyItemExtension ext)
        {
            EconomyCategoryDef forced;
            if (runtimeOverrides.TryGetValue(def, out forced) && forced != null) return forced;

            if (ext != null && ext.category != null) return ext.category;

            for (int i = 0; i < categories.Count; i++)
            {
                EconomyCategoryDef c = categories[i];
                if (c.isFallback) continue;
                if (Matches(c, def)) return c;
            }

            for (int i = 0; i < categories.Count; i++)
                if (categories[i].isFallback) return categories[i];

            return null;
        }

        private static bool Matches(EconomyCategoryDef cat, ThingDef def)
        {
            if (cat.matchThingCategories != null && cat.matchThingCategories.Count > 0
                && def.thingCategories != null && def.thingCategories.Count > 0)
            {
                for (int i = 0; i < def.thingCategories.Count; i++)
                {
                    ThingCategoryDef tc = def.thingCategories[i];
                    for (int j = 0; j < cat.matchThingCategories.Count; j++)
                    {
                        if (IsDescendantOf(tc, cat.matchThingCategories[j])) return true;
                    }
                }
            }

            if (cat.matchTradeTags != null && cat.matchTradeTags.Count > 0 && def.tradeTags != null)
            {
                for (int i = 0; i < cat.matchTradeTags.Count; i++)
                {
                    if (def.tradeTags.Contains(cat.matchTradeTags[i])) return true;
                }
            }

            if (cat.matchDefNameContains != null && cat.matchDefNameContains.Count > 0 && !string.IsNullOrEmpty(def.defName))
            {
                for (int i = 0; i < cat.matchDefNameContains.Count; i++)
                {
                    string k = cat.matchDefNameContains[i];
                    if (!string.IsNullOrEmpty(k)
                        && def.defName.IndexOf(k, System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }

            return false;
        }

        private static bool IsDescendantOf(ThingCategoryDef cat, ThingCategoryDef ancestor)
        {
            if (cat == null || ancestor == null) return false;
            for (ThingCategoryDef c = cat; c != null; c = c.parent)
            {
                if (c == ancestor) return true;
            }
            return false;
        }

        private static bool IsCorpseDef(ThingDef def)
        {
            return def.thingClass != null && typeof(Corpse).IsAssignableFrom(def.thingClass);
        }

        /// <summary>查物品所属分类。未纳入经济的物品返回 null。</summary>
        public static EconomyCategoryDef CategoryOf(ThingDef def)
        {
            if (def == null) return null;
            EconomyCategoryDef cat;
            return lookup.TryGetValue(def, out cat) ? cat : null;
        }

        public static bool IsCurrency(ThingDef def)
        {
            if (def == null) return false;
            return currencies.Contains(def);
        }

        /// <summary>某分类下的所有物品。返回内部列表，不要修改。</summary>
        public static List<ThingDef> ItemsIn(EconomyCategoryDef cat)
        {
            if (cat == null) return null;
            List<ThingDef> list;
            return byCategory.TryGetValue(cat, out list) ? list : null;
        }

        public static EconomyCategoryDef FallbackCategory
        {
            get
            {
                for (int i = 0; i < categories.Count; i++)
                    if (categories[i].isFallback) return categories[i];
                return null;
            }
        }

        /// <summary>
        /// 运行时把某个 ThingDef 指派到分类（覆盖 XML 匹配结果）。
        /// 其他 mod 可以在自己的 StaticConstructorOnStartup 里调用。
        /// </summary>
        public static void RegisterOverride(ThingDef def, EconomyCategoryDef cat)
        {
            if (def == null) return;
            runtimeOverrides[def] = cat;
            if (cat != null && byCategory.ContainsKey(cat))
            {
                lookup[def] = cat;
                if (!byCategory[cat].Contains(def)) byCategory[cat].Add(def);
            }
        }

        public static void ClearOverrides()
        {
            runtimeOverrides.Clear();
        }

        /// <summary>取一个分类里最有代表性的物品，用于界面展示。</summary>
        public static ThingDef SampleItem(EconomyCategoryDef cat)
        {
            if (cat == null) return null;
            ThingDef cached;
            if (samples.TryGetValue(cat, out cached)) return cached;

            List<ThingDef> list = ItemsIn(cat);
            ThingDef pick = null;
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i].GetStatValueAbstract(StatDefOf.MarketValue) > 0f)
                    {
                        pick = list[i];
                        break;
                    }
                }
            }
            samples[cat] = pick;
            return pick;
        }

        /// <summary>统计：有多少物品类 ThingDef 没被纳入经济。</summary>
        public static int CountUncovered()
        {
            int n = 0;
            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def == null) continue;
                if (def.category != ThingCategory.Item) continue;
                if (IsCorpseDef(def)) continue;
                if (CategoryOf(def) == null) n++;
            }
            return n;
        }

        /// <summary>调试用：把注册结果汇总成一段文本。</summary>
        public static string Describe()
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.AppendLine("[DSH-ECONOMY] 分类 " + categories.Count + " 个，物品 " + lookup.Count + " 种，货币 " + currencies.Count + " 种");
            for (int i = 0; i < categories.Count; i++)
            {
                List<ThingDef> list = ItemsIn(categories[i]);
                sb.AppendLine("  " + categories[i].defName + " (" + (list != null ? list.Count : 0) + " 项)");
            }
            return sb.ToString();
        }
    }
}
