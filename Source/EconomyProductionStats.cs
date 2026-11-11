using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 产地统计：这件东西在**这个殖民地**里，实际是怎么来的。
    ///
    /// 为什么需要它：
    /// 同一个定义可能有多种来源。钢铁主要靠挖，但 mod 也可能给你一条"废渣炼钢"的配方；
    /// 玻璃钢原版靠挖，有的 mod 让动物产出；布原版是棉花种的，有的 mod 加了织布配方。
    /// 只看定义去猜，永远会猜错其中一个 —— 所以让游戏自己记账。
    ///
    /// 记四类来源：合成 / 挖掘 / 收割 / 屠宰。
    /// 定价时取"出现次数最多的那一类"来决定成本口径；
    /// 样本不足（少于 3 次）时返回 false，退回原来的单分支判断，避免开局乱猜。
    /// </summary>
    public static class EconomyProductionStats
    {
        public enum Source { Recipe = 0, Mine = 1, Harvest = 2, Butcher = 3, Gather = 4 }

        /// <summary>一条记录：某个定义的几种来源各发生过多少次（按件数累计）。</summary>
        public class Record : IExposable
        {
            public string defName;
            public int recipe;
            public int mine;
            public int harvest;
            public int butcher;
            public int gather;

            // LookMode.Deep 要求元素实现 IExposable，否则存盘时会报
            // "Cannot use LookDeep to save non-IExposable non-null li" 并丢掉整张表。
            public void ExposeData()
            {
                Scribe_Values.Look(ref defName, "def", null);
                Scribe_Values.Look(ref recipe, "recipe", 0);
                Scribe_Values.Look(ref mine, "mine", 0);
                Scribe_Values.Look(ref harvest, "harvest", 0);
                Scribe_Values.Look(ref butcher, "butcher", 0);
                Scribe_Values.Look(ref gather, "gather", 0);
            }
        }

        /// <summary>存档用的扁平列表（Scribe 不能直接序列化 Dictionary）。</summary>
        public static List<Record> records = new List<Record>();

        private static readonly Dictionary<string, Record> index = new Dictionary<string, Record>();

        /// <summary>样本少于这个数就不下结论，免得开局第一次挖到铁就把铁定性成"矿"。</summary>
        public const int MinSamples = 3;

        public static void Note(ThingDef def, Source source, int count)
        {
            if (def == null || count <= 0) return;
            Record r = Get(def.defName);
            switch (source)
            {
                case Source.Recipe: r.recipe += count; break;
                case Source.Mine: r.mine += count; break;
                case Source.Harvest: r.harvest += count; break;
                case Source.Butcher: r.butcher += count; break;
                case Source.Gather: r.gather += count; break;
            }
        }

        private static Record Get(string defName)
        {
            Record r;
            if (index.TryGetValue(defName, out r)) return r;
            r = new Record { defName = defName };
            index[defName] = r;
            records.Add(r);
            return r;
        }

        /// <summary>
        /// 这件东西在这个殖民地里最主要的来源。
        /// 样本不足返回 false（调用方退回旧逻辑）。
        /// </summary>
        public static bool Dominant(ThingDef def, out Source source)
        {
            source = Source.Recipe;
            if (def == null) return false;
            Record r;
            if (!index.TryGetValue(def.defName, out r)) return false;

            int total = r.recipe + r.mine + r.harvest + r.butcher + r.gather;
            if (total < MinSamples) return false;

            int best = r.recipe;
            source = Source.Recipe;
            if (r.mine > best) { best = r.mine; source = Source.Mine; }
            if (r.harvest > best) { best = r.harvest; source = Source.Harvest; }
            if (r.butcher > best) { best = r.butcher; source = Source.Butcher; }
            if (r.gather > best) { best = r.gather; source = Source.Gather; }
            return true;
        }

        /// <summary>调试用：把某件东西的统计打出来。</summary>
        public static string Describe(ThingDef def)
        {
            if (def == null) return "无";
            Record r;
            if (!index.TryGetValue(def.defName, out r)) return "无记录";
            return "合成=" + r.recipe + " 挖掘=" + r.mine + " 收割=" + r.harvest + " 屠宰=" + r.butcher + " 产出=" + r.gather;
        }

        public static void Reset()
        {
            records.Clear();
            index.Clear();
        }

        /// <summary>多久衰减一次（游戏内 15 天）。</summary>
        public const int DecayIntervalTicks = 900000;

        /// <summary>
        /// 衰减：把所有计数减半（各除以 2，至少保留 1）。
        /// 目的：让"最近怎么来的"占主导，而不是被几十天前的旧账压住。
        /// 只减半不清零 —— 清零会让已经定性的物品瞬间退回"猜"的状态。
        /// </summary>
        public static void Decay()
        {
            for (int i = 0; i < records.Count; i++)
            {
                Record r = records[i];
                if (r == null) continue;
                r.recipe = Halve(r.recipe);
                r.mine = Halve(r.mine);
                r.harvest = Halve(r.harvest);
                r.butcher = Halve(r.butcher);
                r.gather = Halve(r.gather);
            }
        }

        private static int Halve(int n)
        {
            if (n <= 0) return 0;
            if (n == 1) return 1;      // 保留 1，避免"有记录"变成"没记录"
            return n / 2;
        }

        /// <summary>读档后把扁平列表灌回字典。</summary>
        public static void RebuildIndex()
        {
            index.Clear();
            if (records == null) records = new List<Record>();
            for (int i = 0; i < records.Count; i++)
            {
                Record r = records[i];
                if (r == null || r.defName == null) continue;
                index[r.defName] = r;
            }
        }

        /// <summary>
        /// 累计挖矿原始工时（tick）。独立于工资的"本期"账本：工资那边每期会清，
        /// 这里不清，只在新档/衰减时保留，用来算"挖一件平均要多少人日"。
        /// </summary>
        public static float miningTicks;

        /// <summary>记一段挖矿工时。由 MapEconomyComponent.AddWorkHours 顺手调用。</summary>
        public static void NoteMiningLabor(float rawTicks)
        {
            if (rawTicks <= 0f) return;
            miningTicks += rawTicks;
            if (miningTicks > 60000000f) miningTicks = 60000000f;   // 防溢出，约 1000 人日
        }

        /// <summary>累计钓鱼原始工时（tick）。和挖矿一样，独立长期账。</summary>
        public static float fishingTicks;

        /// <summary>累计钓上来的条数（按"完成一次钓鱼"记 1 条）。</summary>
        public static int fishingCount;

        /// <summary>记一段钓鱼工时。</summary>
        public static void NoteFishingLabor(float rawTicks)
        {
            if (rawTicks <= 0f) return;
            fishingTicks += rawTicks;
            if (fishingTicks > 60000000f) fishingTicks = 60000000f;
        }

        /// <summary>记一次"钓到东西"。没有产量字段可读，按次计。</summary>
        public static void NoteFishingCatch(int n)
        {
            if (n <= 0) return;
            fishingCount += n;
            if (fishingCount > 100000) fishingCount = 100000;
        }

        /// <summary>平均一条鱼花多少 tick。样本不足返回 0。</summary>
        public static float FishingTicksPerCatch()
        {
            if (fishingCount <= 0 || fishingTicks <= 0f) return 0f;
            return fishingTicks / fishingCount;
        }

        /// <summary>某一种来源一共记了多少件（跨所有物品）。</summary>
        public static int TotalOf(Source source)
        {
            int n = 0;
            for (int i = 0; i < records.Count; i++)
            {
                Record r = records[i];
                if (r == null) continue;
                switch (source)
                {
                    case Source.Recipe: n += r.recipe; break;
                    case Source.Mine: n += r.mine; break;
                    case Source.Harvest: n += r.harvest; break;
                    case Source.Butcher: n += r.butcher; break;
                    case Source.Gather: n += r.gather; break;
                }
            }
            return n;
        }

        /// <summary>
        /// 总社会劳动生产率：这段时间里产出的东西值多少钱 ÷ 造它们花了多少人日。
        /// 单位是"银/人日"。样本太少（< 3 件）返回 -1，界面显示"—"。
        /// 这是宏观提示，不参与任何定价 —— 定价仍逐件走 c + v+s。
        /// </summary>
        public static float SocialLaborProductivity(Map map)
        {
            if (map == null) return -1f;
            try
            {
                float totalValue = 0f;
                float totalDays = 0f;
                int count = 0;
                for (int i = 0; i < records.Count; i++)
                {
                    Record r = records[i];
                    if (r == null || r.defName == null) continue;
                    ThingDef def = DefDatabase<ThingDef>.GetNamedSilentFail(r.defName);
                    if (def == null) continue;
                    int n = r.recipe + r.mine + r.harvest + r.butcher + r.gather;
                    if (n <= 0) continue;
                    float days = EconomyValue.LaborDaysPerUnit(def);
                    if (days <= 0f) continue;
                    totalValue += EconomyValue.Of(def, map) * n;
                    totalDays += days * n;
                    count += n;
                }
                if (count < 3 || totalDays <= 0.01f) return -1f;
                return totalValue / totalDays;
            }
            catch { return -1f; }
        }
    }
}
