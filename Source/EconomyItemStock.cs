using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 「按价格决定囤几件」的统一公式：食物、衣物、武器、耗材共用。
    /// 便宜的多囤，贵的少囤；再乘上个人的财富系数。
    /// </summary>
    public static class EconomyItemStock
    {
        /// <summary>参考价：低于它算便宜（多囤），高于它算贵（少囤）。</summary>
        public const float PriceReference = 30f;
        public const float MinCoef = 0.4f;
        public const float MaxCoef = 3.0f;

        /// <summary>价格系数：参考价 / 单价，夹在 0.4–3.0。</summary>
        public static float PriceCoef(ThingDef def, Map map)
        {
            if (def == null) return 1f;
            float price = EconomyPrice.UnitPrice(def, map);
            if (price <= 0.01f) return MaxCoef;
            return Mathf.Clamp(PriceReference / price, MinCoef, MaxCoef);
        }

        /// <summary>基础件数：食物按顿（6 顿），武器 1，衣物 2，其它 2。</summary>
        public static int BaseCount(ThingDef def)
        {
            if (def == null) return 1;
            if (def.IsIngestible) return 6;
            if (def.IsWeapon) return 1;
            if (def.IsApparel) return 2;
            return 2;
        }

        /// <summary>上限。衣物按财富放大（原版有背包/衣柜这种容量，富人本来就囤得多）。</summary>
        public static int MaxCount(ThingDef def)
        {
            if (def == null) return 1;
            if (def.IsIngestible) return 10;
            if (def.IsWeapon) return 2;
            if (def.IsApparel) return 4;
            return 4;
        }

        /// <summary>衣物上限随财富走：穷人 4 件，富人最多 12 件。</summary>
        public static int ApparelCap(Pawn p, Map map)
        {
            float wf = (p != null && map != null) ? EconomyConsumption.WealthFactor(p, map) : 1f;
            // wf 大致落在 0.75~1.6：0.75→4，1.15→7，1.6→11
            return Mathf.Clamp(4 + Mathf.RoundToInt((wf - 0.75f) * 8f), 4, 12);
        }

        /// <summary>目标件数 = 基础件数 × 价格系数 × 财富系数，夹在 1–上限。</summary>
        public static int TargetCount(Pawn p, ThingDef def, Map map)
        {
            if (def == null) return 1;
            float wf = (p != null && map != null) ? EconomyConsumption.WealthFactor(p, map) : 1f;
            float raw = BaseCount(def) * PriceCoef(def, map) * wf;
            int cap = MaxCount(def);
            if (def.IsApparel && p != null && map != null) cap = ApparelCap(p, map);
            return Mathf.Clamp(Mathf.RoundToInt(raw), 1, cap);
        }

        /// <summary>衣物层：内衣(OnSkin)/中衣(Middle)/外衣(Shell)。</summary>
        public static ApparelLayerDef LayerOf(ThingDef def)
        {
            if (def == null || def.apparel == null) return null;
            return def.apparel.LastLayer;
        }

        /// <summary>这个人身上是否已经有这一层的衣服（用于"每层至少一件"的补货判断）。</summary>
        public static bool LayerCovered(Pawn p, ThingDef def)
        {
            ApparelLayerDef layer = LayerOf(def);
            if (layer == null) return true;              // 不是衣服：不按层判断
            if (p == null || p.apparel == null) return false;
            var worn = p.apparel.WornApparel;
            if (worn == null) return false;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel a = worn[i];
                if (a == null || a.def == null || a.def.apparel == null) continue;
                if (a.def.apparel.LastLayer == layer) return true;
            }
            return false;
        }

        /// <summary>身上缺了几层（0–3）：内衣/中衣/外衣。</summary>
        public static int MissingLayers(Pawn p)
        {
            int missing = 0;
            missing += LayerCoveredByName(p, "OnSkin") ? 0 : 1;
            missing += LayerCoveredByName(p, "Middle") ? 0 : 1;
            missing += LayerCoveredByName(p, "Shell") ? 0 : 1;
            return missing;
        }

        private static bool LayerCoveredByName(Pawn p, string layerName)
        {
            if (p == null || p.apparel == null || p.apparel.WornApparel == null) return false;
            var worn = p.apparel.WornApparel;
            for (int i = 0; i < worn.Count; i++)
            {
                Apparel a = worn[i];
                if (a == null || a.def == null || a.def.apparel == null) continue;
                ApparelLayerDef l = a.def.apparel.LastLayer;
                if (l != null && l.defName == layerName) return true;
            }
            return false;
        }

        /// <summary>这个人已经拥有多少件（背包私有 + 身上穿 + 手里拿 + 自己仓库里的）。</summary>
        public static int CountOwned(Pawn p, ThingDef def)
        {
            if (p == null || def == null) return 0;
            int n = 0;

            if (p.inventory != null && p.inventory.innerContainer != null)
            {
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (t == null || t.def != def) continue;
                    if (EconomyPrivate.OwnerOf(t) != p) continue;
                    n += t.stackCount;
                }
            }

            if (p.apparel != null && p.apparel.WornApparel != null)
            {
                foreach (Apparel a in p.apparel.WornApparel)
                {
                    if (a != null && a.def == def) n++;
                }
            }

            if (p.equipment != null && p.equipment.Primary != null && p.equipment.Primary.def == def) n++;

            Map map = p.MapHeld;
            if (map != null && map.haulDestinationManager != null)
            {
                foreach (SlotGroup g in map.haulDestinationManager.AllGroupsListForReading)
                {
                    if (g == null || g.HeldThings == null) continue;
                    if (EconomyPrivateStorageBehavior.OwnerOfGroup(g) != p) continue;
                    foreach (Thing t in g.HeldThings)
                    {
                        if (t != null && t.def == def) n += t.stackCount;
                    }
                }
            }
            return n;
        }

        /// <summary>还差几件（≤0 表示够用）。</summary>
        public static int Needed(Pawn p, ThingDef def, Map map)
        {
            return TargetCount(p, def, map) - CountOwned(p, def);
        }
    }
}
