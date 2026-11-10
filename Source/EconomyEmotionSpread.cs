using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 情绪传播：工作满意度低于 50 的人，每小时有一定概率把负面情绪传给殖民地里的另一个人。
    /// 被传染者同时掉心情（ThoughtDef）与工作满意度（事件），富人免疫。
    /// </summary>
    public static class EconomyEmotionSpread
    {
        public const string ThoughtDefName = "DshEco_Contagion";
        public const float BaseChancePerHour = 0.10f;
        public const float Radius = 30f;
        public const float MoodPenalty = -6f;
        public const float SatPenalty = -2f;
        public const int DurationDays = 3;
        public const float RichMultiple = 2f;
        public const float RichFloor = 200f;

        private static readonly Dictionary<Pawn, int> lastHitDay = new Dictionary<Pawn, int>();
        private static int lastTick = -99999;
        private static int lastTargetDay = -1;

        public static void ResetRuntime()
        {
            lastHitDay.Clear();
            lastTick = -99999;
            lastTargetDay = -1;
        }

        public static void Tick(Map map)
        {
            if (map == null || map.mapPawns == null) return;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastTick < 2500) return;
            lastTick = now;

            ThoughtDef thought = DefDatabase<ThoughtDef>.GetNamedSilentFail(ThoughtDefName);

            List<Pawn> colonists = map.mapPawns.FreeColonists;
            if (colonists == null || colonists.Count < 2) return;

            int day = now / GenDate.TicksPerDay;
            if (day != lastTargetDay)
            {
                lastTargetDay = day;
                lastHitDay.Clear();
            }

            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn src = colonists[i];
                if (src == null || src.Dead || src.InMentalState) continue;

                float sat = EconomyLedger.GetSatisfaction(src);
                if (sat >= 50f) continue;

                float chance = BaseChancePerHour * Mathf.Clamp01((50f - sat) / 50f) * 2f;
                if (!Rand.Chance(chance)) continue;

                Pawn target = PickTarget(map, src, colonists, day);
                if (target == null) continue;

                Apply(target, src, thought);
            }
        }

        private static Pawn PickTarget(Map map, Pawn src, List<Pawn> colonists, int day)
        {
            var pool = new List<Pawn>();
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn t = colonists[i];
                if (t == null || t == src || t.Dead || t.Downed) continue;
                if (t.InMentalState) continue;
                if (!t.Spawned || t.MapHeld != map) continue;
                if (t.Position.DistanceTo(src.Position) > Radius) continue;
                if (IsRich(t, map)) continue;
                int hitDay;
                if (lastHitDay.TryGetValue(t, out hitDay) && hitDay == day) continue;
                if (t.needs == null || t.needs.mood == null) continue;
                pool.Add(t);
            }
            return pool.Count == 0 ? null : pool.RandomElement();
        }

        /// <summary>富人：私有财富达到人均两倍以上（且不低于 200 银），不会被传染。</summary>
        public static bool IsRich(Pawn p, Map map)
        {
            if (p == null || map == null) return false;
            float avg = TaxPopulation.Cached(map, 600).wealthAvg;
            float mine = TaxPopulation.PawnPrivateWealth(p, EconomyBank.PrivateSilver(map, p));
            return mine >= Mathf.Max(RichFloor, avg * RichMultiple);
        }

        public static void Apply(Pawn target, Pawn source, ThoughtDef thought)
        {
            string from = source != null ? source.LabelShort : "某人";
            if (thought != null)
            {
                try { target.needs.mood.thoughts.memories.TryGainMemory(thought, source); }
                catch (System.Exception e) { Log.Warning("[EMOTION] 加心情失败：" + e.Message); }
            }
            EconomySatisfactionEvents.Add(target, "情绪传染（来自" + from + "）", SatPenalty, DurationDays);
            EconomySatisfaction.Recompute(target);

            int day = Find.TickManager != null ? Find.TickManager.TicksGame / GenDate.TicksPerDay : 0;
            lastHitDay[target] = day;
            DevLog.Msg("[EMOTION] " + from + "（满意度 " + EconomyLedger.GetSatisfaction(source).ToString("F0")
                + "）把负面情绪传给了 " + target.LabelShort + "：心情 " + MoodPenalty.ToString("F0")
                + "，" + DurationDays + " 天");
        }
    }
}

