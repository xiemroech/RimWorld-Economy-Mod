using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    public enum EconomyPanelMode
    {
        Categories,
        Taxes,
        Policy,
        Residents,
        Private,
        Market
    }

    /// <summary>窗口和模组设置页共用的面板：分类公共化 / 商品税 两个页签。</summary>
    public static class EconomyPanel
    {
        public const float RowHeight = 46f;

        public static void Draw(Rect inRect, MapEconomyComponent comp, Map map, ref Vector2 scroll, ref EconomyPanelMode mode)
        {
            if (Widgets.ButtonText(new Rect(inRect.x, inRect.y, 130f, 26f), "社会化"))
            {
                mode = EconomyPanelMode.Categories;
                scroll = Vector2.zero;
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 140f, inRect.y, 100f, 26f), "商品税"))
            {
                mode = EconomyPanelMode.Taxes;
                scroll = Vector2.zero;
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 250f, inRect.y, 100f, 26f), "税收政策"))
            {
                mode = EconomyPanelMode.Policy;
                scroll = Vector2.zero;
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 360f, inRect.y, 90f, 26f), "工资"))
            {
                mode = EconomyPanelMode.Residents;
                scroll = Vector2.zero;
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 460f, inRect.y, 70f, 26f), "私产"))
            {
                mode = EconomyPanelMode.Private;
                scroll = Vector2.zero;
            }
            if (Widgets.ButtonText(new Rect(inRect.x + 540f, inRect.y, 70f, 26f), "价格"))
            {
                mode = EconomyPanelMode.Market;
                scroll = Vector2.zero;
            }

            Rect listRect = new Rect(inRect.x, inRect.y + 32f, inRect.width, inRect.yMax - inRect.y - 32f);
            if (mode == EconomyPanelMode.Taxes) DrawTaxList(listRect, comp, map, ref scroll);
            else if (mode == EconomyPanelMode.Market) DrawMarket(listRect, comp, map, ref scroll);
            else if (mode == EconomyPanelMode.Policy) DrawTaxPolicy(listRect, comp, map, ref scroll);
            else if (mode == EconomyPanelMode.Residents) EconomyPanelResidents.Draw(listRect, comp, map, ref scroll);
            else if (mode == EconomyPanelMode.Private) DrawPrivate(listRect, comp, map, ref scroll);
            else
            {
                DrawDoctrineRow(new Rect(listRect.x, listRect.y, listRect.width, 32f), comp, map);
                DrawMedicalRow(new Rect(listRect.x, listRect.y + 34f, listRect.width, 32f), comp);
                DrawEducationRow(new Rect(listRect.x, listRect.y + 68f, listRect.width, 32f), comp);
                DrawCategoryList(new Rect(listRect.x, listRect.y + 110f, listRect.width, listRect.height - 110f), comp, map, ref scroll);
            }
        }

        /// <summary>文化制度：私有制 / 公有制，决定社会化的上下限。</summary>
        private static void DrawDoctrineRow(Rect rect, MapEconomyComponent comp, Map map)
        {
            // 制度由殖民地的文化决定，不能手动切换：有集体主义/和睦团体 → 公有制，否则 → 私有制。
            bool pub = EconomyDoctrine.IsPublic(map);
            Widgets.Label(new Rect(rect.x, rect.y + 4f, 260f, 24f),
                pub ? "制度：公有制（文化强制）" : "制度：私有制（文化强制）");

        }

        /// <summary>学费设置，放在「社会化」页签顶部、医疗下面。</summary>
        private static void DrawEducationRow(Rect rect, MapEconomyComponent comp)
        {
            if (comp == null || comp.education == null) return;
            EducationPolicy e = comp.education;

            Widgets.Checkbox(new Vector2(rect.x + 2f, rect.y + 3f), ref e.enabled, 24f);
            Widgets.Label(new Rect(rect.x + 32f, rect.y + 4f, 70f, 24f), "学费");

            if (!e.enabled)
            {
                Widgets.Label(new Rect(rect.x + 112f, rect.y + 4f, 620f, 24f),
                    "已关闭：不向父母收取学费");
                return;
            }

            Widgets.Label(new Rect(rect.x + 112f, rect.y + 4f, 60f, 24f), "每童");
            if (Widgets.ButtonText(new Rect(rect.x + 154f, rect.y, 34f, 26f), "-5"))
            {
                e.feePerChild = Mathf.Clamp(e.feePerChild - 5f, 0f, 1000f);
            }
            string feeText = e.feePerChild.ToString("0");
            string feeEdited = Widgets.TextField(new Rect(rect.x + 192f, rect.y, 50f, 24f), feeText);
            float feeParsed;
            if (feeEdited != feeText && float.TryParse(feeEdited, out feeParsed))
            {
                e.feePerChild = Mathf.Clamp(feeParsed, 0f, 1000f);
            }
            if (Widgets.ButtonText(new Rect(rect.x + 246f, rect.y, 34f, 26f), "+5"))
            {
                e.feePerChild = Mathf.Clamp(e.feePerChild + 5f, 0f, 1000f);
            }
            Widgets.Label(new Rect(rect.x + 290f, rect.y + 4f, 460f, 24f),
                "每次发薪向父母收，父母个人白银 → 殖民地；无父母或没钱则跳过");
        }

        /// <summary>医疗收费设置，放在「社会化」页签顶部。</summary>
        private static void DrawMedicalRow(Rect rect, MapEconomyComponent comp)
        {
            if (comp == null || comp.medical == null) return;
            MedicalPolicy m = comp.medical;

            Widgets.Checkbox(new Vector2(rect.x + 2f, rect.y + 3f), ref m.enabled, 24f);
            Widgets.Label(new Rect(rect.x + 32f, rect.y + 4f, 70f, 24f), "医疗收费");

            if (!m.enabled)
            {
                Widgets.Label(new Rect(rect.x + 112f, rect.y + 4f, 620f, 24f),
                    "已关闭：治疗不收费，小人的个人白银不会被扣");
                return;
            }

            Widgets.Label(new Rect(rect.x + 112f, rect.y + 4f, 40f, 24f), "基础");
            if (Widgets.ButtonText(new Rect(rect.x + 154f, rect.y, 30f, 26f), "-5"))
            {
                m.baseFee = Mathf.Clamp(m.baseFee - 5f, 0f, 300f);
            }
            Widgets.Label(new Rect(rect.x + 188f, rect.y + 4f, 46f, 24f), m.baseFee.ToString("0"));
            if (Widgets.ButtonText(new Rect(rect.x + 238f, rect.y, 30f, 26f), "+5"))
            {
                m.baseFee = Mathf.Clamp(m.baseFee + 5f, 0f, 300f);
            }
            Widgets.Label(new Rect(rect.x + 282f, rect.y + 4f, 40f, 24f), "每级");
            if (Widgets.ButtonText(new Rect(rect.x + 324f, rect.y, 30f, 26f), "-1"))
            {
                m.perSkillLevel = Mathf.Clamp(m.perSkillLevel - 1f, 0f, 50f);
            }
            Widgets.Label(new Rect(rect.x + 358f, rect.y + 4f, 40f, 24f), m.perSkillLevel.ToString("0"));
            if (Widgets.ButtonText(new Rect(rect.x + 400f, rect.y, 30f, 26f), "+1"))
            {
                m.perSkillLevel = Mathf.Clamp(m.perSkillLevel + 1f, 0f, 50f);
            }
            Widgets.Label(new Rect(rect.x + 440f, rect.y + 4f, 320f, 24f), "病人个人白银 → 殖民地公共库存");
        }

        private static void DrawCategoryList(Rect inRect, MapEconomyComponent comp, Map map, ref Vector2 scroll)
        {
            IReadOnlyList<EconomyCategoryDef> cats = EconomyRegistry.Categories;
            Rect outRect = inRect;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, cats.Count * RowHeight + 4f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 2f;
            for (int i = 0; i < cats.Count; i++)
            {
                DrawCategoryRow(new Rect(4f, y, viewRect.width - 8f, RowHeight - 2f), cats[i], comp, map, i % 2 == 0);
                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        // 分类行的输入框缓冲：defName → 正在输入的文字
        private static readonly Dictionary<string, string> pubBuffers = new Dictionary<string, string>();

        public static void DrawCategoryRow(Rect row, EconomyCategoryDef cat, MapEconomyComponent comp, Map map, bool alternate)
        {
            if (alternate) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
            if (cat == null) return;

            float pub = comp != null ? comp.GetPublicization(cat) : cat.defaultPublicization;
            string name = cat.label;
            if (comp != null && comp.IsOverridden(cat)) name += " *";
            Widgets.Label(new Rect(row.x + 6f, row.y + 6f, 120f, 24f), name);

            if (comp != null)
            {
                // 拖动条
                float slided = Widgets.HorizontalSlider(
                    new Rect(row.x + 128f, row.y + 11f, 210f, 24f), pub, 0f, 100f, false, null, null, null, 1f);
                if (Mathf.Abs(slided - pub) > 0.01f)
                {
                    comp.SetPublicization(cat, slided);
                    pubBuffers[cat.defName] = slided.ToString("0");
                }

                // 可直接输入精确数值
                string buf;
                if (!pubBuffers.TryGetValue(cat.defName, out buf)) buf = pub.ToString("0");
                string typed = Widgets.TextField(new Rect(row.x + 342f, row.y + 4f, 56f, 24f), buf);
                if (typed != buf)
                {
                    pubBuffers[cat.defName] = typed;
                    float parsed;
                    if (float.TryParse(typed, out parsed))
                    {
                        comp.SetPublicization(cat, Mathf.Clamp(parsed, 0f, 100f));
                    }
                }
                Widgets.Label(new Rect(row.x + 400f, row.y + 6f, 20f, 24f), "%");
            }
            else
            {
                Widgets.Label(new Rect(row.x + 128f, row.y + 6f, 100f, 24f), pub.ToString("0") + "%");
            }

            float factor = EconomyPrice.PublicizationFactor(pub) * Mathf.Max(0f, cat.priceFactor);
            Widgets.Label(new Rect(row.x + 424f, row.y + 6f, 110f, 24f), "价格 × " + factor.ToString("0.00"));

            ThingDef sample = EconomyRegistry.SampleItem(cat);
            if (sample != null && map != null)
            {
                Widgets.Label(new Rect(row.x + 540f, row.y + 6f, row.width - 546f, 24f),
                    sample.label + "  " + EconomyPrice.UnitPrice(sample, map).ToString("0.##"));
            }
            else if (sample != null)
            {
                Widgets.Label(new Rect(row.x + 540f, row.y + 6f, row.width - 546f, 24f), sample.label);
            }
        }

        private static void DrawTaxList(Rect inRect, MapEconomyComponent comp, Map map, ref Vector2 scroll)
        {
            if (comp == null)
            {
                Widgets.Label(inRect, "需要在地图中使用。");
                return;
            }

            int count = DshEconomyMod.Settings != null ? DshEconomyMod.Settings.taxSlotCount : 4;
            comp.EnsureTaxSlots(count);
            if (count <= 0)
            {
                Widgets.Label(inRect, "商品税栏位为 0，可在模组设置里增加。");
                return;
            }

            Widgets.Label(new Rect(inRect.x + 6f, inRect.y, 280f, 22f), "商品（点击选择）");
            Widgets.Label(new Rect(inRect.x + 300f, inRect.y, 100f, 22f), "税率");
            Widgets.Label(new Rect(inRect.x + 520f, inRect.y, 200f, 22f), "最终单价");

            Rect outRect = new Rect(inRect.x, inRect.y + 24f, inRect.width, inRect.yMax - inRect.y - 24f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, count * RowHeight + 4f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 2f;
            for (int i = 0; i < count; i++)
            {
                TaxSlot slot = comp.taxSlots[i];
                Rect row = new Rect(4f, y, viewRect.width - 8f, RowHeight - 2f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));

                string name = slot.thing != null ? slot.thing.label : "（空）点击选择商品";
                if (Widgets.ButtonText(new Rect(row.x + 4f, row.y + 4f, 280f, 24f), name))
                {
                    int index = i;
                    Find.WindowStack.Add(new Window_ItemPicker(delegate(ThingDef d)
                    {
                        if (index < comp.taxSlots.Count) comp.taxSlots[index].thing = d;
                    }));
                }

                if (Widgets.ButtonText(new Rect(row.x + 300f, row.y + 4f, 34f, 24f), "-10"))
                {
                    slot.rate = Mathf.Clamp(slot.rate - 10f, -100f, 200f);
                }
                Widgets.Label(new Rect(row.x + 338f, row.y + 6f, 66f, 24f), slot.rate.ToString("0") + "%");
                if (Widgets.ButtonText(new Rect(row.x + 404f, row.y + 4f, 34f, 24f), "+10"))
                {
                    slot.rate = Mathf.Clamp(slot.rate + 10f, -100f, 200f);
                }

                if (slot.thing != null)
                {
                    if (Widgets.ButtonText(new Rect(row.x + 450f, row.y + 4f, 56f, 24f), "清除"))
                    {
                        slot.thing = null;
                        slot.rate = 0f;
                    }
                    if (map != null)
                    {
                        Widgets.Label(new Rect(row.x + 520f, row.y + 6f, row.width - 526f, 24f),
                            EconomyPrice.UnitPrice(slot.thing, map).ToString("0.##"));
                    }
                }

                y += RowHeight;
            }
            Widgets.EndScrollView();
        }

        // ── 价格行情（价值 = c + v + s，价格围绕价值波动）──
        /// <summary>????????????250 tick ??????</summary>
        private class PawnRow
        {
            public List<Thing> owned;            public List<Thing> carried;
            public float value;
            public float mass;
            public float free;
            public int used;
            public int cells;
            public int silver;
            public float wf;
            public float desire;
            public string plan = "";
            public Dictionary<Thing, float> prices = new Dictionary<Thing, float>();
        }

        private static readonly Dictionary<Pawn, PawnRow> privRows = new Dictionary<Pawn, PawnRow>();
        private static readonly Dictionary<Pawn, List<Thing>> privIndex = new Dictionary<Pawn, List<Thing>>();

        /// <summary>找这个人名下的第一个私有仓库建筑（用来把镜头跳过去）。</summary>
        private static Thing FirstPrivateStorage(Pawn p, Map map)
        {
            if (p == null || map == null) return null;
            try
            {
                List<SlotGroup> groups = map.haulDestinationManager.AllGroupsListForReading;
                for (int i = 0; i < groups.Count; i++)
                {
                    SlotGroup g = groups[i];
                    if (g == null || g.parent == null) continue;
                    if (EconomyPrivateStorageBehavior.OwnerOfGroup(g) != p) continue;
                    Thing holder = g.parent as Thing;
                    if (holder != null) return holder;
                }
            }
            catch { }
            return null;
        }
        private static Map privRowMap;
        private static int privRowTick = -99999;

        private static List<ThingDef> marketDefs;
        private static int marketBuiltTick = -99999;
        private static string marketFilter = "";
        // 排序：0 名称 1 库存 2 参考 3 c 4 v+s 5 稀缺 6 现价
        private static int marketSort = 5;
        private static bool marketSortAsc = false;

        /// <summary>一行算好的数据。价格表每帧只画字，不再每帧重算。</summary>
        private class MarketRow
        {
            public ThingDef def;
            public string name;
            public string stock;
            public string reference;
            public string cost;
            public string labor;
            public string scarcity;
            public string price;
        }

        private static List<MarketRow> marketRows = new List<MarketRow>();
        private static string lastFilter = "";

        /// <summary>强制下次重算（点"刷新"时用）。</summary>
        public static void RefreshMarket()
        {
            marketBuiltTick = -99999;
        }

        private static float SortValue(ThingDef def, Map map, int mode)
        {
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            switch (mode)
            {
                case 1: return comp != null ? comp.StockOf(def) : 0f;
                case 2:
                    float refStock = EconomyPrice.ReferenceStock(def, map);
                    return refStock < 0f ? 0f : refStock;   // 算不出来的排在最后（升序时值为 0）
                case 3: return EconomyValue.MaterialCost(def, map);
                case 4: return EconomyValue.LaborValue(def, map);
                case 5: return EconomyPrice.ScarcityFactor(def, map);
                case 6: return EconomyPrice.UnitPrice(def, map);
                default: return 0f;
            }
        }

        private static void DrawTradeLimitRow(Rect inRect, MapEconomyComponent comp, Map map)
        {
            if (comp == null) return;
            float y = inRect.y + 46f;
            Widgets.CheckboxLabeled(new Rect(inRect.x, y, 190f, 24f), "外部贸易限制", ref comp.tradeLimitEnabled);
            if (!comp.tradeLimitEnabled)
            {
                Widgets.Label(new Rect(inRect.x + 200f, y + 4f, 760f, 22f),
                    "已关闭：买卖一律按原版价格，不设额度。");
                return;
            }
            float x = inRect.x + 200f;
            Widgets.Label(new Rect(x, y + 4f, 60f, 22f), "阈值");
            if (Widgets.ButtonText(new Rect(x + 58f, y + 2f, 30f, 22f), "-")) comp.tradeLimitValue = Mathf.Max(100f, comp.tradeLimitValue - 500f);
            Widgets.Label(new Rect(x + 92f, y + 4f, 70f, 22f), comp.tradeLimitValue.ToString("F0") + " 银");
            if (Widgets.ButtonText(new Rect(x + 166f, y + 2f, 30f, 22f), "+")) comp.tradeLimitValue = Mathf.Min(100000f, comp.tradeLimitValue + 500f);

            x += 210f;
            Widgets.Label(new Rect(x, y + 4f, 70f, 22f), "买入 ×");
            if (Widgets.ButtonText(new Rect(x + 66f, y + 2f, 30f, 22f), "-")) comp.tradeBuyMult = Mathf.Max(1.1f, comp.tradeBuyMult - 0.25f);
            Widgets.Label(new Rect(x + 100f, y + 4f, 60f, 22f), comp.tradeBuyMult.ToString("F2"));
            if (Widgets.ButtonText(new Rect(x + 148f, y + 2f, 30f, 22f), "+")) comp.tradeBuyMult = Mathf.Min(10f, comp.tradeBuyMult + 0.25f);

            x += 200f;
            Widgets.Label(new Rect(x, y + 4f, 70f, 22f), "卖出 ×");
            if (Widgets.ButtonText(new Rect(x + 66f, y + 2f, 30f, 22f), "-")) comp.tradeSellMult = Mathf.Max(0.02f, comp.tradeSellMult - 0.05f);
            Widgets.Label(new Rect(x + 100f, y + 4f, 60f, 22f), comp.tradeSellMult.ToString("F2"));
            if (Widgets.ButtonText(new Rect(x + 148f, y + 2f, 30f, 22f), "+")) comp.tradeSellMult = Mathf.Min(1f, comp.tradeSellMult + 0.05f);
        }

        /// <summary>
        /// 总社会劳动生产率：产出价值 ÷ 投入人日，单位"银/人日"。
        /// 样本不足时显示"—"，避免开局就给出误导性的数字。
        /// </summary>
        private static string FormatProductivity(Map map)
        {
            float v = EconomyProductionStats.SocialLaborProductivity(map);
            if (v < 0f) return "—（样本不足）";
            return v.ToString("0.0") + " 银/人日";
        }

        private static void DrawMarket(Rect inRect, MapEconomyComponent comp, Map map, ref Vector2 scroll)
        {            Widgets.Label(new Rect(inRect.x, inRect.y + 2f, 860f, 22f),
                "商品价值 = c（原料）+ v+s（社会必要劳动时间 × 劳动日价值）；价格围绕价值波动 ±"
                + (EconomyPrice.ScarcityRange * 100f).ToString("0") + "%，另受通胀与恐慌加成");
            Widgets.Label(new Rect(inRect.x, inRect.y + 22f, 860f, 22f),
                EconomyInflation.Describe(map)
                + "　本期消费 " + (comp != null ? comp.consumptionBought : 0)
                + " 件　囤积浪费 " + (comp != null ? comp.consumptionWasted : 0) + " 件"
                + "　总社会劳动生产率 " + FormatProductivity(map));

            marketFilter = Widgets.TextField(new Rect(inRect.x, inRect.y + 50f, 200f, 24f), marketFilter);
            if (marketFilter != lastFilter) { lastFilter = marketFilter; RefreshMarket(); }
            if (Widgets.ButtonText(new Rect(inRect.x + 208f, inRect.y + 50f, 66f, 24f), "刷新")) RefreshMarket();
            if (Widgets.ButtonText(new Rect(inRect.x + 282f, inRect.y + 50f, 66f, 24f), "判断"))
                Find.WindowStack.Add(new Window_EconomyExplain());
            Widgets.Label(new Rect(inRect.x + 356f, inRect.y + 54f, 560f, 22f),
                "搜索物品名 / defName；参考库存 = 约 2000 银 ÷ 原版市价（按堆叠上限封顶）");

            BuildMarket(comp, map);

            float top = inRect.y + 80f;
            // 表头即排序按钮
            HeaderButton(inRect.x + 6f, top, 280f, "物品", 0);
            HeaderButton(inRect.x + 290f, top, 60f, "库存", 1);
            HeaderButton(inRect.x + 355f, top, 70f, "参考", 2);
            HeaderButton(inRect.x + 430f, top, 80f, "c（原料）", 3);
            HeaderButton(inRect.x + 515f, top, 80f, "v+s（劳动）", 4);
            HeaderButton(inRect.x + 600f, top, 70f, "稀缺", 5);
            HeaderButton(inRect.x + 675f, top, 90f, "现价", 6);

            Rect outRect = new Rect(inRect.x, top + 24f, inRect.width, inRect.yMax - top - 24f);
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, marketRows.Count * 26f + 6f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float rowY = 2f;
            for (int i = 0; i < marketRows.Count; i++)
            {
                MarketRow r = marketRows[i];
                Rect row = new Rect(4f, rowY, viewRect.width - 8f, 24f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));
                Widgets.Label(new Rect(6f, rowY + 2f, 280f, 22f), r.name);
                Widgets.Label(new Rect(290f, rowY + 2f, 60f, 22f), r.stock);
                Widgets.Label(new Rect(355f, rowY + 2f, 70f, 22f), r.reference);
                Widgets.Label(new Rect(430f, rowY + 2f, 80f, 22f), r.cost);
                Widgets.Label(new Rect(515f, rowY + 2f, 80f, 22f), r.labor);
                Widgets.Label(new Rect(600f, rowY + 2f, 70f, 22f), r.scarcity);
                Widgets.Label(new Rect(675f, rowY + 2f, 90f, 22f), r.price);
                rowY += 26f;
            }
            Widgets.EndScrollView();
        }

        private static void HeaderButton(float x, float y, float w, string label, int mode)
        {
            string text = label + (marketSort == mode ? (marketSortAsc ? " ↑" : " ↓") : "");
            if (Widgets.ButtonText(new Rect(x, y, w, 22f), text))
            {
                if (marketSort == mode) marketSortAsc = !marketSortAsc;
                else { marketSort = mode; marketSortAsc = false; }
                marketBuiltTick = -99999;
            }
        }

        private static void BuildMarket(MapEconomyComponent comp, Map map)
        {
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (marketRows != null && marketRows.Count > 0 && now - marketBuiltTick < 120) return;
            marketBuiltTick = now;
            if (marketRows == null) marketRows = new List<MarketRow>();
            marketRows.Clear();
            if (marketDefs == null) marketDefs = new List<ThingDef>();
            marketDefs.Clear();

            List<ThingDef> all = DefDatabase<ThingDef>.AllDefsListForReading;
            var scored = new List<KeyValuePair<ThingDef, float>>();
            for (int i = 0; i < all.Count; i++)
            {
                ThingDef def = all[i];
                if (def == null || def.category != ThingCategory.Item) continue;
                if (EconomyRegistry.CategoryOf(def) == null) continue;
                if (EconomyConsumption.IsPasteDef(def)) continue;   // 营养膏不进价格表
                if (EconomyValue.IsPlaceholder(def)) continue;      // 隐藏 mod 中间定义
                if (EconomyValue.Of(def, map) <= 0.001f) continue;   // 只挡真·零价值，别把便宜货也藏起来
                float dev = Mathf.Abs(EconomyPrice.ScarcityFactor(def, map) - 1f);
                scored.Add(new KeyValuePair<ThingDef, float>(def, dev));
            }
            scored.Sort((a, b) =>
            {
                if (marketSort == 0)
                {
                    int c = string.Compare(a.Key.label, b.Key.label, System.StringComparison.Ordinal);
                    return marketSortAsc ? c : -c;
                }
                float va = SortValue(a.Key, map, marketSort);
                float vb = SortValue(b.Key, map, marketSort);
                return marketSortAsc ? va.CompareTo(vb) : vb.CompareTo(va);
            });

            string f = marketFilter != null ? marketFilter.Trim() : "";
            for (int i = 0; i < scored.Count; i++)
            {
                ThingDef def = scored[i].Key;
                if (f.Length > 0
                    && def.defName.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) < 0
                    && def.label.IndexOf(f, System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                float cost = EconomyValue.MaterialCost(def, map);
                float labor = EconomyValue.LaborValue(def, map);
                float reference = EconomyPrice.ReferenceStock(def, map);
                int stock = comp != null ? comp.StockOf(def) : 0;
                float scarcity = EconomyPrice.ScarcityFactor(def, map);

                MarketRow row = new MarketRow();
                row.def = def;
                row.name = def.LabelCap;
                row.stock = stock.ToString("0");
                row.reference = reference < 0f ? "—" : reference.ToString("0");
                // 0 和"—"要分开：0 是"算出来就是零"，— 是"没有配方/没有来源，算不出来"。
                row.cost = EconomyValue.HasKnownCost(def) ? cost.ToString("0.#") : "—";
                row.labor = EconomyValue.HasKnownLabor(def) ? labor.ToString("0.#") : "—";
                string color = scarcity >= 1f ? "D05C5C" : "7CC35A";
                row.scarcity = "<color=#" + color + ">×" + scarcity.ToString("0.00") + "</color>";
                row.price = EconomyPrice.UnitPrice(def, map).ToString("0.##");
                marketRows.Add(row);
            }
        }

        private static void DrawTaxPolicy(Rect inRect, MapEconomyComponent comp, Map map, ref Vector2 scroll)
        {
            if (comp != null && comp.incomeTax != null) EconomyDoctrine.EnforceTax(map, comp.incomeTax);
            if (comp == null)
            {
                Widgets.Label(inRect, "需要在地图中使用。");
                return;
            }

            TaxPolicy p = comp.incomeTax;
            Rect outRect = inRect;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, 330f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 2f;

            Widgets.Label(new Rect(4f, y + 4f, 80f, 24f), "税制");
            if (Widgets.ButtonText(new Rect(86f, y, 84f, 26f), "累退税"))
            {
                p.mode = TaxMode.Regressive;
                p.SetModeDefaults();
            }
            if (Widgets.ButtonText(new Rect(176f, y, 84f, 26f), "比例税"))
            {
                p.mode = TaxMode.Flat;
                p.SetModeDefaults();
            }
            if (Widgets.ButtonText(new Rect(266f, y, 84f, 26f), "累进税"))
            {
                p.mode = TaxMode.Progressive;
                p.SetModeDefaults();
            }
            y += 32f;

            // 实时判断：当前权重实际构成哪种税制
            float rLow = p.RateFor(WealthTier.Low);
            float rMid = p.RateFor(WealthTier.Mid);
            float rHigh = p.RateFor(WealthTier.High);
            string actual;
            if (Mathf.Approximately(rLow, rMid) && Mathf.Approximately(rMid, rHigh)) actual = "比例型（三档同率）";
            else if (rLow < rMid && rMid < rHigh) actual = "累进型（收入越高税率越高）";
            else if (rLow > rMid && rMid > rHigh) actual = "累退型（收入越高税率越低）";
            else actual = "自定义混合（税率不单调）";
            Widgets.Label(new Rect(4f, y, viewRect.width, 24f),
                "实际类型：" + actual + "    低 " + rLow.ToString("0.#") + "% / 中 " + rMid.ToString("0.#") + "% / 高 " + rHigh.ToString("0.#") + "%");
            y += 26f;

            // ── 人头税：与收入无关，按人头收 ──
            Widgets.Checkbox(new Vector2(4f, y + 3f), ref p.pollTaxEnabled, 24f);
            Widgets.Label(new Rect(34f, y + 4f, 80f, 24f), "人头税");
            if (p.pollTaxEnabled)
            {
                if (Widgets.ButtonText(new Rect(120f, y, 34f, 26f), "-5"))
                {
                    p.pollTaxAmount = Mathf.Clamp(p.pollTaxAmount - 5f, 0f, 500f);
                }
                string pollText = p.pollTaxAmount.ToString("0");
                string pollEdited = Widgets.TextField(new Rect(158f, y, 54f, 24f), pollText);
                float pollParsed;
                if (pollEdited != pollText && float.TryParse(pollEdited, out pollParsed))
                {
                    p.pollTaxAmount = Mathf.Clamp(pollParsed, 0f, 500f);
                }
                if (Widgets.ButtonText(new Rect(216f, y, 34f, 26f), "+5"))
                {
                    p.pollTaxAmount = Mathf.Clamp(p.pollTaxAmount + 5f, 0f, 500f);
                }
                Widgets.Label(new Rect(258f, y + 4f, 460f, 24f), "每人每期固定缴纳，与收入无关；没有私银则跳过");
            }
            else
            {
                Widgets.Label(new Rect(120f, y + 4f, 600f, 24f), "已关闭：只收所得税");
            }
            y += 32f;

            Widgets.Label(new Rect(4f, y + 4f, 80f, 24f), "基准税率");
            if (Widgets.ButtonText(new Rect(86f, y, 34f, 26f), "-5"))
            {
                p.baseRate = Mathf.Clamp(p.baseRate - 5f, 0f, 100f);
            }
            string rateText = p.baseRate.ToString("0.#");
            string rateEdited = Widgets.TextField(new Rect(124f, y, 46f, 24f), rateText);
            float rateParsed;
            if (rateEdited != rateText && float.TryParse(rateEdited, out rateParsed))
            {
                p.baseRate = Mathf.Clamp(rateParsed, 0f, 100f);
            }
            Widgets.Label(new Rect(174f, y + 4f, 20f, 24f), "%");
            if (Widgets.ButtonText(new Rect(196f, y, 34f, 26f), "+5"))
            {
                p.baseRate = Mathf.Clamp(p.baseRate + 5f, 0f, 100f);
            }
            y += 32f;

            Widgets.Label(new Rect(4f, y + 4f, 80f, 24f), "分档权重");
            DrawWeightGroup(86f, y, "低收入", p, WealthTier.Low);
            DrawWeightGroup(296f, y, "中等", p, WealthTier.Mid);
            DrawWeightGroup(506f, y, "高收入", p, WealthTier.High);
            y += 34f;

            WealthDistribution d = TaxPopulation.Classify(map, p);
            TaxPopulation.ColonyStats cs = TaxPopulation.Compute(map);
            if (p.IsZero)
            {
                Widgets.Label(new Rect(4f, y, viewRect.width, 24f), "基准税率为 0：当前没有任何税收。");
                y += 26f;
            }
            else
            {
                var incomeTiers = TaxPopulation.RankTiers(map);
                var wealthTiers = TaxPopulation.RankTiersByWealth(map);
                int iLow = 0, iMid = 0, iHigh = 0, wLow = 0, wMid = 0, wHigh = 0;
                foreach (var kv in incomeTiers)
                {
                    if (kv.Value == WealthTier.Low) iLow++;
                    else if (kv.Value == WealthTier.High) iHigh++;
                    else iMid++;
                }
                foreach (var kv in wealthTiers)
                {
                    if (kv.Value == WealthTier.Low) wLow++;
                    else if (kv.Value == WealthTier.High) wHigh++;
                    else wMid++;
                }

                Widgets.Label(new Rect(4f, y, viewRect.width, 24f),
                    "收入分档（按本期收入排名三等分）：低收入 " + iLow + " 人"
                    + "    中等 " + iMid + " 人"
                    + "    高收入 " + iHigh + " 人");
                y += 24f;
                Widgets.Label(new Rect(4f, y, viewRect.width, 24f),
                    "财富分布（按私有财富排名，仅供参考）：低 " + wLow + " 人"
                    + "    中 " + wMid + " 人"
                    + "    高 " + wHigh + " 人");
                y += 24f;
                Widgets.Label(new Rect(4f, y, viewRect.width, 24f),
                    "实际税率：低收入 " + p.RateFor(WealthTier.Low).ToString("0.#") + "%"
                    + "    中等 " + p.RateFor(WealthTier.Mid).ToString("0.#") + "%"
                    + "    高收入 " + p.RateFor(WealthTier.High).ToString("0.#") + "%"
                    + "    平均 " + d.averageRate.ToString("0.#") + "%");
                y += 24f;
                Widgets.Label(new Rect(4f, y, viewRect.width, 24f),
                    "本期收入合计 " + cs.incomeTotal.ToString("N0")
                    + "    最高 " + cs.incomeMax.ToString("N0")
                    + "    最低 " + cs.incomeMin.ToString("N0")
                    + "    收入基尼 " + cs.incomeGini.ToString("0.00"));
                y += 24f;
                Widgets.Label(new Rect(4f, y, viewRect.width, 24f),
                    "累计税收 " + comp.totalTaxCollected.ToString("N0") + " 白银"
                    + "    国库现行 " + EconomyBank.ColonySilver(map).ToString("N0") + " 白银");
                y += 24f;
            }

            Widgets.EndScrollView();
        }

        private static void DrawResidents(Rect inRect, MapEconomyComponent comp, Map map, ref Vector2 scroll)
        {
            if (comp == null)
            {
                Widgets.Label(inRect, "需要在地图中使用。");
                return;
            }

            WagePolicy w = comp.wages;
            Rect outRect = inRect;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, 640f);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            float y = 2f;
            Widgets.Checkbox(new Vector2(4f, y + 3f), ref w.enabled, 24f);
            Widgets.Label(new Rect(32f, y + 4f, 90f, 24f), "每日工资");

            Widgets.Label(new Rect(130f, y + 4f, 80f, 24f), "劳动日价值");
            if (Widgets.ButtonText(new Rect(212f, y, 30f, 26f), "-5"))
            {
                w.laborDayValue = Mathf.Clamp(w.laborDayValue - 5f, 0f, 300f);
            }
            Widgets.Label(new Rect(246f, y + 4f, 46f, 24f), w.laborDayValue.ToString("0"));
            if (Widgets.ButtonText(new Rect(296f, y, 30f, 26f), "+5"))
            {
                w.laborDayValue = Mathf.Clamp(w.laborDayValue + 5f, 0f, 300f);
            }

            Widgets.Label(new Rect(350f, y + 4f, 70f, 24f), "工资比例");
            if (Widgets.ButtonText(new Rect(422f, y, 30f, 26f), "-"))
            {
                w.wageShare = Mathf.Clamp(w.wageShare - 0.05f, 0f, 1f);
            }
            Widgets.Label(new Rect(456f, y + 4f, 60f, 24f), (w.wageShare * 100f).ToString("0") + "%");
            if (Widgets.ButtonText(new Rect(520f, y, 30f, 26f), "+"))
            {
                w.wageShare = Mathf.Clamp(w.wageShare + 0.05f, 0f, 1f);
            }
            y += 34f;

            MedicalPolicy m = comp.medical;
            Widgets.Checkbox(new Vector2(4f, y + 3f), ref m.enabled, 24f);
            Widgets.Label(new Rect(32f, y + 4f, 70f, 24f), "医疗收费");
            Widgets.Label(new Rect(112f, y + 4f, 40f, 24f), "基础");
            if (Widgets.ButtonText(new Rect(154f, y, 30f, 26f), "-5"))
            {
                m.baseFee = Mathf.Clamp(m.baseFee - 5f, 0f, 300f);
            }
            Widgets.Label(new Rect(188f, y + 4f, 46f, 24f), m.baseFee.ToString("0"));
            if (Widgets.ButtonText(new Rect(238f, y, 30f, 26f), "+5"))
            {
                m.baseFee = Mathf.Clamp(m.baseFee + 5f, 0f, 300f);
            }
            Widgets.Label(new Rect(282f, y + 4f, 40f, 24f), "每级");
            if (Widgets.ButtonText(new Rect(324f, y, 30f, 26f), "-1"))
            {
                m.perSkillLevel = Mathf.Clamp(m.perSkillLevel - 1f, 0f, 50f);
            }
            Widgets.Label(new Rect(358f, y + 4f, 40f, 24f), m.perSkillLevel.ToString("0"));
            if (Widgets.ButtonText(new Rect(400f, y, 30f, 26f), "+1"))
            {
                m.perSkillLevel = Mathf.Clamp(m.perSkillLevel + 1f, 0f, 50f);
            }
            Widgets.Label(new Rect(440f, y + 4f, 300f, 24f), "病人个人白银 → 医生个人白银");
            y += 34f;

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            int totalPersonal = EconomyLedger.TotalSilver(colonists);
            float totalLabor = 0f;
            float totalDue = 0f;
            float totalProduced = 0f;
            for (int i = 0; i < colonists.Count; i++)
            {
                float lab = comp.LaborOf(colonists[i]);
                totalLabor += lab;
                totalDue += w.WageFor(lab, colonists[i], comp.RawLaborOf(colonists[i]));
                totalProduced += w.ProducedValue(lab, colonists[i]);
            }
            float surplus = totalProduced - totalDue;

            Widgets.Label(new Rect(4f, y, viewRect.width, 22f),
                "公共库存 " + EconomyBank.ColonySilver(map).ToString("N0")
                + "    个人白银 " + totalPersonal.ToString("N0")
                + "    待发工资 " + totalDue.ToString("0")
                + "    剩余价值留存 " + surplus.ToString("0"));
            Widgets.Label(new Rect(4f, y + 20f, viewRect.width, 22f),
                "累计劳动 " + (totalLabor / GenDate.TicksPerDay).ToString("0.0") + " 人日（每天自动发放一次）"
                + "    总社会劳动生产率 " + FormatProductivity(map));
            if (Widgets.ButtonText(new Rect(4f, y + 44f, 140f, 26f), "立即发放全部"))
            {
                int paid = comp.PayWagesNow();
                Messages.Message("已发放工资 " + paid + " 白银", MessageTypeDefOf.NeutralEvent, false);
            }
            y += 78f;

            Widgets.Label(new Rect(8f, y, 170f, 22f), "居民");
            Widgets.Label(new Rect(184f, y, 90f, 22f), "劳动(天)");
            Widgets.Label(new Rect(280f, y, 80f, 22f), "应发");
            Widgets.Label(new Rect(366f, y, 80f, 22f), "余额");
            y += 24f;

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                int balance = map != null ? EconomyBank.PrivateSilver(map, p) : 0;   // 实物私银，不用账本死数据
                float lab = comp.LaborOf(p);

                Rect row = new Rect(4f, y, viewRect.width - 8f, 30f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));

                Widgets.Label(new Rect(row.x + 4f, row.y + 5f, 170f, 24f), p.LabelShort);
                Widgets.Label(new Rect(row.x + 180f, row.y + 5f, 90f, 24f), (lab / GenDate.TicksPerDay).ToString("0.0"));
                Widgets.Label(new Rect(row.x + 276f, row.y + 5f, 80f, 24f), w.WageFor(lab, colonists[i], comp.RawLaborOf(colonists[i])).ToString("0"));
                Widgets.Label(new Rect(row.x + 362f, row.y + 5f, 80f, 24f), balance.ToString("N0"));

                if (Widgets.ButtonText(new Rect(row.x + 450f, row.y + 3f, 70f, 24f), "结算"))
                {
                    EconomyWages.PayOne(p, map, w);
                }
                if (balance > 0 && Widgets.ButtonText(new Rect(row.x + 528f, row.y + 3f, 70f, 24f), "征用"))
                {
                    int taken = EconomyWages.Confiscate(p, map);
                    Messages.Message("已征用 " + p.LabelShort + " 的白银 " + taken, MessageTypeDefOf.NeutralEvent, false);
                }
                y += 32f;
            }

            Widgets.EndScrollView();
        }

        private static int privateSortMode;

        private static void SortColonists(List<Pawn> colonists, Map map, Dictionary<Pawn, List<Thing>> index)
        {
            if (colonists == null) return;
            if (privateSortMode == 1)
            {
                colonists.Sort((a, b) => string.Compare(a.LabelShort, b.LabelShort, System.StringComparison.Ordinal));
                return;
            }
            if (privateSortMode == 2)
            {
                colonists.Sort((a, b) => CountOf(index, b).CompareTo(CountOf(index, a)));
                return;
            }
            colonists.Sort((a, b) =>
            {
                float va = EconomyBank.PrivateSilver(map, a) + EconomyPrivate.Value(ListOf(index, a), map);
                float vb = EconomyBank.PrivateSilver(map, b) + EconomyPrivate.Value(ListOf(index, b), map);
                return vb.CompareTo(va);
            });
        }

        private static List<Thing> ListOf(Dictionary<Pawn, List<Thing>> index, Pawn p)
        {
            List<Thing> l;
            return index != null && index.TryGetValue(p, out l) ? l : new List<Thing>();
        }

        private static int CountOf(Dictionary<Pawn, List<Thing>> index, Pawn p)
        {
            return ListOf(index, p).Count;
        }

        private static void DrawPrivate(Rect inRect, MapEconomyComponent comp, Map map, ref Vector2 scroll)
        {
            List<Pawn> colonists = new List<Pawn>(map.mapPawns.FreeColonists);
            // 全图私产索引很贵（要扫所有 Thing），只在 250 tick 的缓存块里重建；
            // 平时读上一次的缓存，否则开着窗口每帧都在扫全图。
            bool refresh = privRowMap != map
                           || Find.TickManager.TicksGame - privRowTick > 250
                           || privRowTick < 0;
            if (refresh)
            {
                Dictionary<Pawn, List<Thing>> built = EconomyPrivate.BuildIndex(map);
                privIndex.Clear();
                foreach (KeyValuePair<Pawn, List<Thing>> kv in built) privIndex[kv.Key] = kv.Value;
            }
            Dictionary<Pawn, List<Thing>> index = privIndex;
            // 排序会逐件算 UnitPrice，每帧做一次太贵；改到下面的 250 tick 缓存块里。

            int zoneCount = 0;
            foreach (Zone z in map.zoneManager.AllZones)
            {
                if (z is Zone_Stockpile) zoneCount++;
            }

            float contentHeight = 200f + zoneCount * 24f + 60f;
            for (int ci = 0; ci < colonists.Count; ci++)
            {
                PawnRow cached;
                int ownedN = 0, carriedN = 0;
                if (privRows.TryGetValue(colonists[ci], out cached) && cached != null)
                {
                    ownedN = cached.owned != null ? cached.owned.Count : 0;
                    carriedN = cached.carried != null ? cached.carried.Count : 0;
                }
                contentHeight += 28f;
                contentHeight += Mathf.Min(6, ownedN) * 22f;
                contentHeight += Mathf.Min(6, carriedN) * 22f;
            }

            Rect outRect = inRect;
            Rect viewRect = new Rect(0f, 0f, outRect.width - 18f, contentHeight);
            Widgets.BeginScrollView(outRect, ref scroll, viewRect);

            if (Widgets.ButtonText(new Rect(2f, 4f, 140f, 26f), "囤积统计"))
            {
                Find.WindowStack.Add(new Window_Hoarding());
            }
            if (Widgets.ButtonText(new Rect(148f, 4f, 140f, 26f), "欲望明细"))
            {
                Find.WindowStack.Add(new Window_Desire(map));
            }
            if (Widgets.ButtonText(new Rect(294f, 4f, 120f, 26f), "购买记录"))
            {
                Find.WindowStack.Add(new Window_PurchaseLog());
            }
            Widgets.Label(new Rect(420f, 10f, 240f, 22f),
                "在售容器 " + EconomyRealEstate.CountForSale(map) + " 个");

            if (Widgets.ButtonText(new Rect(viewRect.width - 300f, 4f, 60f, 26f), "财富"))
            {
                privateSortMode = 0;
                SortColonists(colonists, map, index);
            }
            if (Widgets.ButtonText(new Rect(viewRect.width - 236f, 4f, 60f, 26f), "姓名"))
            {
                privateSortMode = 1;
                SortColonists(colonists, map, index);
            }
            if (Widgets.ButtonText(new Rect(viewRect.width - 172f, 4f, 60f, 26f), "件数"))
            {
                privateSortMode = 2;
                SortColonists(colonists, map, index);
            }
            float y = 34f;
            Widgets.Label(new Rect(4f, y, viewRect.width, 40f),
                "私有物品可「社会化」交回集体；「征用私有」会掉心情（-12，持续 5 天）。×N = 消费财富系数（相对中位私产）；欲望 = 当前购买欲（越高越容易冲动消费，存款期间会压低）。");
            y += 44f;
            Text.Font = GameFont.Tiny;
            GUI.color = new Color(0.75f, 0.75f, 0.75f);
            Widgets.Label(new Rect(4f, y, 150f, 20f), "居民");
            Widgets.Label(new Rect(150f, y, 80f, 20f), "私银");
            Widgets.Label(new Rect(226f, y, 80f, 20f), "件数");
            Widgets.Label(new Rect(292f, y, 80f, 20f), "重量");
            Widgets.Label(new Rect(360f, y, 140f, 20f), "私有仓库");
            Widgets.Label(new Rect(500f, y, 52f, 20f), "估值");
            Widgets.Label(new Rect(548f, y, 44f, 20f), "财富×");
            Widgets.Label(new Rect(594f, y, 50f, 20f), "欲望");
            GUI.color = Color.white;
            Text.Font = GameFont.Small;
            y += 26f;

            // 私产页每帧重算"全图仓库容量 + 逐件市价"太贵，这里 250 tick 算一次，
            // 期间只读缓存；否则开着窗口就一直在扫全图的 SlotGroup。
            if (privRowMap != map || Find.TickManager.TicksGame - privRowTick > 250 || privRowTick < 0)
            {
                SortColonists(colonists, map, index);
                privRows.Clear();
                for (int ci = 0; ci < colonists.Count; ci++)
                {
                    Pawn cp = colonists[ci];
                    if (cp == null) continue;
                    PawnRow r = new PawnRow();
                    List<Thing> o;
                    if (!index.TryGetValue(cp, out o)) o = new List<Thing>();
                    r.owned = o;
                    r.carried = EconomyPrivate.CarriedPublic(cp);
                    r.value = EconomyPrivate.Value(o, map);
                    float mm = 0f;
                    for (int k = 0; k < o.Count; k++)
                    {
                        if (o[k] == null || o[k].def == null) continue;
                        mm += o[k].def.GetStatValueAbstract(StatDefOf.Mass) * o[k].stackCount;
                    }
                    r.mass = mm;
                    r.free = EconomyPrivateStorageBehavior.FreeRatio(cp, map, out r.used, out r.cells);
                    r.silver = EconomyBank.PrivateSilver(map, cp);
                    r.wf = EconomyConsumption.WealthFactor(cp, map);
                    r.desire = EconomyConsumption.DesireOf(cp, map);
                    // 提示文字和逐件现价都很贵，一起在这里算好；
                    // 绘制时只读，否则每帧每人都要重算一遍价格。
                    try { r.plan = EconomySavings.PlanText(cp, map); } catch { r.plan = ""; }
                    r.prices.Clear();
                    for (int k = 0; k < o.Count && k < 8; k++)
                    {
                        Thing t2 = o[k];
                        if (t2 == null || t2.def == null) continue;
                        r.prices[t2] = EconomyPrice.UnitPrice(t2, map);
                    }
                    privRows[cp] = r;
                }
                privRowMap = map;
                privRowTick = Find.TickManager.TicksGame;
            }

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];

                Rect row = new Rect(4f, y, viewRect.width - 8f, 28f);
                if (i % 2 == 0) Widgets.DrawBoxSolid(row, new Color(1f, 1f, 1f, 0.03f));

                PawnRow pr;
                if (!privRows.TryGetValue(p, out pr) || pr == null)
                {
                    pr = new PawnRow();
                    pr.owned = new List<Thing>();
                    pr.carried = new List<Thing>();
                }
                List<Thing> owned = pr.owned;
                List<Thing> carried = pr.carried;
                float ownedValue = pr.value;
                float mass = pr.mass;
                int cells = pr.cells;
                int used = pr.used;
                float free = pr.free;
                string storeText = cells <= 0
                    ? "无仓库"
                    : "仓库 " + used + "/" + cells + "（余 " + (free * 100f).ToString("0") + "%）";

                Widgets.Label(new Rect(row.x + 4f, row.y + 4f, 150f, 24f), p.LabelShort);
                Widgets.Label(new Rect(row.x + 150f, row.y + 4f, 80f, 24f), "私银 " + EconomyBank.PrivateSilver(map, p));
                Widgets.Label(new Rect(row.x + 226f, row.y + 4f, 80f, 24f), owned.Count + " 件");
                Widgets.Label(new Rect(row.x + 292f, row.y + 4f, 80f, 24f), mass.ToString("0.##") + " kg");
                Widgets.Label(new Rect(row.x + 360f, row.y + 4f, 170f, 24f), storeText);
                // 点一下"仓库 used/cells"就把镜头跳到他的私有仓库建筑，免得满地图找
                if (cells > 0 && Widgets.ButtonInvisible(new Rect(row.x + 360f, row.y + 4f, 170f, 24f)))
                {
                    Thing box = FirstPrivateStorage(p, map);
                    if (box != null) CameraJumper.TryJump(box);
                    else Messages.Message("找不到 " + p.LabelShort + " 的私有仓库建筑", MessageTypeDefOf.RejectInput, false);
                }
                Widgets.Label(new Rect(row.x + 500f, row.y + 4f, 52f, 24f), ownedValue.ToString("0.#"));
                float wf = pr.wf;
                Widgets.Label(new Rect(row.x + 548f, row.y + 4f, 44f, 24f), "×" + wf.ToString("0.00"));
                float desire = pr.desire;
                Color old = GUI.color;
                GUI.color = desire >= 0.65f ? new Color(1f, 0.55f, 0.45f)
                          : (desire <= 0.2f ? new Color(0.7f, 0.85f, 1f) : Color.white);
                Rect desireRect = new Rect(row.x + 594f, row.y + 4f, 50f, 24f);
                Widgets.Label(desireRect, (desire * 100f).ToString("0") + "%");
                TooltipHandler.TipRegion(desireRect, pr.plan);
                GUI.color = old;

                if (owned.Count > 0 && Widgets.ButtonText(new Rect(row.x + 648f, row.y + 2f, 84f, 24f), "征用私有"))
                {
                    int n = EconomyPrivate.Confiscate(p, map);
                    Messages.Message("已征用 " + p.LabelShort + " 的 " + n + " 件私产", MessageTypeDefOf.NeutralEvent, false);
                }
                y += 28f;

                for (int j = 0; j < owned.Count && j < 6; j++)
                {
                    Thing t = owned[j];
                    Widgets.Label(new Rect(24f, y + 1f, 270f, 22f), "· 私有 " + t.LabelNoCount + " ×" + t.stackCount);
                    float pv;
                    if (!pr.prices.TryGetValue(t, out pv)) pv = EconomyPrice.UnitPrice(t, map);
                    Widgets.Label(new Rect(300f, y + 1f, 60f, 22f), pv.ToString("0.##"));
                    if (Widgets.ButtonText(new Rect(366f, y - 1f, 70f, 22f), "社会化"))
                    {
                        EconomyPrivate.SetPrivate(t, null);
                    }
                    if (Widgets.ButtonText(new Rect(440f, y - 1f, 110f, 22f), "社会化并丢弃"))
                    {
                        EconomyPrivate.SocializeAndDrop(t, map);
                    }
                    y += 22f;
                }

                for (int j = 0; j < carried.Count && j < 6; j++)
                {
                    Thing t = carried[j];
                    Widgets.Label(new Rect(24f, y + 1f, 300f, 22f), "· 搬运中 " + t.LabelNoCount + " ×" + t.stackCount);
                    Widgets.Label(new Rect(330f, y + 1f, 80f, 22f), EconomyPrice.UnitPrice(t, map).ToString("0.##"));
                    if (Widgets.ButtonText(new Rect(420f, y - 1f, 70f, 22f), "划为私有"))
                    {
                        EconomyPrivate.SetPrivate(t, p);
                    }
                    y += 22f;
                }
            }

            // ── 存储区归属 ──
            y += 10f;
            Widgets.Label(new Rect(4f, y, viewRect.width, 22f),
                "存储区归属：私人仓库只收物主自己的东西，且只让物主本人搬运（点一下选择所有者）");
            y += 24f;

            List<Zone> stockpiles = new List<Zone>();
            foreach (Zone z in map.zoneManager.AllZones)
            {
                if (z is Zone_Stockpile) stockpiles.Add(z);
            }
            for (int i = 0; i < stockpiles.Count; i++)
            {
                Zone z = stockpiles[i];
                Pawn owner = EconomyRealEstate.OwnerOf(z);
                string zoneName = string.IsNullOrEmpty(z.label) ? "存储区" : z.label;
                string label = zoneName
                    + "   " + (owner != null ? "私有：" + owner.LabelShort : "公共")
                    + "   （点击选择）";
                if (Widgets.ButtonText(new Rect(24f, y, 300f, 22f), label))
                {
                    Zone zone = z;
                    Find.WindowStack.Add(new Window_PawnPicker(delegate(Pawn picked)
                    {
                        EconomyRealEstate.SetOwner(zone, picked);
                    }, colonists));
                }
                y += 24f;
            }
            if (stockpiles.Count == 0)
            {
                Widgets.Label(new Rect(24f, y, viewRect.width - 20f, 22f), "（这张图还没有堆放区）");
                y += 22f;
            }

            Widgets.EndScrollView();
        }

        private static void DrawWeightGroup(float x, float y, string label, TaxPolicy p, WealthTier tier)
        {
            Widgets.Label(new Rect(x, y + 4f, 60f, 24f), label);
            if (Widgets.ButtonText(new Rect(x + 58f, y, 30f, 26f), "-"))
            {
                p.SetWeight(tier, p.WeightFor(tier) - 0.5f);
            }
            string text = p.WeightFor(tier).ToString("0.0");
            string edited = Widgets.TextField(new Rect(x + 92f, y, 46f, 24f), text);
            float parsed;
            if (edited != text && float.TryParse(edited, out parsed))
            {
                p.SetWeight(tier, parsed);
            }
            if (Widgets.ButtonText(new Rect(x + 142f, y, 30f, 26f), "+"))
            {
                p.SetWeight(tier, p.WeightFor(tier) + 0.5f);
            }
        }
    }
}
