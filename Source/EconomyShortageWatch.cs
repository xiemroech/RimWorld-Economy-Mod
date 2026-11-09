using System.Collections.Generic;
using System.Text;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 结构性短缺：某种东西连续几天都低于参考库存的一成，就记一笔。
    /// 不逐个发信——一封汇总信，按"领域"分组，免得刷屏。
    /// 价格涨上去是在发信号，这封信把信号翻译成人话。
    ///
    /// 筛选（白名单语义，空 = 全都要）：
    ///   · 设置里「只看原版物品」→ 其它 mod / 种族产物不进信
    ///   · 设置里「只提示勾选的分类」→ 只有勾的分类进信
    ///   · 「判断」窗里逐件勾选 → 只有勾的物品进信
    /// </summary>
    public static class EconomyShortageWatch
    {
        private const int DaysToWarn = 4;          // 连续几天才记
        private const int LetterCooldown = 180000; // 汇总信 3 天最多一封
        private const float LowShare = 0.1f;       // 低于参考库存 10% 算短缺
        private const int SamplesPerBucket = 3;    // 每个领域最多列几件

        private static Dictionary<string, int> streak;
        private static Dictionary<string, int> shortSince;
        private static int lastLetterTick = -999999;

        // 每次检查临时用的分组容器（不存档，纯临时）
        private static readonly Dictionary<string, List<string>> buckets =
            new Dictionary<string, List<string>>();
        private static readonly List<string> bucketOrder = new List<string>();

        public static void Ensure()
        {
            if (streak == null) streak = new Dictionary<string, int>();
            if (shortSince == null) shortSince = new Dictionary<string, int>();
        }

        public static void Reset()
        {
            if (streak != null) streak.Clear();
            if (shortSince != null) shortSince.Clear();
            lastLetterTick = -999999;
        }

        /// <summary>这件东西属于哪个"领域"（用经济分类名，取不到就按大类兜底）。</summary>
        private static string BucketOf(ThingDef def)
        {
            try
            {
                EconomyCategoryDef cat = EconomyRegistry.CategoryOf(def);
                if (cat != null && !string.IsNullOrEmpty(cat.label)) return cat.label;
            }
            catch { }

            if (def.IsIngestible) return "食物与药品";
            if (def.IsApparel) return "衣物";
            if (def.IsWeapon) return "武器";
            if (def.category == ThingCategory.Building) return "建筑与家具";
            return "其它物资";
        }

        private static string CatNameOf(ThingDef def)
        {
            try
            {
                EconomyCategoryDef cat = EconomyRegistry.CategoryOf(def);
                if (cat != null) return cat.defName;
            }
            catch { }
            return null;
        }

        /// <summary>玩家筛选：返回 true 表示这件东西可以进短缺信。</summary>
        private static bool IsTracked(ThingDef def)
        {
            DshEconomySettings st = DshEconomyMod.Settings;
            if (st == null) return true;
            try
            {
                // 只看原版：非官方 mod 的产物一律不进
                if (st.shortageVanillaOnly && def.modContentPack != null && !def.modContentPack.IsOfficialMod)
                    return false;

                bool hasDefs = st.shortageOnlyDefs != null && st.shortageOnlyDefs.Count > 0;
                bool hasCats = st.shortageOnlyCats != null && st.shortageOnlyCats.Count > 0;
                // 两个白名单都没填 = 全都要
                if (!hasDefs && !hasCats) return true;

                if (hasDefs && st.shortageOnlyDefs.Contains(def.defName)) return true;

                if (hasCats)
                {
                    string catName = CatNameOf(def);
                    if (catName != null && st.shortageOnlyCats.Contains(catName)) return true;
                }
                return false;
            }
            catch { }
            return true;
        }

        /// <summary>
        /// 把当前"低于一成库存"的东西收进 buckets。
        /// requireStreak=true 时只在连续 DaysToWarn 天后才算（正常巡检）；
        /// false 时只要现在低就算（手动试发用）。
        /// </summary>
        private static int Collect(Map map, MapEconomyComponent comp, bool requireStreak, int now)
        {
            buckets.Clear();
            bucketOrder.Clear();

            // 候选 = 最近被买卖过的 + 曾经用过的 + 现在仓里有货的。
            // 只看成交量是不够的：一场游戏里大多数东西是自产自用，根本不过市场，
            // 结果就是"什么都检测不到"。这里把库存表并进来。
            HashSet<ThingDef> candidates = new HashSet<ThingDef>();
            foreach (string k in EconomyTradeVolume.previous.Keys)
            {
                ThingDef d0 = DefDatabase<ThingDef>.GetNamedSilentFail(k);
                if (d0 != null) candidates.Add(d0);
            }
            foreach (string k in EconomyTradeVolume.current.Keys)
            {
                ThingDef d0 = DefDatabase<ThingDef>.GetNamedSilentFail(k);
                if (d0 != null) candidates.Add(d0);
            }
            foreach (string k in EconomyTradeVolume.dailyAverage.Keys)
            {
                ThingDef d0 = DefDatabase<ThingDef>.GetNamedSilentFail(k);
                if (d0 != null) candidates.Add(d0);
            }
            comp.AppendStockedDefs(candidates);

            int found = 0;
            foreach (ThingDef def in candidates)
            {
                if (def == null) continue;
                if (!IsTracked(def)) continue;

                float reference = EconomyPrice.ReferenceStock(def, map);
                if (reference < 0f) continue;

                int stock = comp.StockOf(def);
                // 小基数物品（武器、器官这类参考量只有几件）如果按一成算，
                // "剩 0 件"都可能触发不了，所以至少保证"一件都没有"能报出来。
                bool low = stock < Mathf.Max(1f, reference * LowShare);

                if (requireStreak)
                {
                    string defKey = def.defName;
                    int s;
                    streak.TryGetValue(defKey, out s);
                    s = low ? s + 1 : 0;
                    streak[defKey] = s;
                    if (s < DaysToWarn) continue;

                    int since;
                    if (!shortSince.TryGetValue(defKey, out since)) shortSince[defKey] = now;
                }
                else if (!low)
                {
                    continue;
                }

                string bucket = BucketOf(def);
                List<string> list;
                if (!buckets.TryGetValue(bucket, out list))
                {
                    list = new List<string>();
                    buckets[bucket] = list;
                    bucketOrder.Add(bucket);
                }
                if (list.Count < SamplesPerBucket)
                    list.Add(def.LabelCap + "（" + stock + "/" + reference.ToString("0") + "）");
                found++;
            }
            return found;
        }

        private static void SendLetter(bool forced)
        {
            StringBuilder sb = new StringBuilder();
            if (forced)
                sb.AppendLine("（手动试发：按当前库存判断，不等连续 " + DaysToWarn + " 天。）");
            else
                sb.AppendLine("这些物资已经连续 " + DaysToWarn + " 天低于正常库存的一成，价格因此一直高挂：");
            sb.AppendLine();

            for (int i = 0; i < bucketOrder.Count; i++)
            {
                string bucket = bucketOrder[i];
                sb.Append("· ").Append(bucket).Append("：");
                sb.AppendLine(string.Join("、", buckets[bucket].ToArray()));
            }
            sb.AppendLine();
            sb.Append("价格涨过价值是在发「该多生产」的信号。要么扩大生产，要么从外面买进来；");
            sb.AppendLine("长期靠高价硬撑，会变成结构性短缺。");
            sb.AppendLine();
            sb.Append("（每类只列缺口最大的几样，其余同类没列全。要改提示范围：");
            sb.Append("经济窗口 → 设置 →「只看原版物品 / 只提示勾选的分类」，");
            sb.Append("或在「价格 → 判断」窗里逐件勾选。）");

            Find.LetterStack.ReceiveLetter(LetterMaker.MakeLetter(
                "结构性短缺：" + bucketOrder.Count + " 类物资",
                sb.ToString(),
                LetterDefOf.NeutralEvent));
        }

        /// <summary>每 250 tick 由地图组件调一次。</summary>
        public static void Tick(Map map)
        {
            if (map == null) return;
            Ensure();
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return;

            int now = Find.TickManager.TicksGame;
            if (Collect(map, comp, true, now) == 0) return;
            if (now - lastLetterTick < LetterCooldown) return;
            lastLetterTick = now;
            SendLetter(false);
        }

        /// <summary>设置页「立即发一次预警」：跳过天数与冷却，直接按当前库存发一封。</summary>
        public static void ForceSend(Map map)
        {
            if (map == null)
            {
                Messages.Message("缺少地图：先进入一个殖民地地图。", MessageTypeDefOf.RejectInput, false);
                return;
            }
            Ensure();
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp == null) return;

            int now = Find.TickManager.TicksGame;
            int found = Collect(map, comp, false, now);
            if (found == 0)
            {
                Messages.Message("当前没有低于参考库存一成的物资（已按你的筛选条件检查）。",
                    MessageTypeDefOf.NeutralEvent, false);
                return;
            }
            lastLetterTick = now;
            SendLetter(true);
        }
    }
}
