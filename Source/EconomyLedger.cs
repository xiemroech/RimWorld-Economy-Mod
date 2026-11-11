using System.Collections.Generic;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 个人账本：白银余额、累计劳动量、工作满意度。
    /// 做成 GameComponent 而不是 Pawn 的 Comp，旧存档的小人也能直接记账。
    /// </summary>
    public class EconomyLedger : GameComponent
    {
        public static EconomyLedger Instance;

        private List<Pawn> pawns = new List<Pawn>();
        private List<int> silver = new List<int>();
        private List<float> labor = new List<float>();
        private List<float> laborRaw = new List<float>();
        private List<float> satisfaction = new List<float>();
        private List<float> income = new List<float>();
        private List<float> incomeLast = new List<float>();
        private List<float> expectedLast = new List<float>();
        private List<float> shareCut = new List<float>();
        private List<float> capCut = new List<float>();
        private List<float> unpaidCut = new List<float>();

        public const float DefaultSatisfaction = 50f;

        public EconomyLedger(Game game)
        {
            Instance = this;
        }

        /// <summary>优先用静态实例；万一没设上，从当前存档里捞，避免"钱扣了但没到账"。</summary>
        private static EconomyLedger Inst
        {
            get
            {
                if (Instance != null) return Instance;
                if (Current.Game != null) Instance = Current.Game.GetComponent<EconomyLedger>();
                return Instance;
            }
        }

        public override void FinalizeInit()
        {
            base.FinalizeInit();
            Instance = this;
            Sanitize();
            Prune();
        }

        private void Sanitize()
        {
            if (pawns == null) pawns = new List<Pawn>();
            if (silver == null) silver = new List<int>();
            if (labor == null) labor = new List<float>();
            if (laborRaw == null) laborRaw = new List<float>();
            if (satisfaction == null) satisfaction = new List<float>();
            if (income == null) income = new List<float>();
            if (incomeLast == null) incomeLast = new List<float>();
            if (expectedLast == null) expectedLast = new List<float>();
            if (shareCut == null) shareCut = new List<float>();
            if (capCut == null) capCut = new List<float>();
            if (unpaidCut == null) unpaidCut = new List<float>();
            while (silver.Count < pawns.Count) silver.Add(0);
            while (labor.Count < pawns.Count) labor.Add(0f);
            while (laborRaw.Count < pawns.Count) laborRaw.Add(0f);
            while (satisfaction.Count < pawns.Count) satisfaction.Add(DefaultSatisfaction);
            while (income.Count < pawns.Count) income.Add(0f);
            while (incomeLast.Count < pawns.Count) incomeLast.Add(0f);
            while (expectedLast.Count < pawns.Count) expectedLast.Add(0f);
            while (shareCut.Count < pawns.Count) shareCut.Add(0f);
            while (capCut.Count < pawns.Count) capCut.Add(0f);
            while (unpaidCut.Count < pawns.Count) unpaidCut.Add(0f);
            if (silver.Count > pawns.Count) silver.RemoveRange(pawns.Count, silver.Count - pawns.Count);
            if (labor.Count > pawns.Count) labor.RemoveRange(pawns.Count, labor.Count - pawns.Count);
            if (laborRaw.Count > pawns.Count) laborRaw.RemoveRange(pawns.Count, laborRaw.Count - pawns.Count);
            if (satisfaction.Count > pawns.Count) satisfaction.RemoveRange(pawns.Count, satisfaction.Count - pawns.Count);
            if (income.Count > pawns.Count) income.RemoveRange(pawns.Count, income.Count - pawns.Count);
            if (incomeLast.Count > pawns.Count) incomeLast.RemoveRange(pawns.Count, incomeLast.Count - pawns.Count);
            if (expectedLast.Count > pawns.Count) expectedLast.RemoveRange(pawns.Count, expectedLast.Count - pawns.Count);
            if (shareCut.Count > pawns.Count) shareCut.RemoveRange(pawns.Count, shareCut.Count - pawns.Count);
            if (capCut.Count > pawns.Count) capCut.RemoveRange(pawns.Count, capCut.Count - pawns.Count);
            if (unpaidCut.Count > pawns.Count) unpaidCut.RemoveRange(pawns.Count, unpaidCut.Count - pawns.Count);
        }

        private void Prune()
        {
            Sanitize();
            for (int i = pawns.Count - 1; i >= 0; i--)
            {
                if (pawns[i] == null || pawns[i].Destroyed)
                {
                    pawns.RemoveAt(i);
                    silver.RemoveAt(i);
                    labor.RemoveAt(i);
                    laborRaw.RemoveAt(i);
                    satisfaction.RemoveAt(i);
                    income.RemoveAt(i);
                    incomeLast.RemoveAt(i);
                    expectedLast.RemoveAt(i);
                    shareCut.RemoveAt(i);
                    capCut.RemoveAt(i);
                    unpaidCut.RemoveAt(i);
                }
            }
        }

        private int IndexOf(Pawn pawn)
        {
            if (pawn == null) return -1;
            for (int i = 0; i < pawns.Count; i++)
            {
                if (pawns[i] == pawn) return i;
            }
            return -1;
        }

        private int EnsureIndex(Pawn pawn)
        {
            int i = IndexOf(pawn);
            if (i >= 0) return i;
            pawns.Add(pawn);
            silver.Add(0);
            labor.Add(0f);
            laborRaw.Add(0f);
            satisfaction.Add(DefaultSatisfaction);
            income.Add(0f);
            incomeLast.Add(0f);
            expectedLast.Add(0f);
            shareCut.Add(0f);
            capCut.Add(0f);
            unpaidCut.Add(0f);
            return pawns.Count - 1;
        }

        public static int Get(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return 0;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.silver[i] : 0;
        }

        public static void Add(Pawn pawn, int amount)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null || amount <= 0) return;
            int i = inst.EnsureIndex(pawn);
            inst.silver[i] = UnityEngine.Mathf.Max(0, inst.silver[i] + amount);
        }

        public static int Take(Pawn pawn, int amount)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null || amount <= 0) return 0;
            int i = inst.IndexOf(pawn);
            if (i < 0) return 0;
            int have = inst.silver[i];
            int taken = amount < have ? amount : have;
            inst.silver[i] = have - taken;
            return taken;
        }

        public static float GetLabor(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return 0f;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.labor[i] : 0f;
        }

        public static void AddLabor(Pawn pawn, float amount)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null || amount <= 0f) return;
            int i = inst.EnsureIndex(pawn);
            inst.labor[i] += amount;
        }

        /// <summary>加"实际工时"（不加权），只用于奖金排名。</summary>
        public static void AddRawLabor(Pawn pawn, float amount)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null || amount <= 0f) return;
            int i = inst.EnsureIndex(pawn);
            inst.laborRaw[i] += amount;
        }

        /// <summary>实际工时（未加权），奖金排名用。</summary>
        public static float GetRawLabor(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return 0f;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.laborRaw[i] : 0f;
        }

        public static void ConsumeRawLabor(Pawn pawn, float amount)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null || amount <= 0f) return;
            int i = inst.IndexOf(pawn);
            if (i < 0) return;
            inst.laborRaw[i] = UnityEngine.Mathf.Max(0f, inst.laborRaw[i] - amount);
        }

        public static void ConsumeLabor(Pawn pawn, float amount)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null || amount <= 0f) return;
            int i = inst.IndexOf(pawn);
            if (i < 0) return;
            inst.labor[i] = UnityEngine.Mathf.Max(0f, inst.labor[i] - amount);
        }

        public static float GetSatisfaction(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return DefaultSatisfaction;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.satisfaction[i] : DefaultSatisfaction;
        }

        public static void AddSatisfaction(Pawn pawn, float delta)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return;
            int i = inst.EnsureIndex(pawn);
            inst.satisfaction[i] = UnityEngine.Mathf.Clamp(inst.satisfaction[i] + delta, 0f, 100f);
            Need_Satisfaction.Sync(pawn);
        }

        public static int TotalSilver(IEnumerable<Pawn> list)
        {
            int total = 0;
            if (list == null) return 0;
            foreach (Pawn p in list) total += Get(p);
            return total;
        }

        /// <summary>调试用：清空所有人的劳动量，并把满意度复位。</summary>
        public static void ResetAll()
        {
            EconomyLedger inst = Inst;
            if (inst == null) return;
            for (int i = 0; i < inst.labor.Count; i++) inst.labor[i] = 0f;
            for (int i = 0; i < inst.satisfaction.Count; i++) inst.satisfaction[i] = DefaultSatisfaction;
            for (int i = 0; i < inst.income.Count; i++) inst.income[i] = 0f;
        }

        /// <summary>记一笔本期收入（工资、奖金），用于所得税。</summary>
        public static void AddIncome(Pawn pawn, float amount)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null || amount <= 0f) return;
            int i = inst.EnsureIndex(pawn);
            inst.income[i] += amount;
        }

        public static float GetIncome(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return 0f;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.income[i] : 0f;
        }

        public static void ClearIncome(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return;
            int i = inst.IndexOf(pawn);
            if (i >= 0)
            {
                inst.incomeLast[i] = inst.income[i];
                inst.income[i] = 0f;
            }
        }

        /// <summary>上一期的收入（发薪后快照），用于界面展示收入差距。</summary>
        public static float GetLastIncome(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return 0f;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.incomeLast[i] : 0f;
        }

        /// <summary>上一期的"按工作权重应有的工资"，用来和实发对比。</summary>
        public static void SetLastExpected(Pawn pawn, float amount)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return;
            int i = inst.EnsureIndex(pawn);
            inst.expectedLast[i] = amount;
        }

        public static float GetLastExpected(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return 0f;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.expectedLast[i] : 0f;
        }

        /// <summary>
        /// 记录上一期工资被拆走的三个部分（都是白银）：
        ///   shareCut  = 工资比例抽走的剩余价值
        ///   capCut    = 日薪上限截留的部分（相对剩余）
        ///   unpaidCut = 该发但国库没钱发的部分
        /// 满意度会分别对这三项计算扣分，而不是揉成一个比值。
        /// </summary>
        public static void SetPayCuts(Pawn pawn, float shareCutValue, float capCutValue, float unpaidCutValue)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return;
            int i = inst.EnsureIndex(pawn);
            inst.shareCut[i] = shareCutValue;
            inst.capCut[i] = capCutValue;
            inst.unpaidCut[i] = unpaidCutValue;
        }

        public static float GetShareCut(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return 0f;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.shareCut[i] : 0f;
        }

        public static float GetCapCut(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return 0f;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.capCut[i] : 0f;
        }

        public static float GetUnpaidCut(Pawn pawn)
        {
            EconomyLedger inst = Inst;
            if (inst == null || pawn == null) return 0f;
            int i = inst.IndexOf(pawn);
            return i >= 0 ? inst.unpaidCut[i] : 0f;
        }

        /// <summary>调试用：统计劳动总量。</summary>
        public static float TotalLabor(IEnumerable<Pawn> list)
        {
            float total = 0f;
            if (list == null) return 0f;
            foreach (Pawn p in list) total += GetLabor(p);
            return total;
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Collections.Look(ref pawns, "dsh_silverPawns", LookMode.Reference);
            Scribe_Collections.Look(ref silver, "dsh_silverValues", LookMode.Value);
            Scribe_Collections.Look(ref labor, "dsh_laborValues", LookMode.Value);
            // 未加权劳动（真实工作时间）：日薪上限按它算，不存就会在读档后变成 0，
            // 于是「上限截留」把整期工资吃光，只发出 1 银。
            Scribe_Collections.Look(ref laborRaw, "dsh_rawLaborValues", LookMode.Value);
            Scribe_Collections.Look(ref satisfaction, "dsh_satisfaction", LookMode.Value);
            Scribe_Collections.Look(ref income, "dsh_income", LookMode.Value);
            Scribe_Collections.Look(ref incomeLast, "dsh_incomeLast", LookMode.Value);
            Scribe_Collections.Look(ref expectedLast, "dsh_expectedLast", LookMode.Value);
            Scribe_Collections.Look(ref shareCut, "dsh_shareCut", LookMode.Value);
            Scribe_Collections.Look(ref capCut, "dsh_capCut", LookMode.Value);
            Scribe_Collections.Look(ref unpaidCut, "dsh_unpaidCut", LookMode.Value);
            Sanitize();
        }
    }
}
