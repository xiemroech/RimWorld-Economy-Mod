using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 成交量统计：按物品记"本期买了多少件"，每期滚动一次（本期→上期）。
    ///
    /// 用途：给定价加"需求"这一维 —— 同样缺货，抢的人多就涨得更凶；
    /// 没人要的东西，就算缺货也涨不动。这是"社会需要"那一侧的信号，
    /// 与"库存缺口"（供给那一侧）合起来才是完整的供求。
    ///
    /// 键一律用 defName（不是 ThingDef 引用），存档里就是普通字符串，不会有解析失败的问题。
    /// </summary>
    public static class EconomyTradeVolume
    {
        /// <summary>一期多长：1 天。太短噪声大，太长反应慢。</summary>
        public const int PeriodTicks = 60000;

        /// <summary>需求倍率上下限：需求再弱也不会把价格压到 0.5 倍以下，再强也不超过 2 倍。</summary>
        public const float DemandMin = 0.5f;
        public const float DemandMax = 2f;

        public static Dictionary<string, int> current = new Dictionary<string, int>();
        public static Dictionary<string, int> previous = new Dictionary<string, int>();
        public static int lastRollTick = -99999;

        public static void Note(ThingDef def, int count)
        {
            if (def == null || count <= 0) return;
            string key = def.defName;
            if (key == null) return;
            int old;
            current.TryGetValue(key, out old);
            current[key] = old + count;
        }

        /// <summary>
        /// 需求倍率：本期相对上期买得更多 → &gt;1（涨价），买得更少 → &lt;1（跌价）。
        /// 用开方压一下幅度，避免"多买一件就翻倍"。
        /// </summary>
        public static float DemandFactor(ThingDef def)
        {
            if (def == null || def.defName == null) return 1f;
            int now, before;
            current.TryGetValue(def.defName, out now);
            previous.TryGetValue(def.defName, out before);

            // 上期没成交过：这期只要有成交就当作"新出现的需求"，但幅度给一半，
            // 免得新商品第一次卖就暴涨。
            float a = now;
            float b = before;
            if (b <= 0f) b = Mathf.Max(1f, a * 2f);

            float factor = Mathf.Sqrt((a + 1f) / (b + 1f));
            return Mathf.Clamp(factor, DemandMin, DemandMax);
        }

        /// <summary>到点了就把本期变成上期，本期清零。</summary>
        public static void Roll(int now)
        {
            if (now - lastRollTick < PeriodTicks) return;
            lastRollTick = now;

            previous.Clear();
            foreach (KeyValuePair<string, int> kv in current) previous[kv.Key] = kv.Value;
            current.Clear();
        }

        public static void Clear()
        {
            current.Clear();
            previous.Clear();
            lastRollTick = -99999;
        }

        /// <summary>本期一共成交了多少件（所有物品合计）。用来粗估"钱转得快不快"。</summary>
        public static int TotalThisPeriod()
        {
            int total = 0;
            foreach (KeyValuePair<string, int> kv in current) total += kv.Value;
            return total;
        }

        public static int CurrentOf(ThingDef def)
        {
            int v;
            if (def != null && def.defName != null && current.TryGetValue(def.defName, out v)) return v;
            return 0;
        }

        public static int PreviousOf(ThingDef def)
        {
            int v;
            if (def != null && def.defName != null && previous.TryGetValue(def.defName, out v)) return v;
            return 0;
        }
    }
}
