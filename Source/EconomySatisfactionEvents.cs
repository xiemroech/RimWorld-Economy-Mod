using System.Collections.Generic;
using System.Text;
using Verse;

namespace DshEconomy
{
    /// <summary>一条满意度事件：有名字、有每日影响、有到期时间，会逐日累积并留下记录。</summary>
    public class SatisfactionEvent
    {
        public Pawn pawn;
        public string label;
        public float daily;
        public int expireTick;
        public int startedTick;
        public float applied;
    }

    /// <summary>
    /// 满意度事件表。测试用的加减不再直接改数值，而是挂一条有时限的事件，
    /// 每天结算时按事件累加，这样明细里能看到来源，也能追溯。
    /// </summary>
    public static class EconomySatisfactionEvents
    {
        private static readonly List<SatisfactionEvent> active = new List<SatisfactionEvent>();

        public static IReadOnlyList<SatisfactionEvent> All
        {
            get { return active; }
        }

        private static int Now
        {
            get { return Find.TickManager != null ? Find.TickManager.TicksGame : 0; }
        }

        public static void Add(Pawn pawn, string label, float daily, int days)
        {
            if (pawn == null || days <= 0) return;

            // 同名事件最多叠 MaxStacks 层，超出就只刷新持续时间，不再继续加深
            if (CountOf(pawn, label) >= MaxStacks)
            {
                RefreshDuration(pawn, label, days);
                return;
            }

            // 同名事件按原版心情那样"叠加"：点几次就是几层，明细里显示成 ×N。
            var e = new SatisfactionEvent
            {
                pawn = pawn,
                label = label,
                daily = daily,
                startedTick = Now,
                expireTick = Now + days * 60000
            };
            active.Add(e);
            Log.Message("[SAT] 事件开始：" + pawn.LabelShort + " " + label + " 每日 " + daily.ToString("+0.0;-0.0")
                + " 持续 " + days + " 天（当前同名 " + CountOf(pawn, label) + " 层）");

            // 满意度是即时结算的，事件一变当场重算，不用等下一次日结算
            EconomySatisfaction.Recompute(pawn);
        }

        /// <summary>同名事件最多叠几层。</summary>
        public const int MaxStacks = 8;

        private static void RefreshDuration(Pawn pawn, string label, int days)
        {
            int until = Now + days * 60000;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].pawn == pawn && active[i].label == label && active[i].expireTick < until)
                    active[i].expireTick = until;
            }
        }

        private static int CountOf(Pawn pawn, string label)
        {
            int n = 0;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].pawn == pawn && active[i].label == label) n++;
            }
            return n;
        }

        public static void AddForAll(Map map, string label, float daily, int days)
        {
            if (map == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++)
            {
                Add(colonists[i], label, daily, days);
            }
        }

        public static List<SatisfactionEvent> For(Pawn pawn)
        {
            var result = new List<SatisfactionEvent>();
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].pawn == pawn) result.Add(active[i]);
            }
            return result;
        }

        public static float SumFor(Pawn pawn)
        {
            float sum = 0f;
            for (int i = 0; i < active.Count; i++)
            {
                if (active[i].pawn == pawn) sum += active[i].daily;
            }
            return sum;
        }

        /// <summary>每天结算时调用：把每条事件的每日影响累加，并记录。</summary>
        public static float ApplyDaily(Pawn pawn)
        {
            float sum = 0f;
            for (int i = 0; i < active.Count; i++)
            {
                SatisfactionEvent e = active[i];
                if (e.pawn != pawn) continue;
                sum += e.daily;
                e.applied += e.daily;
                Log.Message("[SAT] " + pawn.LabelShort + " 事件结算 " + e.label + " "
                    + e.daily.ToString("+0.0;-0.0") + "（累计 " + e.applied.ToString("+0.0;-0.0") + "）");
            }
            return sum;
        }

        public static void Tick()
        {
            int now = Now;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (now >= active[i].expireTick)
                {
                    Log.Message("[SAT] 事件结束：" + active[i].pawn.LabelShort + " " + active[i].label
                        + "（共结算 " + active[i].applied.ToString("+0.0;-0.0") + "）");
                    active.RemoveAt(i);
                }
            }
        }

        public static void Clear(Pawn pawn = null)
        {
            if (pawn == null)
            {
                active.Clear();
                RecomputeAll();
                return;
            }
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].pawn == pawn) active.RemoveAt(i);
            }
            EconomySatisfaction.Recompute(pawn);
        }

        /// <summary>删掉某个人身上名称以 prefix 开头的事件（奖金这类会刷新的效果用）。</summary>
        public static void RemoveByPrefix(Pawn pawn, string prefix)
        {
            if (pawn == null) return;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                if (active[i].pawn == pawn && active[i].label != null && active[i].label.StartsWith(prefix))
                    active.RemoveAt(i);
            }
        }

        /// <summary>清空后把当前地图上所有殖民者重算一遍，进度条立刻回到基准。</summary>
        private static void RecomputeAll()
        {
            Map map = Find.CurrentMap;
            if (map == null) return;
            List<Pawn> colonists = map.mapPawns.FreeColonists;
            for (int i = 0; i < colonists.Count; i++) EconomySatisfaction.Recompute(colonists[i]);
        }

        public static string Describe(Pawn pawn)
        {
            List<SatisfactionEvent> list = For(pawn);
            if (list.Count == 0) return "（无）";
            var sb = new StringBuilder();
            for (int i = 0; i < list.Count; i++)
            {
                int daysLeft = System.Math.Max(1, (list[i].expireTick - Now) / 60000);
                if (i > 0) sb.Append("，");
                sb.Append(list[i].label).Append(" ").Append(list[i].daily.ToString("+0.0;-0.0"))
                  .Append("/日（剩 ").Append(daysLeft).Append(" 天）");
            }
            return sb.ToString();
        }
    }
}
