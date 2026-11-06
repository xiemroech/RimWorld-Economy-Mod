using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>存款：每人有一笔" earmark 的钱"和一件想买的目标物，钱不够目标价时按比例存起来，不花在零食上。</summary>
    public class EconomySavings : GameComponent
    {
        public const float SaveRate = 0.35f;      // 多余现金里存起来的比例
        public const int MinFreeCash = 60;        // 至少要留这么多随时可花
        public const int ReserveSilver = 150;     // 应急金底线：跟"有没有贵东西"无关

        private List<Pawn> pawns = new List<Pawn>();
        private List<int> saved = new List<int>();
        private List<ThingDef> goals = new List<ThingDef>();
        private List<int> stamps = new List<int>();

        public EconomySavings(Game game) { }

        public static EconomySavings Instance
        {
            get
            {
                if (Current.Game == null) return null;
                return Current.Game.GetComponent<EconomySavings>();
            }
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pawns, "dsh_savPawns", LookMode.Reference);
            Scribe_Collections.Look(ref saved, "dsh_savAmounts", LookMode.Value);
            Scribe_Collections.Look(ref goals, "dsh_savGoals", LookMode.Def);
            Scribe_Collections.Look(ref stamps, "dsh_savStamps", LookMode.Value);
            if (Scribe.mode == LoadSaveMode.PostLoadInit) Prune();
        }

        private void Prune()
        {
            if (pawns == null) pawns = new List<Pawn>();
            if (saved == null) saved = new List<int>();
            if (goals == null) goals = new List<ThingDef>();
            if (stamps == null) stamps = new List<int>();
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                if (i >= saved.Count || i >= goals.Count || i >= stamps.Count || pawns[i] == null || pawns[i].Dead)
                {
                    pawns.RemoveAt(i);
                    if (i < saved.Count) saved.RemoveAt(i);
                    if (i < goals.Count) goals.RemoveAt(i);
                    if (i < stamps.Count) stamps.RemoveAt(i);
                }
            }
        }

        private int IndexOf(Pawn p)
        {
            if (p == null) return -1;
            if (pawns == null) return -1;
            return pawns.IndexOf(p);
        }

        private int Ensure(Pawn p)
        {
            int i = IndexOf(p);
            if (i >= 0) return i;
            pawns.Add(p); saved.Add(0); goals.Add(null); stamps.Add(0);
            return pawns.Count - 1;
        }

        public static int Of(Pawn p)
        {
            var c = Instance;
            if (c == null) return 0;
            int i = c.IndexOf(p);
            return i < 0 ? 0 : c.saved[i];
        }

        public static ThingDef Goal(Pawn p)
        {
            var c = Instance;
            if (c == null) return null;
            int i = c.IndexOf(p);
            return i < 0 ? null : c.goals[i];
        }

        public static void SetGoal(Pawn p, ThingDef def)
        {
            var c = Instance;
            if (c == null) return;
            int i = c.Ensure(p);
            c.goals[i] = def;
            if (def == null) c.saved[i] = 0;
        }

        public static void Deposit(Pawn p, int amount)
        {
            var c = Instance;
            if (c == null || amount <= 0) return;
            int i = c.Ensure(p);
            c.saved[i] += amount;
        }

        public static void Take(Pawn p, int amount)
        {
            var c = Instance;
            if (c == null || amount <= 0) return;
            int i = c.IndexOf(p);
            if (i < 0) return;
            c.saved[i] = System.Math.Max(0, c.saved[i] - amount);
        }

        public static void ResetRuntime()
        {
            var c = Instance;
            if (c == null) return;
            c.pawns.Clear(); c.saved.Clear(); c.goals.Clear(); c.stamps.Clear();
        }

        /// <summary>每小时跑一次：挑目标、存钱。</summary>
        public static void Accrue(Pawn p, Map map)
        {
            var c = Instance;
            if (c == null || p == null || map == null || !p.IsColonist) return;

            ThingDef goal = Goal(p);
            if (goal == null || (goal.IsApparel && EconomyItemStock.CountOwned(p, goal) > 0))
            {
                ThingDef pick = PickGoal(p, map);
                if (pick != goal)
                {
                    SetGoal(p, pick);
                    goal = pick;
                }
            }
            int target = TargetOf(p, map);
            if (target <= 0) return;

            int silver = EconomyBank.PrivateSilver(map, p);

            // 现金变少时，存款下限跟着降，绝不能把口袋掏空（否则饿死）
            int keepCash = System.Math.Max(30, MinFreeCash / 2);
            if (Of(p) > silver - keepCash)
            {
                int cut = Of(p) - System.Math.Max(0, silver - keepCash);
                if (cut > 0) Take(p, cut);
            }

            int free = silver - System.Math.Max(MinFreeCash, EconomyPocketMoney.PocketLimit(map) * 2) - Of(p);
            if (free <= 0) return;
            int need = target - Of(p);
            if (need <= 0) return;
            int room = System.Math.Max(0, silver - keepCash - Of(p));
            int add = UnityEngine.Mathf.Clamp(UnityEngine.Mathf.RoundToInt(free * SaveRate), 0, System.Math.Min(need, room));
            Deposit(p, add);
        }

        /// <summary>存款目标：应急金 150 与"想买的大件价格"取大者；没有大件时至少存 150。</summary>
        public static int TargetOf(Pawn p, Map map)
        {
            int reserve = ReserveSilver;
            ThingDef g = Goal(p);
            int price = g != null ? GoalPrice(g, map) : 0;
            return UnityEngine.Mathf.Max(reserve, price);
        }

        /// <summary>购买计划的一行文字（给界面 tooltip / 明细窗口用）。</summary>
        public static string PlanText(Pawn p, Map map)
        {
            if (p == null || map == null) return "无";
            List<string> parts = new List<string>();

            // 当下必要：吃的
            float food = EconomyConsumption.StoredNutrition(p, map, false);
            parts.Add("食物 " + food.ToString("0.0") + "/" + EconomyConsumption.FoodTargetNutrition(p, map).ToString("0.0") + " 营养");

            // 当下必要：穿的
            int apparel = CountCategory(p, "DshEco_Apparel");
            parts.Add("衣物 " + apparel + "/3 件");

            ThingDef g = Goal(p);
            if (g != null)
            {
                int price = GoalPrice(g, map);
                int cash = EconomyBank.PrivateSilver(map, p);
                string goal = "目标 " + g.LabelCap + "（存款 " + Of(p) + "/" + price + " 银）";
                goal += cash >= price ? "· 钱够了，下次冲动就买" : "· 还差 " + (price - cash) + " 银";
                parts.Add(goal);
            }
            else
            {
                parts.Add("无大件目标（仓库里没有值得攒的东西），只留应急金 " + Of(p) + "/" + ReserveSilver + " 银");
            }
            return string.Join("\n", parts.ToArray());
        }

        public static bool SavingForGoal(Pawn p, Map map)
        {
            int target = TargetOf(p, map);
            return target > 0 && Of(p) < target;
        }

        public static int GoalPrice(ThingDef def, Map map)
        {
            if (def == null) return 0;
            return UnityEngine.Mathf.CeilToInt(EconomyPrice.UnitPrice(def, map));
        }

        /// <summary>当前最想买的那件：没武器→武器，衣物不足→衣物。</summary>
        public static ThingDef PickGoal(Pawn p, Map map)
        {
            if (p == null || map == null) return null;
            if (weaponMissing(p, map)) return CheapestIn("DshEco_Weapon", map);
            if (CountCategory(p, "DshEco_Apparel") < 3) return CheapestIn("DshEco_Apparel", map);
            return null;
        }

        private static bool weaponMissing(Pawn p, Map map)
        {
            return CountCategory(p, "DshEco_Weapon") <= 0;
        }

        private static int CountCategory(Pawn p, string categoryDefName)
        {
            EconomyCategoryDef cat = null;
            foreach (EconomyCategoryDef c in EconomyRegistry.Categories)
            {
                if (c != null && c.defName == categoryDefName) { cat = c; break; }
            }
            if (cat == null) return 0;
            int n = 0;
            foreach (ThingDef d in EconomyRegistry.ItemsIn(cat))
            {
                if (d == null) continue;
                n += EconomyItemStock.CountOwned(p, d);
            }
            return n;
        }

        private static Map availMap;
        private static int availTick = -99999;
        private static readonly HashSet<ThingDef> avail = new HashSet<ThingDef>();

        /// <summary>殖民地里实际存在（仓库/市场/地上）的物品定义，每小时重建一次。</summary>
        private static bool Available(ThingDef d, Map map)
        {
            if (map == null || d == null) return false;
            int now = Find.TickManager.TicksGame;
            if (availMap != map || now - availTick >= 2500)
            {
                availMap = map; availTick = now; avail.Clear();
                foreach (Thing t in map.listerThings.AllThings)
                {
                    if (t == null || t.Destroyed) continue;
                    avail.Add(t.def);
                }
            }
            return avail.Contains(d);
        }

        private static ThingDef CheapestIn(string category, Map map)
        {
            ThingDef best = null;
            float bestPrice = float.MaxValue;
            EconomyCategoryDef cat = null;
            foreach (EconomyCategoryDef c in EconomyRegistry.Categories)
            {
                if (c != null && c.defName == category) { cat = c; break; }
            }
            if (cat == null) return null;
            foreach (ThingDef d in EconomyRegistry.ItemsIn(cat))
            {
                if (d == null) continue;
                if (EconomyConsumption.IsImplantLike(d)) continue;
                if (d.BaseMarketValue <= 0.1f) continue;
                float price = EconomyPrice.UnitPrice(d, map);
                if (price < 30f || price > 4000f) continue;      // 太便宜的不值得当"目标大件"
                if (!Available(d, map)) continue;                 // 殖民地里根本没有的，不算目标
                if (price < bestPrice) { bestPrice = price; best = d; }
            }
            return best;
        }
    }
}
