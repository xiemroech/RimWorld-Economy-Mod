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

        public static int MaxCount(ThingDef def)
        {
            if (def == null) return 1;
            if (def.IsIngestible) return 10;
            if (def.IsWeapon) return 2;
            if (def.IsApparel) return 4;
            return 4;
        }

        /// <summary>目标件数 = 基础件数 × 价格系数 × 财富系数，夹在 1–上限。</summary>
        public static int TargetCount(Pawn p, ThingDef def, Map map)
        {
            if (def == null) return 1;
            float wf = (p != null && map != null) ? EconomyConsumption.WealthFactor(p, map) : 1f;
            float raw = BaseCount(def) * PriceCoef(def, map) * wf;
            return Mathf.Clamp(Mathf.RoundToInt(raw), 1, MaxCount(def));
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
