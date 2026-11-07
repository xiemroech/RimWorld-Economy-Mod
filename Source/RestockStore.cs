using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>单条补货规则：某物品在这个仓库里保持 [min, max] 件。</summary>
    public class RestockRule : IExposable
    {
        public ThingDef def;

        /// <summary>旧版按件数的下限/上限，仅用于读老存档时迁移。</summary>
        public int min;
        public int max;

        /// <summary>
        /// 占多少百分比（0-100）：100% = 这个仓库的全部栏位（格数 × 每格堆数）。
        /// 同一仓库里所有规则的百分比之和不超过 100%，加一条会自动压缩其它条。
        /// </summary>
        public float pct = 10f;

        /// <summary>下限百分比（旧字段，现由 pct 推导）。</summary>
        public float minPct = 20f;

        /// <summary>上限百分比（0-100）。</summary>
        public float maxPct = 40f;

        /// <summary>勾上后不手填百分比，和其它"自动"的物品平分剩余百分比。</summary>
        public bool autoShare;

        /// <summary>手动指定的精确件数（0 = 不用，按占比算）。</summary>
        public int exactItems;

        public void ExposeData()
        {
            Scribe_Defs.Look(ref def, "def");
            Scribe_Values.Look(ref min, "min", 0);
            Scribe_Values.Look(ref max, "max", 0);
            Scribe_Values.Look(ref pct, "pct", -1f);
            Scribe_Values.Look(ref minPct, "minPct", 20f);
            Scribe_Values.Look(ref maxPct, "maxPct", 40f);
            Scribe_Values.Look(ref autoShare, "autoShare", false);
            Scribe_Values.Look(ref exactItems, "exactItems", 0);
            // 老存档：没有 pct 时从旧的 min/max 百分比或件数推一个出来
            if (Scribe.mode == LoadSaveMode.LoadingVars && pct <= 0f)
            {
                if (maxPct > 0f) pct = maxPct;
                else if (max > 0) pct = 10f;
                else pct = 10f;
            }
        }

        public RestockRule Clone()
        {
            return new RestockRule
            {
                def = def, min = min, max = max,
                pct = pct, minPct = minPct, maxPct = maxPct, autoShare = autoShare, exactItems = exactItems
            };
        }
    }

    /// <summary>某个存储建筑的补货设置。</summary>
    public class RestockSettings : IExposable
    {
        /// <summary>勾上「细化运输设置」之后才按 rules 走，否则按「把允许的物品补满空格」。</summary>
        public bool detailed;

        public List<RestockRule> rules = new List<RestockRule>();

        public void ExposeData()
        {
            Scribe_Values.Look(ref detailed, "dsh_restockDetailed", false);
            Scribe_Collections.Look(ref rules, "dsh_restockRules", LookMode.Deep);
            if (rules == null) rules = new List<RestockRule>();
        }
    }

    /// <summary>
    /// 补货设置的存在处：建筑 → 设置 的地图级映射。
    /// 不用 ThingComp，是因为部分存储 mod 会重建/覆盖 comps 列表，
    /// 组件会被丢掉（表现为"这个建筑没有补货组件"）。
    /// </summary>
    public class RestockStore : MapComponent
    {
        private List<Building_Storage> keys = new List<Building_Storage>();
        private List<RestockSettings> values = new List<RestockSettings>();

        public RestockStore(Map map) : base(map)
        {
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref keys, "dsh_restockKeys", LookMode.Reference);
            Scribe_Collections.Look(ref values, "dsh_restockValues", LookMode.Deep);
            if (keys == null) keys = new List<Building_Storage>();
            if (values == null) values = new List<RestockSettings>();
        }

        public RestockSettings Get(Building_Storage st, bool create)
        {
            if (st == null) return null;
            for (int i = 0; i < keys.Count; i++)
            {
                if (keys[i] == st) return values[i];
            }
            if (!create) return null;
            RestockSettings cfg = new RestockSettings();
            keys.Add(st);
            values.Add(cfg);
            return cfg;
        }

        public static RestockStore Of(Thing t)
        {
            if (t == null) return null;
            Map map = t.MapHeld ?? t.Map;
            return map != null ? map.GetComponent<RestockStore>() : null;
        }

        /// <summary>取（必要时创建）某个存储建筑的补货设置。</summary>
        public static RestockSettings For(Building_Storage st)
        {
            RestockStore store = Of(st);
            return store != null ? store.Get(st, true) : null;
        }

        /// <summary>只查不建，给补货逻辑用。</summary>
        /// <summary>这个仓库的栏位总数：格数 × 每格堆数。</summary>
        public static int TotalSlots(Building_Storage st)
        {
            if (st == null) return 0;
            return Mathf.Max(0, ContainerUtil.CapacitySlots(st));
        }

        /// <summary>
        /// 某物品在这个仓库里的栏位配额（整组）。
        /// 有占比就至少给 1 组，并对小数向上取整——宁可多占一格，也不要把格子空着浪费。
        /// </summary>
        public static int SlotsFor(Building_Storage st, float pct)
        {
            float share = TotalSlots(st) * Mathf.Clamp(pct, 0f, 100f) / 100f;
            if (share <= 0f) return 0;
            return Mathf.Max(1, Mathf.CeilToInt(share));
        }

        /// <summary>
        /// 把所有规则的百分比当作共享的 100% 处理：总和超过 100 时，按比例压缩其它条。
        /// </summary>
        public static void Normalize(RestockSettings cfg, RestockRule changed)
        {
            if (cfg == null) return;
            changed.pct = Mathf.Clamp(changed.pct, 0f, 100f);
            float others = 0f;
            foreach (RestockRule r in cfg.rules)
            {
                if (r == null || r == changed || r.def == null) continue;
                others += Mathf.Max(0f, r.pct);
            }
            float room = Mathf.Max(0f, 100f - changed.pct);
            if (others <= room + 0.001f) return;
            float scale = others > 0.001f ? room / others : 0f;
            foreach (RestockRule r in cfg.rules)
            {
                if (r == null || r == changed || r.def == null) continue;
                r.pct = Mathf.Max(0f, r.pct * scale);
            }
        }

        /// <summary>所有规则占比之和（用于显示，上限 100）。</summary>
        public static float TotalPct(RestockSettings cfg)
        {
            float sum = 0f;
            if (cfg == null) return 0f;
            foreach (RestockRule r in cfg.rules)
            {
                if (r == null || r.def == null) continue;
                sum += Mathf.Max(0f, r.pct);
            }
            return Mathf.Min(100f, sum);
        }

        /// <summary>
        /// 一"组"最多装多少件：用 def.stackLimit（这是硬上限）。
        /// 容量必须按"每组多少个"来算，否则会低估目标量、搬不满格子。
        /// </summary>
        public static int ItemsPerSlot(Building_Storage st, ThingDef def)
        {
            if (def == null) return 1;
            return Mathf.Max(1, def.stackLimit);
        }

        /// <summary>按栏位配额算该物品的目标件数（现行值低于一半时补到目标）。</summary>
        public static int TargetItems(Building_Storage st, RestockRule r)
        {
            if (st == null || r == null || r.def == null) return 0;
            if (r.exactItems > 0) return r.exactItems;
            int slots = SlotsFor(st, r.pct);
            if (slots <= 0) return 0;
            return Mathf.Max(1, slots * ItemsPerSlot(st, r.def));
        }

        /// <summary>只查不建，给补货逻辑用。</summary>
        /// <summary>某物品在这个仓库里的理论容量（件）：格数 × 每格堆数 × 堆叠上限。</summary>
        public static int CapacityItems(Building_Storage st, ThingDef def)
        {
            if (st == null || def == null) return 0;
            int slots = ContainerUtil.CapacitySlots(st);
            if (slots <= 0) return 0;
            return Mathf.Max(1, slots * ItemsPerSlot(st, def));
        }

        /// <summary>把"百分比 / 自动平分"解成件数阈值。</summary>
        public static void ResolveThresholds(RestockSettings cfg, Building_Storage st,
            out List<RestockRule> rules, out List<int> lows, out List<int> highs)
        {
            rules = new List<RestockRule>();
            lows = new List<int>();
            highs = new List<int>();
            if (cfg == null || st == null) return;

            float manual = 0f;
            int autos = 0;
            foreach (RestockRule r in cfg.rules)
            {
                if (r == null || r.def == null) continue;
                if (r.autoShare) autos++;
                else manual += Mathf.Clamp(r.maxPct, 0f, 100f);
            }
            float share = autos > 0 ? Mathf.Max(0f, 100f - manual) / autos : 0f;

            foreach (RestockRule r in cfg.rules)
            {
                if (r == null || r.def == null) continue;
                float maxPct = r.autoShare ? share : Mathf.Clamp(r.maxPct, 0f, 100f);
                float minPct = r.autoShare ? share * 0.5f : Mathf.Clamp(r.minPct, 0f, 100f);
                if (maxPct <= 0f) continue;
                if (minPct > maxPct) minPct = maxPct;
                int cap = CapacityItems(st, r.def);
                int low = Mathf.RoundToInt(cap * minPct / 100f);
                int high = Mathf.RoundToInt(cap * maxPct / 100f);
                if (high <= 0) continue;
                rules.Add(r); lows.Add(low); highs.Add(high);
            }
        }

        public static RestockSettings Lookup(Building_Storage st)
        {
            RestockStore store = Of(st);
            return store != null ? store.Get(st, false) : null;
        }
    }
}
