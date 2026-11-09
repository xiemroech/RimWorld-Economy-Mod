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
        /// <summary>每日成交量的平滑均值（0.7 旧 + 0.3 新）。用来推算"这个殖民地每天用掉多少件"。</summary>
        public static Dictionary<string, float> dailyAverage = new Dictionary<string, float>();
        public static int lastRollTick = -99999;

        /// <summary>
        /// 读档时 Scribe 可能把这两个字段置空（存档里没有对应节点、或类型对不上）。
        /// 之后任何一次访问都会空引用，而且会被 UI 每帧调用 → 疯狂刷错。
        /// 所有公开入口都先过一遍这里，宁可重置成空表也不要崩。
        /// </summary>
        public static void Ensure()
        {
            if (current == null) current = new Dictionary<string, int>();
            if (previous == null) previous = new Dictionary<string, int>();
            if (dailyAverage == null) dailyAverage = new Dictionary<string, float>();
        }

        public static void Note(ThingDef def, int count)
        {
            Ensure();
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
            Ensure();
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
            Ensure();
            if (now - lastRollTick < PeriodTicks) return;
            lastRollTick = now;

            previous.Clear();
            foreach (KeyValuePair<string, int> kv in current) previous[kv.Key] = kv.Value;

            // 记一笔平滑日耗：0.7 旧均值 + 0.3 本期实际。参考库存靠它推算"够用多少天"。
            foreach (KeyValuePair<string, int> kv in current)
            {
                float old;
                dailyAverage.TryGetValue(kv.Key, out old);
                dailyAverage[kv.Key] = old * 0.7f + kv.Value * 0.3f;
            }

            current.Clear();
        }

        public static void Clear()
        {
            Ensure();
            current.Clear();
            previous.Clear();
            lastRollTick = -99999;
        }

        /// <summary>本期一共成交了多少件（所有物品合计）。用来粗估"钱转得快不快"。</summary>
        public static int TotalThisPeriod()
        {
            Ensure();
            int total = 0;
            foreach (KeyValuePair<string, int> kv in current) total += kv.Value;
            return total;
        }

        public static int CurrentOf(ThingDef def)
        {
            Ensure();
            int v;
            if (def != null && def.defName != null && current.TryGetValue(def.defName, out v)) return v;
            return 0;
        }

        /// <summary>这件东西平均每天成交多少件。没数据返回 0。</summary>
        public static float DailyAverage(ThingDef def)
        {
            Ensure();
            if (def == null || def.defName == null) return 0f;
            float v;
            if (dailyAverage.TryGetValue(def.defName, out v)) return v;
            return 0f;
        }

        public static int PreviousOf(ThingDef def)
        {
            Ensure();
            int v;            if (def != null && def.defName != null && previous.TryGetValue(def.defName, out v)) return v;
            return 0;
        }
    }
}
