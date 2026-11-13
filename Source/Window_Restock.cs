using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>仓库补货设置窗口：默认补满空格 / 细化到每种物品的上下限 / 预设。</summary>
    public class Window_Restock : Window
    {
        private readonly Building_Storage storage;
        private readonly RestockSettings comp;
        private Vector2 scroll;
        private string presetName = "预设1";
        private int presetIndex = -1;
        private string rename = string.Empty;
        private ThingDef renamingDef;
        private string search = "";
        private bool onlyRules;
        private bool onlyColony;

        public Window_Restock(Building_Storage storage)
        {
            this.storage = storage;
            comp = storage != null ? RestockStore.For(storage) : null;
            try
            {
                int cells = 0;
                try { cells = storage.AllSlotCellsList().Count; } catch { }
                int per = (storage.def != null && storage.def.building != null) ? storage.def.building.maxItemsInCell : 1;
                DevLog.Msg("[RESTOCK] " + storage.LabelShort + " 格=" + cells + " 每格=" + per
                    + " 栏=" + RestockStore.TotalSlots(storage)
                    + " 白银每组=" + ThingDefOf.Silver.stackLimit);
            }
            catch { }
            doCloseX = true;
            closeOnCancel = true;
            absorbInputAroundWindow = true;
            draggable = true;
            resizeable = false;
        }

        public override Vector2 InitialSize
        {
            get { return new Vector2(720f, 640f); }
        }

        public override void DoWindowContents(Rect inRect)
        {
            if (comp == null)
            {
                Widgets.Label(inRect, "这个建筑没有补货组件。");
                return;
            }

            float y = inRect.y;
            Widgets.Label(new Rect(inRect.x, y, inRect.width, 26f),
                (storage.LabelCap + "   （" + storage.AllSlotCellsList().Count + " 格）").Truncate(inRect.width));
            y += 28f;

            Widgets.Label(new Rect(inRect.x, y, inRect.width, 40f),
                "默认：把仓库允许的物品补满空格。\n勾选后按「占仓库栏位的百分比」分配，总和 100%，加一条会自动压缩其它条。");
            y += 44f;

            bool detailed = comp.detailed;
            Widgets.CheckboxLabeled(new Rect(inRect.x, y, 260f, 26f), "细化运输设置", ref detailed);
            if (detailed != comp.detailed)
            {
                comp.detailed = detailed;
                if (detailed && comp.rules.Count == 0) SeedFromStock();
            }
            y += 30f;

            // 与智能补货 mod 的冲突提示
            if (RestockConflict.SmartRestockActive())
            {
                GUI.color = new Color(1f, 0.6f, 0.6f);
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 24f),
                    "⚠ 检测到「Storage Refill Hysteresis（存储补货滞后）」：两个补货系统会互相抢活，建议只开一个。");
                GUI.color = Color.white;
                y += 26f;
            }

            Rect listRect = new Rect(inRect.x, y, inRect.width, inRect.height - y - 112f);
            if (comp.detailed)
            {
                DrawRules(listRect);
            }
            else
            {
                Widgets.DrawMenuSection(listRect);
                Widgets.Label(listRect.ContractedBy(10f),
                    "当前是默认模式：\n\n"
                    + "· 仓库里每个空格都会尽量补满，物品来自殖民地其它地方的公共库存。\n"
                    + "· 不区分种类——只要仓库过滤器允许、别处有货，就会往里搬。\n"
                    + "· 每个仓库每次最多补 2 堆，不会一次叫走所有搬运工。\n\n"
                    + "想要「某样东西低于 20 件就补到 60 件」这种，勾上上面的细化开关。");
            }

            // 底部按钮（两行：上排预设，下排操作，全部按窗口宽度排版，不会超出）
            float by = inRect.yMax - 32f;
            float gap = 6f;
            float w1 = 130f, w2 = 110f, w3 = 130f, w4 = 70f;
            Rect b1 = new Rect(inRect.x, by, w1, 28f);
            Rect b2 = new Rect(b1.xMax + gap, by, w2, 28f);
            Rect b3 = new Rect(b2.xMax + gap, by, w3, 28f);
            Rect b4 = new Rect(Mathf.Max(b3.xMax + gap, inRect.xMax - w4), by, w4, 28f);

            if (Widgets.ButtonText(b1, "按当前库存生成"))
            {
                SeedFromStock();
                comp.detailed = true;
            }
            if (Widgets.ButtonText(b2, "清空规则"))
            {
                comp.rules.Clear();
            }
            if (Widgets.ButtonText(b3, "保存为预设"))
            {
                RestockPresets.Save(presetName, comp.rules);
                Messages.Message("已保存补货预设：" + presetName, MessageTypeDefOf.TaskCompletion, false);
            }
            if (Widgets.ButtonText(b4, "关闭")) Close();

            Rect b5 = new Rect(b4.x - 76f, by, 70f, 28f);
            if (Widgets.ButtonText(b5, "诊断"))
            {
                EconomyRestock.Diagnose(storage != null ? storage.Map : Find.CurrentMap, storage);
                Messages.Message("补货诊断已写入日志（搜 [RESTOCK]）", MessageTypeDefOf.TaskCompletion, false);
            }

            // 预设行（单独一行；宽度扣掉右下角「关闭」，永不重叠）
            Rect pr = new Rect(inRect.x, by - 34f, inRect.width - 80f, 28f);
            DrawPresetRow(pr);
        }

        private void DrawPresetRow(Rect r)
        {
            List<string> list = RestockPresets.List;
            Rect nameRect = new Rect(r.x, r.y, Mathf.Min(200f, r.width - 260f), r.height);
            presetName = Widgets.TextField(nameRect, presetName ?? string.Empty);

            Rect left = new Rect(nameRect.xMax + 6f, r.y, 30f, r.height);
            Rect mid = new Rect(left.xMax + 4f, r.y, 160f, r.height);
            Rect right = new Rect(mid.xMax + 4f, r.y, 30f, r.height);
            if (Widgets.ButtonText(left, "<")) presetIndex--;
            if (Widgets.ButtonText(right, ">")) presetIndex++;
            if (list.Count == 0) presetIndex = -1;
            else presetIndex = Mathf.Clamp(presetIndex, 0, list.Count - 1);

            string label = presetIndex >= 0 && presetIndex < list.Count ? RestockPresets.NameOf(list[presetIndex]) : "无预设";
            if (Widgets.ButtonText(mid, label.Truncate(76f)))
            {
                if (presetIndex >= 0 && presetIndex < list.Count)
                {
                    RestockPresets.Apply(list[presetIndex], comp);
                    Messages.Message("已应用预设：" + RestockPresets.NameOf(list[presetIndex]), MessageTypeDefOf.TaskCompletion, false);
                }
            }
        }

        private void DrawRules(Rect outer)
        {
            Widgets.DrawMenuSection(outer);
            Rect inner = outer.ContractedBy(4f);

            // 顶部：搜索框 + 只显示已设置
            // 两行：第一行搜索/清除/只看殖民地有的，第二行统计（以前统计和勾选框挤在同一行，会叠字）
            Rect bar = new Rect(inner.x, inner.y, inner.width, 54f);
            Widgets.Label(new Rect(bar.x, bar.y + 4f, 50f, 24f), "搜索");
            search = Widgets.TextField(new Rect(bar.x + 46f, bar.y + 2f, 220f, 24f), search ?? string.Empty);
            // 搜索框右边的"清除"：一键回到不搜索的状态
            if ((search ?? string.Empty).Length > 0
                && Widgets.ButtonText(new Rect(bar.x + 270f, bar.y + 2f, 52f, 24f), "清除"))
                search = string.Empty;

            // 勾上它就是一键"只列殖民地现成有的东西"，不用先搜索
            bool oc = onlyColony;
            Widgets.CheckboxLabeled(new Rect(bar.x + 330f, bar.y + 2f, 150f, 24f), "只看殖民地有的", ref oc);
            onlyColony = oc;


            List<ThingDef> all;
            List<ThingDef> shown;
            try
            {
                all = AllowedDefs();
                shown = Filtered(all);
            }
            catch (System.Exception e)
            {
                // 以前这里一旦抛异常，整个列表区就是空白，什么提示都没有。
                // 现在至少把原因打出来，并且不让窗口瘫掉。
                Log.Error("[DSH-ECONOMY] 补货列表构建失败：" + e.GetType().Name + " " + e.Message
                    + "\n" + e.StackTrace);
                Widgets.Label(new Rect(inner.x, inner.y + 34f, inner.width, 60f),
                    "列表构建失败：" + e.Message + "\n（详细信息见 Player.log）");
                return;
            }
            Widgets.Label(new Rect(bar.x, bar.y + 30f, inner.width - 8f, 22f),
                ("允许 " + all.Count + " 种 · 显示 " + shown.Count + " · 已占 " + Mathf.RoundToInt(RestockStore.TotalPct(comp)) + "%"));

            // 列表区高度必须按"顶栏实际高度"算：顶栏从 28 加到 54（多了一行统计），
            // 这里以前写死 inner.height - 30f，结果底部 24 像素漏出框外，最后一行（乌鸦尸体）画在边框下面。
            float bodyTop = bar.yMax + 2f;
            Rect body = new Rect(inner.x, bodyTop, inner.width, Mathf.Max(60f, inner.yMax - bodyTop - 4f));
            const float rowH = 46f;
            float total = shown.Count * rowH + 40f;
            Rect view = new Rect(0f, 0f, body.width - 20f, Mathf.Max(total, body.height));
            Widgets.BeginScrollView(body, ref scroll, view);

            float y = 0f;
            for (int i = 0; i < shown.Count; i++)
            {
                ThingDef def = shown[i];
                if (def == null) continue;
                RestockRule rule = comp.rules.Find(r => r != null && r.def == def);
                Rect row = new Rect(0f, y, view.width, rowH - 2f);
                if (i % 2 == 0) Widgets.DrawAltRect(row);

                Rect icon = new Rect(row.x + 28f, row.y + 4f, 26f, 26f);
                Widgets.ThingIcon(icon, def);
                // 名字列收窄到 150：以前 190 宽会压到后面的「占比」上（长名字穿模）
                Widgets.Label(new Rect(icon.xMax + 6f, row.y + 7f, 150f, 24f), def.LabelCap.Truncate(140f));

                // 勾选框：勾上=这条规则启用（可以调占比/件数），取消=删掉这条规则
                bool hadRule = rule != null;
                bool on = hadRule;
                Widgets.Checkbox(new Vector2(row.x + 2f, row.y + 5f), ref on);
                if (on != hadRule)
                {
                    if (on)
                    {
                        rule = new RestockRule { def = def, pct = 10f };
                        comp.rules.Add(rule);
                        comp.detailed = true;
                    }
                    else
                    {
                        comp.rules.Remove(rule);
                        rule = null;
                    }
                    // Normalize 要读 changed.pct，传 null 会空引用 —— 删规则时不用调它
                    if (rule != null) RestockStore.Normalize(comp, rule);
                }

                if (rule == null)
                {
                    GUI.color = new Color(1f, 1f, 1f, 0.55f);
                    Widgets.Label(new Rect(icon.xMax + 6f + 160f, row.y + 7f, 320f, 24f), "未勾选（勾上才能调占比/件数）");
                    GUI.color = Color.white;
                    // 必须自己把 y 往前推：continue 会跳过循环末尾的 y += rowH，
                    // 结果后面所有行都画在同一行上（文字叠成一坨）。
                    y += rowH;
                    continue;
                }

                // 容量统一按"件"算：
                // 有内部容器的存储（存储 mod 的大箱子）容量本身就是件数；
                // 纯格位存储是 格数 × 每格堆数 × 堆叠上限。
                int capItems = RestockStore.CapacityItems(storage, def);
                int stackLimit = RestockStore.ItemsPerSlot(storage, def);
                int pct = rule != null ? Mathf.RoundToInt(rule.pct) : 10;
                int newPct = pct;
                int exact = rule != null ? rule.exactItems : 0;

                // 右对齐：× 在 row 最右，件数输入在它左边
                float delX = row.xMax - 26f;
                float itemsBoxX = delX - 112f;
                float itemsLblX = itemsBoxX - 22f;

                float x = row.x + 212f;
                Widgets.Label(new Rect(x, row.y + 13f, 34f, 24f), "占比");
                x += 34f;
                float pctW = Mathf.Clamp(itemsLblX - 150f - x, 120f, 150f);
                x = PctStepper(new Rect(x, row.y + 3f, pctW, 28f), ref newPct, def.defName);

                int allotItems = exact > 0
                    ? exact
                    : Mathf.Max(0, Mathf.RoundToInt(capItems * Mathf.Clamp(newPct, 0, 100) / 100f));
                // 两行显示，不用悬停也能看懂：上行件数，下行组数与槽位
                int slots = ContainerUtil.CapacitySlots(storage);
                int targetSlots = RestockStore.SlotsFor(storage, newPct);
                int capGroups = stackLimit > 0 ? Mathf.Max(1, Mathf.CeilToInt((float)capItems / stackLimit)) : 0;
                int targetGroups = stackLimit > 0 ? Mathf.Max(0, Mathf.CeilToInt((float)allotItems / stackLimit)) : 0;
                float infoW = Mathf.Max(80f, itemsLblX - x - 10f);
                Rect line1 = new Rect(x + 8f, row.y + 3f, infoW, 22f);
                Rect line2 = new Rect(x + 8f, row.y + 23f, infoW, 20f);
                Widgets.Label(line1, ("目标 " + allotItems + " 件 · 上限 " + capItems).Truncate(infoW));
                GUI.color = new Color(1f, 1f, 1f, 0.65f);
                Widgets.Label(line2, ("≈ " + targetGroups + "/" + capGroups + " 组 · " + targetSlots + "/" + slots + " 槽").Truncate(infoW));
                GUI.color = Color.white;
                TooltipHandler.TipRegion(new Rect(line1.x, line1.y, infoW, 40f),
                    "仓库容量：" + slots + " 槽 ≈ " + capGroups + " 组（每组 " + stackLimit + " 件）\n"
                    + "当前占比：" + newPct + "% → " + targetSlots + " 槽 = 目标 " + allotItems + " 件");

                int newItems = allotItems;
                Widgets.Label(new Rect(itemsLblX, row.y + 7f, 22f, 24f), "件");
                newItems = Mathf.Clamp(newItems, 0, capItems);
                Stepper(new Rect(itemsBoxX, row.y + 3f, 108f, 28f), ref newItems, capItems, def.defName);

                if (newPct != pct || newItems != allotItems)
                {
                    if (rule == null)
                    {
                        rule = new RestockRule { def = def };
                        comp.rules.Add(rule);
                    }
                    if (newPct != pct)
                    {
                        // 改占比：清掉精确件数，回到按占比算
                        rule.exactItems = 0;
                        rule.pct = newPct;
                        RestockStore.Normalize(comp, rule);
                    }
                    else
                    {
                        // 改件数：记住精确值，不再被占比四舍五入覆盖
                        rule.exactItems = Mathf.Max(0, newItems);
                        rule.pct = capItems > 0 ? 100f * newItems / capItems : 0f;
                    }
                    comp.detailed = true;
                }

                if (rule != null)
                {
                    Rect del = new Rect(row.xMax - 28f, row.y + 4f, 24f, 24f);
                    if (Widgets.ButtonText(del, "×"))
                    {
                        comp.rules.Remove(rule);
                        break;
                    }
                }
                y += rowH;
            }

            Widgets.EndScrollView();
        }

        private List<ThingDef> cachedDefs;

        /// <summary>仓库过滤器允许的全部物品（按名字排序，缓存一次）。</summary>
        private List<ThingDef> AllowedDefs()
        {
            if (cachedDefs != null) return cachedDefs;
            cachedDefs = new List<ThingDef>();
            try
            {
                StorageSettings s = storage.GetStoreSettings();
                if (s != null && s.filter != null)
                {
                    foreach (ThingDef d in s.filter.AllowedThingDefs)
                    {
                        if (d == null || !d.PlayerAcquirable) continue;
                        cachedDefs.Add(d);
                    }
                }
            }
            catch { }

            // 存储 mod 的容器有时会把真正的过滤器放在 SlotGroup.Settings 上，
            // 或者干脆返回一个"全禁"的默认过滤器；这时读出来是 0 种，搜索就永远是空的。
            // 兜底：读不到就退回 SlotGroup.Settings；还是 0，就列出全部可获取物品（不影响实际存储判定）。
            if (cachedDefs.Count == 0)
            {
                try
                {
                    SlotGroup g = storage.GetSlotGroup();
                    StorageSettings gs = g != null ? g.Settings : null;
                    if (gs != null && gs.filter != null)
                    {
                        foreach (ThingDef d in gs.filter.AllowedThingDefs)
                        {
                            if (d == null || !d.PlayerAcquirable) continue;
                            cachedDefs.Add(d);
                        }
                    }
                }
                catch { }
            }
            if (cachedDefs.Count == 0)
            {
                foreach (ThingDef d in DefDatabase<ThingDef>.AllDefs)
                {
                    if (d == null || !d.PlayerAcquirable || d.category != ThingCategory.Item) continue;
                    cachedDefs.Add(d);
                }
                DevLog.Msg("[RESTOCK] " + storage.LabelShort + " 读不到过滤器允许项（0 种），已退回全部物品列表：" + cachedDefs.Count + " 种");
            }

            cachedDefs.Sort((a, b) => string.Compare(a?.label ?? a?.defName ?? "", b?.label ?? b?.defName ?? "", System.StringComparison.Ordinal));
            return cachedDefs;
        }

        // ── 只看殖民地有的 ───────────────────────────────────────────────
        // 每 250 tick 重建一次"地图上现在有哪些 def"（含殖民者背包），不每帧扫。
        private static readonly HashSet<ThingDef> colonyDefs = new HashSet<ThingDef>();
        private static int colonyTick = -99999;
        private static Map colonyMap;

        private static bool ColonyHas(ThingDef d)
        {
            if (d == null) return false;
            Map map = Find.CurrentMap;
            if (map == null) return true;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (map != colonyMap || now - colonyTick > 250)
            {
                colonyMap = map;
                colonyTick = now;
                colonyDefs.Clear();
                try
                {
                    List<Thing> all = map.listerThings.AllThings;
                    for (int i = 0; i < all.Count; i++)
                        if (all[i] != null && all[i].def != null) colonyDefs.Add(all[i].def);
                }
                catch { }
                try
                {
                    List<Pawn> ps = map.mapPawns.FreeColonistsSpawned;
                    for (int i = 0; i < ps.Count; i++)
                    {
                        Pawn p = ps[i];
                        if (p == null || p.inventory == null || p.inventory.innerContainer == null) continue;
                        foreach (Thing t in p.inventory.innerContainer)
                            if (t != null && t.def != null) colonyDefs.Add(t.def);
                    }
                }
                catch { }
            }
            return colonyDefs.Contains(d);
        }
        private List<ThingDef> Filtered(List<ThingDef> all)
        {
            string q = (search ?? string.Empty).Trim();
            List<ThingDef> list = new List<ThingDef>();
            for (int i = 0; i < all.Count; i++)
            {
                if (list.Count >= 300) break;
                ThingDef d = all[i];
                if (d == null) continue;
                RestockRule rule = comp.rules.Find(r => r != null && r.def == d);

                if (onlyColony && !ColonyHas(d)) continue;

                // 没搜索、也没勾「只看已设置」时，只显示已设规则的（否则几千行会卡）；
                // 但勾了「只看殖民地有的」就该一键全列出来，不然等于还得再搜一次。
                if (q.Length == 0 && !onlyColony && rule == null) continue;
                if (q.Length > 0)
                {
                    // label / defName 都可能是 null（mod 里没填），直接 IndexOf 会抛空引用，
                    // 整个列表就变空白了。这里先兜成空串。
                    string lbl = d.label ?? string.Empty;
                    string dn = d.defName ?? string.Empty;
                    if (lbl.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) < 0
                        && dn.IndexOf(q, System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                }
                // 已设规则的排前面
                if (rule != null) list.Insert(0, d);
                else list.Add(d);
            }
            return list;
        }

        /// <summary>一个 -/数字/- 的小控件，返回右侧 x。按一下 1 件；按住 shift 一次 10 件。</summary>
        private static readonly Dictionary<string, string> editBuf = new Dictionary<string, string>();

        /// <summary>带缓冲的数字输入：只在失去焦点时提交，避免逐帧解析把数字写到别的物品上。</summary>
        private int IntField(Rect r, string key, int value, int min, int max)
        {
            string cur;
            if (!editBuf.TryGetValue(key, out cur)) cur = value.ToString();
            GUI.SetNextControlName(key);
            string edited = Widgets.TextField(r, cur);
            if (edited != cur) editBuf[key] = edited;
            if (GUI.GetNameOfFocusedControl() != key)
            {
                string pending;
                if (editBuf.TryGetValue(key, out pending))
                {
                    pending = pending.Replace("%", string.Empty).Trim();
                    int parsed;
                    if (int.TryParse(pending, out parsed)) value = Mathf.Clamp(parsed, min, max);
                    editBuf.Remove(key);
                }
            }
            return value;
        }

        /// <summary>百分比步进器（0-100，Shift 时 ±10）。</summary>
        private float PctStepper(Rect r, ref int value, string keyName)
        {
            float bw = 26f;
            Rect minus = new Rect(r.x, r.y, bw, r.height);
            Rect box = new Rect(minus.xMax, r.y, r.width - bw * 2f, r.height);
            Rect plus = new Rect(box.xMax, r.y, bw, r.height);

            int step = Event.current.shift ? 10 : 5;
            if (Widgets.ButtonText(minus, "-")) value = Mathf.Max(0, value - step);
            if (Widgets.ButtonText(plus, "+")) value = Mathf.Min(100, value + step);

            value = IntField(box, "pct_" + keyName, value, 0, 100);
            return plus.xMax;
        }

        private float Stepper(Rect r, ref int value, int clampMax, string keyName)
        {
            float bw = 26f;
            Rect minus = new Rect(r.x, r.y, bw, r.height);
            Rect box = new Rect(minus.xMax, r.y, r.width - bw * 2f, r.height);
            Rect plus = new Rect(box.xMax, r.y, bw, r.height);

            int step = Event.current.shift ? 10 : 1;
            if (Widgets.ButtonText(minus, "-")) value = Mathf.Max(0, value - step);
            if (Widgets.ButtonText(plus, "+")) value = Mathf.Min(clampMax, value + step);

            value = IntField(box, "cnt_" + keyName, value, 0, clampMax);
            return plus.xMax;
        }

        /// <summary>给页签按钮用。</summary>
        public void SeedFromStockPublic() { SeedFromStock(); }

        private void SeedFromStock()
        {
            comp.rules.Clear();
            SlotGroup group = storage.GetSlotGroup();
            if (group == null) return;
            Dictionary<ThingDef, int> counts = new Dictionary<ThingDef, int>();
            foreach (Thing t in group.HeldThings)
            {
                if (t == null) continue;
                int n;
                counts.TryGetValue(t.def, out n);
                counts[t.def] = n + t.stackCount;
            }
            foreach (KeyValuePair<ThingDef, int> kv in counts)
            {
                comp.rules.Add(new RestockRule
                {
                    def = kv.Key,
                    min = Mathf.Max(1, kv.Value / 2),
                    max = Mathf.Max(kv.Value, kv.Value / 2 + 1) * 2
                });
            }
        }
    }

    public static class RestockConflict
    {
        private static bool? active;

        public static bool SmartRestockActive()
        {
            if (active.HasValue) return active.Value;
            bool found = false;
            try
            {
                foreach (ModContentPack pack in LoadedModManager.RunningMods)
                {
                    if (pack == null || pack.PackageId == null) continue;
                    if (pack.PackageId.ToLowerInvariant().Contains("storagerefillhysteresis")) { found = true; break; }
                }
            }
            catch { }
            active = found;
            return found;
        }

        /// <summary>
        /// 根源性排斥：两个补货系统同时存在时，直接让本 mod 加载失败，
        /// 而不是进游戏后弹一句提示（那样玩家会一直带着冲突玩下去）。
        /// </summary>
        public static void Guard()
        {
            if (!SmartRestockActive()) return;
            string msg = "[DSH-ECONOMY] 加载中止：检测到 Storage Refill Hysteresis（存储补货滞后）同时启用。\n"
                + "本 mod 的仓库补货与它功能重叠，会互相抢搬运任务，因此两者不能共存。\n"
                + "请在 mod 列表里停用其中一个，然后重启游戏。";
            Log.Error(msg);
            throw new System.InvalidOperationException(
                "DSH 内部经济框架 与 Storage Refill Hysteresis（存储补货滞后）互斥，请停用其中一个后再启动。");
        }
    }
}

