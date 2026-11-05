using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>一个奖金组：玩家自定义的名字 + 成员列表。</summary>
    public class BonusGroup : IExposable
    {
        public string name = "组";
        public List<Pawn> members = new List<Pawn>();

        public bool Has(Pawn p)
        {
            return p != null && members != null && members.Contains(p);
        }

        public void Toggle(Pawn p)
        {
            if (p == null) return;
            if (members == null) members = new List<Pawn>();
            if (members.Contains(p)) members.Remove(p);
            else members.Add(p);
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref name, "name", "组");
            Scribe_Collections.Look(ref members, "members", LookMode.Reference);
            if (members == null) members = new List<Pawn>();
        }
    }

    /// <summary>奖金政策：按组发奖金，组内成员领钱，组外劳动最低的三人按名次扣满意度。</summary>
    public class BonusPolicy : IExposable
    {
        public bool enabled = false;
        public bool autoPay = true;
        public float amount = 50f;

        // 获奖人数（同时决定末位惩罚人数）。上限是成员数的 30%。
        public int recipients = 3;

        /// <summary>成员数允许的最大获奖人数：30%，至少 1 人。</summary>
        public int MaxRecipients(int memberCount)
        {
            return Mathf.Max(1, Mathf.FloorToInt(memberCount * 0.3f));
        }
        public List<BonusGroup> groups = new List<BonusGroup>();
        public int activeIndex;

        // 旧版本的单组字段，读档时迁移到第一组
        private List<Pawn> legacyRecipients;

        public const string GotBonusThought = "DshEco_GotBonus";
        public const string NoBonusThought = "DshEco_NoBonus";

        public BonusGroup Active
        {
            get
            {
                if (groups == null) groups = new List<BonusGroup>();
                if (groups.Count == 0) groups.Add(new BonusGroup { name = "组1" });
                if (activeIndex < 0) activeIndex = 0;
                if (activeIndex >= groups.Count) activeIndex = groups.Count - 1;
                return groups[activeIndex];
            }
        }

        public void AddGroup()
        {
            if (groups == null) groups = new List<BonusGroup>();
            groups.Add(new BonusGroup { name = "组" + (groups.Count + 1) });
            activeIndex = groups.Count - 1;
        }

        public void RemoveActiveGroup()
        {
            if (groups == null || groups.Count <= 1) return;
            groups.RemoveAt(activeIndex);
            if (activeIndex >= groups.Count) activeIndex = groups.Count - 1;
        }

        public void ExposeData()
        {
            Scribe_Values.Look(ref enabled, "enabled", false);
            Scribe_Values.Look(ref autoPay, "autoPay", true);
            Scribe_Values.Look(ref amount, "amount", 50f);
            Scribe_Values.Look(ref recipients, "recipients", 3);
            Scribe_Collections.Look(ref legacyRecipients, "recipients", LookMode.Reference);
            Scribe_Collections.Look(ref groups, "groups", LookMode.Deep);
            Scribe_Values.Look(ref activeIndex, "activeIndex", 0);

            if (groups == null) groups = new List<BonusGroup>();
            if (groups.Count == 0)
            {
                BonusGroup g = new BonusGroup { name = "组1" };
                if (legacyRecipients != null) g.members.AddRange(legacyRecipients);
                groups.Add(g);
            }
            legacyRecipients = null;
        }
    }

    public static class EconomyBonus
    {
        // 满意度事件的前缀：每期先清旧的再加新的，避免层层叠加
        private const string BonusPrefix = "奖金·";
        /// <summary>
        /// 按劳动量排名发放：组内前三名拿奖金（100% / 80% / 60%），
        /// 组内劳动最少的三人依次 −6 / −4 / −2 满意度。
        /// </summary>
        public static int Pay(Map map, BonusPolicy policy)
        {
            if (map == null || policy == null || !policy.enabled) return 0;            List<Pawn> colonists = map.mapPawns.FreeColonists;
            if (colonists == null || colonists.Count == 0) return 0;

            BonusGroup group = policy.Active;
            if (group.members == null) group.members = new List<Pawn>();
            if (group.members.Count == 0)
            {
                for (int i = 0; i < colonists.Count; i++) group.members.Add(colonists[i]);
            }

            List<Pawn> ranked = new List<Pawn>();
            for (int i = 0; i < group.members.Count; i++)
            {
                Pawn p = group.members[i];
                if (p != null && p.Spawned && p.IsColonist) ranked.Add(p);
            }
            if (ranked.Count == 0) return 0;

            ranked.SortByDescending(p => EconomyLedger.GetRawLabor(p));

            int baseAmount = Mathf.RoundToInt(Mathf.Max(0f, policy.amount));

            // 获奖人数可由玩家设定，上限按"奖金组人数"的 30%（组里还没选人则按全体人口）
            int groupCount = ranked.Count;
            int maxWin = policy.MaxRecipients(groupCount);
            int winCount = Mathf.Clamp(policy.recipients, 1, Mathf.Max(1, Mathf.Min(maxWin, ranked.Count)));
            int total = 0;

            for (int i = 0; i < ranked.Count && i < winCount; i++)
            {
                // 第 i 名拿 100%，依次线性降到约 40%（3 人时就是 100/80/60）
                float share = 1f - 0.6f * i / Mathf.Max(1, winCount);
                int amount = Mathf.RoundToInt(baseAmount * share);
                if (amount <= 0) continue;
                int available = EconomyBank.ColonySilver(map);
                if (available <= 0) break;
                int pay = Mathf.Min(amount, available);
                int taken = EconomyBank.SplitSilverToPrivate(map, ranked[i], pay);
                if (taken <= 0) continue;

                EconomyLedger.AddIncome(ranked[i], taken);
                GainMemory(ranked[i], BonusPolicy.GotBonusThought);

                // 心情之外，同时进工作满意度：先清掉上一期的奖金效果，再挂新的
                EconomySatisfactionEvents.RemoveByPrefix(ranked[i], BonusPrefix);
                EconomySatisfactionEvents.Add(ranked[i], BonusPrefix + "第" + (i + 1) + "名", 8f, 3);
                total += taken;
            }

            // 排名效率加成：从倒数第 4 名起算 +1%，排名每往上一位 +1%，最高 +10%
            // （倒数第 1~3 名没有加成，往后越靠前越高）
            MapEconomyComponent comp = map.GetComponent<MapEconomyComponent>();
            if (comp != null)
            {
                comp.ResetBonusSpeed();
                for (int i = 0; i < ranked.Count; i++)
                {
                    int fromBottom = ranked.Count - i;          // 最后一名 = 1
                    if (fromBottom < 4) continue;               // 倒数第 1~3 名不给加成
                    float pct = Mathf.Min(10f, (fromBottom - 3) * 1f);
                    comp.SetBonusSpeed(ranked[i], pct / 100f);
                }
            }

            // 末尾同样按人数扣满意度：−2 × 剩余名次，最重 −6
            int loseCount = Mathf.Min(winCount, ranked.Count - winCount);
            for (int i = 0; i < loseCount; i++)
            {
                Pawn p = ranked[ranked.Count - 1 - i];
                float penalty = -Mathf.Min(6f, 2f * (winCount - i));
                EconomySatisfactionEvents.RemoveByPrefix(p, BonusPrefix);
                EconomySatisfactionEvents.Add(p, BonusPrefix + "倒数第" + (i + 1), penalty, 3);
                GainMemory(p, BonusPolicy.NoBonusThought);
            }

            return total;
        }

        private static void GainMemory(Pawn p, string defName)
        {
            if (p == null || p.needs == null || p.needs.mood == null) return;
            ThoughtDef def = DefDatabase<ThoughtDef>.GetNamedSilentFail(defName);
            if (def != null) p.needs.mood.thoughts.memories.TryGainMemory(def);
        }
    }
}
