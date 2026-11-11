using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    public class DshEconomySettings : ModSettings
    {
        // 银价指数的参考存量：存量等于这个数时指数约为 1.0
        public float silverReference = 5000f;

        // 曲线强度，0 表示关闭银价指数
        public float silverStrength = 0.35f;

        // 0% 公共化时的售价 / 成本价（50% 时恒为 1.0）
        public float profitMultiplier = 1.5f;

        // 商品税栏位数量上限
        public int taxSlotCount = 4;

        // 配方工时（界面显示值，内部 ×60 变成 tick）
        public float mineWork = 100f;
        public float mine4Work = 400f;
        public float refineWork = 80f;

        // true = 按原版判定（必须有屋顶才算室内）；false = 围起来就算室内
        public bool indoorNeedsRoof;

        // 界面加减按钮的步长倍率：0.5 = 小，1 = 中，2 = 大
        public float stepScale = 1f;

        // 每几小时发一次工资（精确到小时，24 = 每天）
        public int payIntervalHours = 24;

        // 发薪时是否弹信件
        public bool sendPayLetter = true;

        // 开发者模式：显示调试按钮
        public bool debugMode;

        // 打印诊断日志（排查用；默认关闭，否则 Player.log 会被刷屏）
        public bool verboseLog;

        // 开发者：高传染（概率 ×5、心跳 15 tick）
        public bool devFastSpread;

        // 工作类型 → 工资权重（defName → 倍率）
        public Dictionary<string, float> workMult = new Dictionary<string, float>();

        // 高性能库存统计：物品生成/销毁时增量更新，全量重建降到每 2500 tick 一次
        public bool incrementalStock = true;

        // 医疗白名单：这些"医药类"物品其实是普通消费品（mod 判定），没开自我治疗也能买
        public List<string> medicalWhitelist = new List<string>();

        // 仓库补货预设："名字|defName:min:max,..."
        public List<string> restockPresets = new List<string>();

        // 外部贸易限制（全局设置，由地图组件每 250 tick 同步过去）
        public bool tradeLimitEnabled = true;
        public float tradeLimitValue = 5000f;      // 涨价线兜底值
        public float tradeLimitRatio = 0.05f;      // 涨价线 = 商人货值 × 该比例
        public float tradeBuyMult = 2f;            // 超线后买入每档翻倍
        public float tradeSellMult = 0.5f;         // 超线后卖出每档打折
        public float tradeSellRatio = 0.5f;        // 收购线 = 商人白银 × 该比例

        // 结构性短缺信的筛选（白名单语义：空的 = 全都要）
        public bool shortageVanillaOnly;

        // 衣服耐久低于这个比例就自动转成公有（0 = 关闭）
        public float apparelPublicizeAt = 0.25f;
        /// <summary>购买记录保留天数；0 = 永久。</summary>
        public float purchaseLogDays = 2f;        public List<string> shortageOnlyCats = new List<string>();
        public List<string> shortageOnlyDefs = new List<string>();

        public static float DefaultWorkMult(string defName)
        {
            switch (defName)
            {
                case "Art": return 2f;          // 艺术最高
                case "Research": return 2f;     // 研究与艺术同等
                case "Childcare": return 1.7f;  // 养育次之
                case "Crafting":
                case "Smithing":
                case "Tailoring": return 1.5f;  // 手工业
                case "Construction": return 1.2f;
                case "Mining": return 1.2f;
                case "Doctor": return 1.2f;
                case "Cooking": return 1.1f;
                default: return 1f;
            }
        }

        public float GetWorkMult(WorkTypeDef w)
        {
            if (w == null) return 1f;
            float v;
            if (workMult != null && workMult.TryGetValue(w.defName, out v)) return v;
            return DefaultWorkMult(w.defName);
        }

        public void SetWorkMult(WorkTypeDef w, float v)
        {
            if (w == null) return;
            if (workMult == null) workMult = new Dictionary<string, float>();
            if (medicalWhitelist == null) medicalWhitelist = new List<string>();
            workMult[w.defName] = Mathf.Clamp(v, 0f, 5f);
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(ref silverReference, "silverReference", 5000f);
            Scribe_Values.Look(ref silverStrength, "silverStrength", 0.35f);
            Scribe_Values.Look(ref profitMultiplier, "profitMultiplier", 1.5f);
            Scribe_Values.Look(ref taxSlotCount, "taxSlotCount", 4);
            Scribe_Values.Look(ref mineWork, "mineWork", 100f);
            Scribe_Values.Look(ref mine4Work, "mine4Work", 400f);
            Scribe_Values.Look(ref refineWork, "refineWork", 80f);
            Scribe_Values.Look(ref indoorNeedsRoof, "indoorNeedsRoof", false);
            Scribe_Values.Look(ref stepScale, "stepScale", 1f);
            Scribe_Values.Look(ref payIntervalHours, "payIntervalHours", 0);
            Scribe_Values.Look(ref sendPayLetter, "sendPayLetter", true);
            Scribe_Values.Look(ref debugMode, "debugMode", false);
            Scribe_Values.Look(ref verboseLog, "verboseLog", false);
            Scribe_Values.Look(ref devFastSpread, "devFastSpread", false);
            Scribe_Collections.Look(ref workMult, "workMult", LookMode.Value, LookMode.Value);
            if (workMult == null) workMult = new Dictionary<string, float>();
            if (medicalWhitelist == null) medicalWhitelist = new List<string>();
            Scribe_Collections.Look(ref restockPresets, "dsh_restockPresets", LookMode.Value);
            if (restockPresets == null) restockPresets = new List<string>();
            Scribe_Values.Look(ref shortageVanillaOnly, "dsh_shortageVanillaOnly", false);
            Scribe_Values.Look(ref apparelPublicizeAt, "dsh_apparelPublicizeAt", 0.25f);
            Scribe_Values.Look(ref purchaseLogDays, "dsh_purchaseLogDays", 2f);
            Scribe_Collections.Look(ref shortageOnlyCats, "dsh_shortageOnlyCats", LookMode.Value);
            if (shortageOnlyCats == null) shortageOnlyCats = new List<string>();
            Scribe_Collections.Look(ref shortageOnlyDefs, "dsh_shortageOnlyDefs", LookMode.Value);
            if (shortageOnlyDefs == null) shortageOnlyDefs = new List<string>();
            // 旧版按"天"存，这里迁移成小时
            int legacyDays = 0;
            Scribe_Values.Look(ref legacyDays, "payEveryDays", 0);
            if (payIntervalHours <= 0) payIntervalHours = legacyDays > 0 ? legacyDays * 24 : 24;
            payIntervalHours = (int)Mathf.Clamp(payIntervalHours, 1, 720);
        }
    }

    public class DshEconomyMod : Mod
    {
        public static DshEconomySettings Settings;

        private Vector2 scroll;
        private EconomyPanelMode mode = EconomyPanelMode.Categories;

        public DshEconomyMod(ModContentPack content) : base(content)
        {
            Settings = GetSettings<DshEconomySettings>();
            RestockConflict.Guard();
        }

        public override string SettingsCategory()
        {
            return "内部经济框架";
        }

        /// <summary>
        /// 调试用：按静态分类，给地图上每种物品各喂 5 条产地记录。
        /// 目的是不用真的去挖矿/种地/宰动物，也能立刻看到"统计生效后价格怎么变"。
        /// 只影响产地统计，不动任何真实物品。
        /// </summary>
        private static int SimulateProduction(Map map)
        {
            if (map == null) return 0;
            HashSet<string> seen = new HashSet<string>();
            List<Thing> things = map.listerThings.AllThings;
            int n = 0;
            for (int i = 0; i < things.Count; i++)
            {
                Thing t = things[i];
                if (t == null || t.def == null) continue;
                if (!seen.Add(t.def.defName)) continue;

                ThingDef d = t.def;
                try
                {
                    if (EconomyValue.IsMineral(d))
                        EconomyProductionStats.Note(d, EconomyProductionStats.Source.Mine, 5);
                    else if (EconomyValue.IsMeatSource(d))
                        EconomyProductionStats.Note(d, EconomyProductionStats.Source.Butcher, 5);
                    else if (EconomyValue.HasPlantSource(d))
                        EconomyProductionStats.Note(d, EconomyProductionStats.Source.Harvest, 5);
                    else if (EconomyValue.HasRecipeSource(d))
                        EconomyProductionStats.Note(d, EconomyProductionStats.Source.Recipe, 5);
                    else
                        continue;
                    n++;
                }
                catch { }
            }
            return n;
        }

        public override void WriteSettings()
        {
            base.WriteSettings();
            EconomyRecipeTuning.Apply();
        }

        /// <summary>把游戏里加载好的原版工作台贴图导出成 PNG，供美术参考。</summary>
        public void ExportVanillaWorkbenchTextures()
        {
            try
            {
                string dir = System.IO.Path.Combine(Content.RootDir, "ExportedTextures");
                System.IO.Directory.CreateDirectory(dir);

                string[] bases =
                {
                    "Things/Building/Production/TableButcher",
                    "Things/Building/Production/TableSmithingFueled",
                    "Things/Building/Production/TableMachining",
                    "Things/Building/Production/TableDrugLab",
                    "Things/Building/Production/TableSculpting",
                    "Things/Building/Production/TableBrewery",
                    "Things/Building/Production/ElectricStove",
                    "Things/Building/Production/FueledStove",
                    "Things/Building/Production/TableTailor",
                    "Things/Building/Production/TableResearch"
                };
                string[] dirs = { "north", "south", "east", "west" };

                int saved = 0;
                for (int i = 0; i < bases.Length; i++)
                {
                    for (int d = 0; d < dirs.Length; d++)
                    {
                        string path = bases[i] + "_" + dirs[d];
                        Texture2D tex = ContentFinder<Texture2D>.Get(path, false);
                        if (tex == null) continue;
                        byte[] png = tex.EncodeToPNG();
                        if (png == null) continue;
                        string file = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(path) + ".png");
                        System.IO.File.WriteAllBytes(file, png);
                        saved++;
                    }
                }

                Messages.Message("已导出 " + saved + " 张原版工作台贴图", MessageTypeDefOf.NeutralEvent, false);
                DevLog.Msg("[DSH-ECONOMY] 贴图导出完成：" + saved + " 张 → " + dir);
            }
            catch (System.Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 贴图导出失败：" + e.Message);
            }
        }

        private static void WorkField(Rect rect, ref float value)
        {
            string text = value.ToString("0");
            string edited = Widgets.TextField(rect, text);
            float parsed;
            if (edited != text && float.TryParse(edited, out parsed))
            {
                value = Mathf.Clamp(parsed, 1f, 10000f);
            }
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            Rect view = new Rect(0f, 0f, inRect.width - 20f, 1400f);
            Widgets.BeginScrollView(inRect, ref scroll, view);

            float y = view.y + 4f;

            Widgets.Label(new Rect(view.x, y, 200f, 24f), "银价参考存量");
            string refText = Settings.silverReference.ToString("0");
            string refEdited = Widgets.TextField(new Rect(view.x + 210f, y, 110f, 24f), refText);
            float refParsed;
            if (refEdited != refText && float.TryParse(refEdited, out refParsed))
            {
                Settings.silverReference = Mathf.Clamp(refParsed, 100f, 1000000f);
            }

            Widgets.Label(new Rect(view.x + 340f, y, 200f, 24f), "银价曲线强度");
            string strText = Settings.silverStrength.ToString("0.##");
            string strEdited = Widgets.TextField(new Rect(view.x + 550f, y, 80f, 24f), strText);
            float strParsed;
            if (strEdited != strText && float.TryParse(strEdited, out strParsed))
            {
                Settings.silverStrength = Mathf.Clamp(strParsed, 0f, 2f);
            }
            y += 26f;

            Widgets.Label(new Rect(view.x, y, 200f, 24f), "利润倍率");
            string proText = Settings.profitMultiplier.ToString("0.##");
            string proEdited = Widgets.TextField(new Rect(view.x + 210f, y, 110f, 24f), proText);
            float proParsed;
            if (proEdited != proText && float.TryParse(proEdited, out proParsed))
            {
                Settings.profitMultiplier = Mathf.Clamp(proParsed, 1f, 5f);
            }

            Widgets.Label(new Rect(view.x + 340f, y, 200f, 24f), "商品税栏位数量");
            string taxText = Settings.taxSlotCount.ToString();
            string taxEdited = Widgets.TextField(new Rect(view.x + 550f, y, 80f, 24f), taxText);
            int taxParsed;
            if (taxEdited != taxText && int.TryParse(taxEdited, out taxParsed))
            {
                Settings.taxSlotCount = Mathf.Clamp(taxParsed, 0, 12);
            }
            y += 26f;

            // 外部贸易限制（全局设置；地图组件每 250 tick 同步）
            Widgets.Checkbox(new Vector2(view.x, y + 3f), ref Settings.tradeLimitEnabled, 24f);
            Widgets.Label(new Rect(view.x + 30f, y + 4f, 120f, 24f), "外部贸易限制");
            Widgets.Label(new Rect(view.x + 160f, y + 4f, 40f, 24f), "阈值");
            if (Widgets.ButtonText(new Rect(view.x + 200f, y, 30f, 24f), "-"))
                Settings.tradeLimitValue = Mathf.Max(100f, Settings.tradeLimitValue - 500f);
            Widgets.Label(new Rect(view.x + 234f, y + 4f, 90f, 24f), Settings.tradeLimitValue.ToString("F0") + " 银");
            if (Widgets.ButtonText(new Rect(view.x + 328f, y, 30f, 24f), "+"))
                Settings.tradeLimitValue = Mathf.Min(100000f, Settings.tradeLimitValue + 500f);
            Widgets.Label(new Rect(view.x + 366f, y + 4f, 420f, 24f), "读不到商人货值时的兜底涨价线");
            y += 26f;

            Widgets.Label(new Rect(view.x, y + 4f, 70f, 24f), "买入 ×");
            if (Widgets.ButtonText(new Rect(view.x + 66f, y, 30f, 24f), "-"))
                Settings.tradeBuyMult = Mathf.Max(1.1f, Settings.tradeBuyMult - 0.25f);
            Widgets.Label(new Rect(view.x + 100f, y + 4f, 60f, 24f), Settings.tradeBuyMult.ToString("F2"));
            if (Widgets.ButtonText(new Rect(view.x + 148f, y, 30f, 24f), "+"))
                Settings.tradeBuyMult = Mathf.Min(10f, Settings.tradeBuyMult + 0.25f);

            Widgets.Label(new Rect(view.x + 200f, y + 4f, 70f, 24f), "卖出 ×");
            if (Widgets.ButtonText(new Rect(view.x + 266f, y, 30f, 24f), "-"))
                Settings.tradeSellMult = Mathf.Max(0.02f, Settings.tradeSellMult - 0.05f);
            Widgets.Label(new Rect(view.x + 300f, y + 4f, 60f, 24f), Settings.tradeSellMult.ToString("F2"));
            if (Widgets.ButtonText(new Rect(view.x + 348f, y, 30f, 24f), "+"))
                Settings.tradeSellMult = Mathf.Min(1f, Settings.tradeSellMult + 0.05f);

            Widgets.Label(new Rect(view.x + 400f, y + 4f, 70f, 24f), "收购线");
            if (Widgets.ButtonText(new Rect(view.x + 466f, y, 30f, 24f), "-"))
                Settings.tradeSellRatio = Mathf.Max(0.05f, Settings.tradeSellRatio - 0.05f);
            Widgets.Label(new Rect(view.x + 500f, y + 4f, 60f, 24f), (Settings.tradeSellRatio * 100f).ToString("F0") + "%");
            if (Widgets.ButtonText(new Rect(view.x + 548f, y, 30f, 24f), "+"))
                Settings.tradeSellRatio = Mathf.Min(1f, Settings.tradeSellRatio + 0.05f);
            Widgets.Label(new Rect(view.x + 584f, y + 4f, 380f, 24f), "= 商人白银 × 该比例，超出部分逐档打折");
            y += 26f;

            Widgets.Label(new Rect(view.x, y, 80f, 24f), "开采工时");
            WorkField(new Rect(view.x + 84f, y, 70f, 24f), ref Settings.mineWork);
            Widgets.Label(new Rect(view.x + 164f, y, 60f, 24f), "x4 工时");
            WorkField(new Rect(view.x + 226f, y, 70f, 24f), ref Settings.mine4Work);
            Widgets.Label(new Rect(view.x + 306f, y, 70f, 24f), "精炼工时");
            WorkField(new Rect(view.x + 380f, y, 70f, 24f), ref Settings.refineWork);
            Widgets.Label(new Rect(view.x + 460f, y, 340f, 24f), "单位是界面显示的时长，改完关窗口即生效");
            y += 26f;

            Widgets.Checkbox(new Vector2(view.x, y + 3f), ref Settings.indoorNeedsRoof, 24f);
            Widgets.Label(new Rect(view.x + 30f, y + 4f, 560f, 24f),
                "室内必须加盖屋顶（原版判定）。不勾选则「围墙围起来就算室内」");
            y += 26f;

            Widgets.Label(new Rect(view.x, y, 80f, 24f), "发薪周期");
            if (Widgets.ButtonText(new Rect(view.x + 84f, y, 34f, 24f), "-1h"))
            {
                Settings.payIntervalHours = Mathf.Clamp(Settings.payIntervalHours - 1, 1, 720);
            }
            string hoursText = Settings.payIntervalHours.ToString();
            string hoursEdited = Widgets.TextField(new Rect(view.x + 122f, y, 54f, 24f), hoursText);
            int hoursParsed;
            if (hoursEdited != hoursText && int.TryParse(hoursEdited, out hoursParsed))
            {
                Settings.payIntervalHours = Mathf.Clamp(hoursParsed, 1, 720);
            }
            Widgets.Label(new Rect(view.x + 180f, y + 2f, 40f, 24f), "小时");
            if (Widgets.ButtonText(new Rect(view.x + 224f, y, 34f, 24f), "+1h"))
            {
                Settings.payIntervalHours = Mathf.Clamp(Settings.payIntervalHours + 1, 1, 720);
            }
            if (Widgets.ButtonText(new Rect(view.x + 266f, y, 40f, 24f), "8h"))
            {
                Settings.payIntervalHours = 8;
            }
            if (Widgets.ButtonText(new Rect(view.x + 310f, y, 44f, 24f), "24h"))
            {
                Settings.payIntervalHours = 24;
            }
            if (Widgets.ButtonText(new Rect(view.x + 358f, y, 50f, 24f), "168h"))
            {
                Settings.payIntervalHours = 168;
            }
            Widgets.Label(new Rect(view.x + 416f, y + 2f, 400f, 24f), "可直接输入：24 = 每天，168 = 每周，最大 720");
            y += 26f;

            Widgets.Checkbox(new Vector2(view.x, y + 3f), ref Settings.sendPayLetter, 24f);
            Widgets.Label(new Rect(view.x + 30f, y + 4f, 560f, 24f), "发薪时弹出信件提示（关闭则只写日志）");
            y += 26f;

            Widgets.Label(new Rect(view.x, y, inRect.width, 44f),
                "银价指数 = 1 + 强度 × log10(1 + 库存白银 / 参考存量)，范围 0.5 ~ 3.0，0 = 关闭。\n"
                + "社会化：0% = 成本价 × 利润倍率，50% = 成本价（原版市场价），100% = 免费。");
            y += 50f;

            Map map = Find.CurrentMap;
            MapEconomyComponent comp = map != null ? map.GetComponent<MapEconomyComponent>() : null;

            if (Widgets.ButtonText(new Rect(view.x, y, 170f, 28f), "打开经济窗口"))
            {
                Find.WindowStack.Add(new Window_Economy());
            }
            if (Widgets.ButtonText(new Rect(view.x + 180f, y, 150f, 28f), "全部恢复默认"))
            {
                if (comp != null) comp.ResetAll();
            }
            Widgets.Label(new Rect(view.x + 344f, y + 5f, inRect.width - 344f, 24f),
                "分类、商品税、工资、私产等所有面板都在独立窗口里，这里只放全局参数。");
            y += 34f;
            Widgets.Checkbox(new Vector2(view.x, y + 3f), ref Settings.incrementalStock, 24f);
            Widgets.Label(new Rect(view.x + 30f, y + 4f, 760f, 24f),
                "高性能库存统计：物品生成/销毁时增量更新，全量重建降到每 2500 tick 一次（关掉则每 250 tick 全量扫描）");
            y += 32f;

            Widgets.Checkbox(new Vector2(view.x, y + 3f), ref Settings.shortageVanillaOnly, 24f);
            Widgets.Label(new Rect(view.x + 30f, y + 4f, 760f, 24f),
                "结构性短缺信只看原版物品（忽略其他 mod / 种族产物）");
            y += 30f;

            // 衣服磨损到一定程度自动交回集体（0 = 关闭）
            Widgets.Label(new Rect(view.x, y + 4f, 260f, 24f), "衣服耐久低于此比例自动公有");
            if (Widgets.ButtonText(new Rect(view.x + 264f, y, 30f, 24f), "-"))
                Settings.apparelPublicizeAt = Mathf.Max(0f, Settings.apparelPublicizeAt - 0.05f);
            Widgets.Label(new Rect(view.x + 298f, y + 4f, 60f, 24f),
                Settings.apparelPublicizeAt <= 0f ? "关闭" : (Settings.apparelPublicizeAt * 100f).ToString("F0") + "%");
            if (Widgets.ButtonText(new Rect(view.x + 362f, y, 30f, 24f), "+"))
                Settings.apparelPublicizeAt = Mathf.Min(1f, Settings.apparelPublicizeAt + 0.05f);
            y += 24f;
            Widgets.Label(new Rect(view.x + 30f, y + 2f, view.width - 40f, 22f),
                "穿在身上的衣服掉到这个耐久以下，自动撕掉私有标签归集体，并且不再被购买（0 = 关闭）");
            y += 28f;

            Widgets.Label(new Rect(view.x, y + 4f, 160f, 24f), "只提示勾选的分类：");
            float bx = view.x + 156f;
            float by = y;
            List<EconomyCategoryDef> shortageCats = DefDatabase<EconomyCategoryDef>.AllDefsListForReading;
            for (int ci = 0; ci < shortageCats.Count; ci++)
            {
                EconomyCategoryDef cat = shortageCats[ci];
                if (cat == null) continue;
                bool picked = Settings.shortageOnlyCats.Contains(cat.defName);
                string catLabel = string.IsNullOrEmpty(cat.label) ? cat.defName : cat.label;
                if (Widgets.ButtonText(new Rect(bx, by, 96f, 24f), picked ? "✓ " + catLabel : catLabel))
                {
                    if (picked) Settings.shortageOnlyCats.Remove(cat.defName);
                    else Settings.shortageOnlyCats.Add(cat.defName);
                }
                bx += 102f;
                if (bx + 96f > inRect.xMax - 20f)
                {
                    bx = view.x + 156f;
                    by += 28f;
                }
            }
            y = by + 30f;
            if (Widgets.ButtonText(new Rect(view.x, y, 140f, 26f), "立即发一次预警"))
            {
                EconomyShortageWatch.ForceSend(map);
            }
            Widgets.Label(new Rect(view.x + 148f, y + 4f, 700f, 24f),
                "不等 4 天、不受 3 天冷却限制，按当前库存直接发一封，用来检查筛选效果。");
            y += 32f;

            Widgets.Checkbox(new Vector2(view.x, y + 3f), ref Settings.debugMode, 24f);
            Widgets.Label(new Rect(view.x + 30f, y + 4f, 620f, 24f),
                "开发者模式：显示测试按钮。会给存档加钱/清数据，正式游玩请关掉。");
            if (Settings.debugMode)
            {
                Widgets.Checkbox(new Vector2(view.x + 620f, y + 3f), ref Settings.devFastSpread, 24f);
                Widgets.Label(new Rect(view.x + 650f, y + 4f, 300f, 24f), "高传染（概率×5、1 秒心跳）");
            }
            y += 30f;

            Widgets.Checkbox(new Vector2(view.x, y + 3f), ref Settings.verboseLog, 24f);
            Widgets.Label(new Rect(view.x + 30f, y + 4f, 900f, 24f),
                "打印诊断日志：排查问题时才勾，平时勾着会把 Player.log 刷满。");
            y += 30f;

            // 购买记录
            Widgets.Label(new Rect(view.x, y + 4f, 240f, 24f), "购买记录保留");
            if (Widgets.ButtonText(new Rect(view.x + 244f, y, 34f, 26f), "-"))
            {
                Settings.purchaseLogDays = Mathf.Max(0f, Settings.purchaseLogDays - 1f);
            }
            Widgets.Label(new Rect(view.x + 282f, y + 4f, 80f, 24f),
                Settings.purchaseLogDays <= 0f ? "永久" : Settings.purchaseLogDays.ToString("0") + " 天");
            if (Widgets.ButtonText(new Rect(view.x + 362f, y, 34f, 26f), "+"))
            {
                Settings.purchaseLogDays = Mathf.Min(60f, Settings.purchaseLogDays + 1f);
            }
            if (Widgets.ButtonText(new Rect(view.x + 404f, y, 120f, 26f), "查看购买记录"))
            {
                Find.WindowStack.Add(new Window_PurchaseLog());
            }
            Widgets.Label(new Rect(view.x + 532f, y + 4f, 900f, 24f),
                "超过这个天数的记录会自动清掉（0 = 永久保留）。");
            y += 32f;

            if (Settings.debugMode && map != null && comp != null)
            {
                List<Pawn> colonists = map.mapPawns.FreeColonists;

                if (Widgets.ButtonText(new Rect(view.x, y, 140f, 26f), "国库 +1000 银"))
                {
                    EconomyBank.GiveColonySilver(map, 1000, map.Center);
                }
                if (Widgets.ButtonText(new Rect(view.x + 148f, y, 140f, 26f), "全体 +100 私银"))
                {
                    for (int i = 0; i < colonists.Count; i++)
                    {
                        EconomyBank.SplitSilverToPrivate(map, colonists[i], 100);
                    }
                }
                if (Widgets.ButtonText(new Rect(view.x + 296f, y, 140f, 26f), "清空私银"))
                {
                    List<Thing> stacks = EconomyBank.AllSilverStacks(map);
                    for (int i = 0; i < stacks.Count; i++)
                    {
                        if (EconomyPrivate.OwnerOf(stacks[i]) != null) EconomyPrivate.SetPrivate(stacks[i], null);
                    }
                }
                y += 30f;

                if (Widgets.ButtonText(new Rect(view.x, y, 140f, 26f), "清零劳动/满意度"))
                {
                    EconomyLedger.ResetAll();
                }
                if (Widgets.ButtonText(new Rect(view.x + 148f, y, 140f, 26f), "立即发薪"))
                {
                    int paid = comp.PayWagesNow();
                    Messages.Message("强制发薪 " + paid + " 白银", MessageTypeDefOf.NeutralEvent, false);
                }
                if (Widgets.ButtonText(new Rect(view.x + 296f, y, 140f, 26f), "打印状态"))
                {
                    Log.Message("[DSH-ECONOMY] 调试：国库=" + EconomyBank.ColonySilver(map)
                        + " 劳动=" + (EconomyLedger.TotalLabor(map.mapPawns.FreeColonists) / GenDate.TicksPerDay).ToString("0.00") + " 人日"
                        + " 个股民=" + map.mapPawns.FreeColonists.Count);
                }
                y += 30f;

                // 产地统计的调试按钮：不用真的去挖矿/种地，也能立刻看到统计生效后的价格
                if (Widgets.ButtonText(new Rect(view.x, y, 140f, 26f), "产地统计·打印"))
                {
                    Log.Message("[DSH-ECONOMY] 产地统计：共 " + EconomyProductionStats.records.Count + " 条记录");
                    int shown = 0;
                    for (int i = 0; i < EconomyProductionStats.records.Count && shown < 40; i++)
                    {
                        EconomyProductionStats.Record r = EconomyProductionStats.records[i];
                        if (r == null) continue;
                        Log.Message("[DSH-ECONOMY]   产地 " + r.defName + "：合成 " + r.recipe
                            + " / 挖掘 " + r.mine + " / 收割 " + r.harvest
                            + " / 屠宰 " + r.butcher + " / 产出 " + r.gather);
                        shown++;
                    }
                }
                if (Widgets.ButtonText(new Rect(view.x + 148f, y, 140f, 26f), "产地统计·模拟 +5"))
                {
                    int n = SimulateProduction(map);
                    Messages.Message("已给 " + n + " 种物品各喂 5 条产地记录", MessageTypeDefOf.NeutralEvent, false);
                }
                if (Widgets.ButtonText(new Rect(view.x + 296f, y, 140f, 26f), "产地统计·清空"))
                {
                    EconomyProductionStats.Reset();
                    Messages.Message("产地统计已清空", MessageTypeDefOf.NeutralEvent, false);
                }
                if (Widgets.ButtonText(new Rect(view.x + 444f, y, 140f, 26f), "食物诊断"))
                {
                    EconomyValue.DumpFoodBranch();
                    Messages.Message("已打印到 Player.log（搜 [FOOD]）", MessageTypeDefOf.NeutralEvent, false);
                }
                if (Widgets.ButtonText(new Rect(view.x + 592f, y, 140f, 26f), "银矿体检"))
                {
                    DshEconomyDebug.CheckSilverMine();
                    Messages.Message("已打印到 Player.log（搜 [MINE]）", MessageTypeDefOf.NeutralEvent, false);
                }
                if (Widgets.ButtonText(new Rect(view.x + 738f, y, 140f, 26f), "劳动体检"))
                {
                    DshEconomyDebug.DumpLabor();
                }
                y += 30f;

                if (Widgets.ButtonText(new Rect(view.x, y, 140f, 26f), "征收所得税"))
                {
                    int taxed = EconomyTax.CollectIncomeTax(map, comp);
                    Messages.Message("已征收所得税 " + taxed + " 白银", MessageTypeDefOf.NeutralEvent, false);
                }
                if (Widgets.ButtonText(new Rect(view.x + 148f, y, 180f, 26f), "导出原版工作台贴图"))
                {
                    ExportVanillaWorkbenchTextures();
                }
                Widgets.Label(new Rect(view.x + 336f, y + 5f, 500f, 24f),
                    "贴图导出到 Mod 目录的 ExportedTextures 下");
                y += 30f;

                if (Widgets.ButtonText(new Rect(view.x, y, 180f, 26f), "测试：发大额工资"))
                {
                    List<Pawn> all = map.mapPawns.FreeColonists;
                    for (int i = 0; i < all.Count; i++)
                    {
                        EconomyLedger.AddLabor(all[i], GenDate.TicksPerDay * 5f);
                    }
                    int paid = comp.PayWagesNow();
                    Messages.Message("已发放工资 " + paid + " 白银（含税），查看日志与信件", MessageTypeDefOf.NeutralEvent, false);
                }
                Widgets.Label(new Rect(view.x + 188f, y + 5f, 620f, 24f),
                    "给每人塞 5 人日劳动，立刻结算工资、奖金、学费、所得税");
                y += 30f;

                if (Widgets.ButtonText(new Rect(view.x, y, 180f, 26f), "测试：收入对半"))
                {
                    DevLog.Msg("[DSH-ECONOMY] 按钮触发：收入对半");
                    List<Pawn> shuffled = new List<Pawn>(map.mapPawns.FreeColonists);
                    shuffled.SortBy(p => Rand.Value);

                    int half = shuffled.Count / 2;
                    for (int i = 0; i < shuffled.Count; i++)
                    {
                        float days = i < half ? 8f : 1f;
                        EconomyLedger.AddLabor(shuffled[i], GenDate.TicksPerDay * days);
                        DevLog.Msg("[DSH-ECONOMY] 测试加劳动：" + shuffled[i].LabelShort + " +" + days + " 人日");
                    }
                    int paid2 = comp.PayWagesNow();
                    Messages.Message("收入差距已制造：" + half + " 人高收入、" + (shuffled.Count - half)
                        + " 人低收入，工资支出 " + paid2 + " 白银", MessageTypeDefOf.NeutralEvent, false);
                }
                Widgets.Label(new Rect(view.x + 188f, y + 5f, 620f, 24f),
                    "随机一半 8 人日、另一半 1 人日，再整体结算，观察累进/累退的差别");
                y += 30f;

                if (Widgets.ButtonText(new Rect(view.x, y, 120f, 26f), "状态：罢工"))
                {
                    int n = EconomyUnrest.ForceStrike(map, 15f);
                    Messages.Message("已让 " + n + " 人罢工（满意度 15）", MessageTypeDefOf.NeutralEvent, false);
                }
                if (Widgets.ButtonText(new Rect(view.x + 128f, y, 120f, 26f), "状态：暴动"))
                {
                    int n = EconomyUnrest.ForceRiot(map, 8f);
                    Messages.Message("已让 " + n + " 人暴动（满意度 8）", MessageTypeDefOf.NeutralEvent, false);
                }
                if (Widgets.ButtonText(new Rect(view.x + 256f, y, 120f, 26f), "状态：革命"))
                {
                    int n = EconomyUnrest.ForceRevolution(map, 2f);
                    Messages.Message("已让 " + n + " 人革命（满意度 2）", MessageTypeDefOf.NeutralEvent, false);
                }
                Widgets.Label(new Rect(view.x + 384f, y + 5f, 460f, 24f),
                    "罢工=停工；暴动/革命=进入狂暴（会攻击、破坏），革命传染最强");
                y += 30f;
            }
        
            Widgets.EndScrollView();
        }
    }
}




