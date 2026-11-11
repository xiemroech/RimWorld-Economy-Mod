using System;
using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 记录每个小人"最近一次被派活"时的工作类型。
    /// JobDef 本身不携带 WorkType，只能在 WorkGiver 产出 Job 的那一刻记录。
    /// 同时记住当时的 JobDef：如果小人现在做的不是那个 Job（比如玩家强制指派、
    /// 发狂、征召），就当作"非工作来源"，权重按 1.0 计。
    /// </summary>
    public static class EconomyWorkTypes
    {
        private struct Entry
        {
            public JobDef job;
            public WorkTypeDef workType;
        }

        private static readonly Dictionary<Pawn, Entry> tracked = new Dictionary<Pawn, Entry>();

        /// <summary>读档/换图时清掉旧 Pawn 记录。</summary>
        public static void ResetRuntime() { tracked.Clear(); }

        // ---------------- 存档 ----------------
        // tracked 只活在内存里，读档就空 → 小人的活没有工作类型 →
        // 工资/产出统计按"非工作来源"权重 1.0 算，和存档前对不上。
        // 所以把整张表（人 / JobDef / 工作类型）一起写进存档。

        private static List<Pawn> scPawns = new List<Pawn>();
        private static List<JobDef> scJobs = new List<JobDef>();
        private static List<WorkTypeDef> scTypes = new List<WorkTypeDef>();

        public static void ExposeData()
        {
            if (Scribe.mode == LoadSaveMode.Saving)
            {
                scPawns.Clear();
                scJobs.Clear();
                scTypes.Clear();
                foreach (KeyValuePair<Pawn, Entry> kv in tracked)
                {
                    if (kv.Key == null) continue;
                    scPawns.Add(kv.Key);
                    scJobs.Add(kv.Value.job);
                    scTypes.Add(kv.Value.workType);
                }
            }

            Scribe_Collections.Look(ref scPawns, "dsh_wtPawns", LookMode.Reference);
            Scribe_Collections.Look(ref scJobs, "dsh_wtJobs", LookMode.Def);
            Scribe_Collections.Look(ref scTypes, "dsh_wtTypes", LookMode.Def);

            if (scPawns == null) scPawns = new List<Pawn>();
            if (scJobs == null) scJobs = new List<JobDef>();
            if (scTypes == null) scTypes = new List<WorkTypeDef>();

            if (Scribe.mode == LoadSaveMode.LoadingVars)
            {
                tracked.Clear();
                int n = scPawns.Count;
                for (int i = 0; i < n; i++)
                {
                    Pawn p = scPawns[i];
                    if (p == null) continue;
                    tracked[p] = new Entry
                    {
                        job = i < scJobs.Count ? scJobs[i] : null,
                        workType = i < scTypes.Count ? scTypes[i] : null
                    };
                }
            }
        }

        public static void Record(Pawn pawn, JobDef job, WorkTypeDef workType)
        {
            if (pawn == null || workType == null || job == null) return;
            tracked[pawn] = new Entry { job = job, workType = workType };
        }

        /// <summary>当前 Job 若是工作派发器给的，返回它的工作类型；否则返回 null。</summary>
        public static WorkTypeDef Of(Pawn pawn)
        {
            if (pawn == null) return null;

            Entry e;
            if (!tracked.TryGetValue(pawn, out e)) return null;

            Job cur = pawn.CurJob;
            if (cur == null || cur.def != e.job) return null;   // 不是同一份工作 → 不算工作来源
            return e.workType;
        }

        public static bool Patch(Harmony harmony)
        {
            bool any = false;
            try
            {
                var onThing = AccessTools.Method(typeof(WorkGiver_Scanner), "JobOnThing");
                if (onThing != null)
                {
                    harmony.Patch(onThing, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyWorkTypes), "Postfix")));
                    any = true;
                }

                var onCell = AccessTools.Method(typeof(WorkGiver_Scanner), "JobOnCell");
                if (onCell != null)
                {
                    harmony.Patch(onCell, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyWorkTypes), "Postfix")));
                    any = true;
                }

                var nonScan = AccessTools.Method(typeof(WorkGiver), "NonScanJob");
                if (nonScan != null)
                {
                    harmony.Patch(nonScan, postfix: new HarmonyMethod(AccessTools.Method(typeof(EconomyWorkTypes), "Postfix")));
                    any = true;
                }

                if (!any) Log.Warning("[DSH-ECONOMY] 没有找到任何 WorkGiver 产出 Job 的方法");
                return any;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 工作类型补丁失败：" + e.Message);
                return false;
            }
        }

        public static void Postfix(WorkGiver __instance, Pawn pawn, ref Job __result)
        {
            if (__result == null || __instance == null || __instance.def == null) return;
            Record(pawn, __result.def, __instance.def.workType);
        }
    }
}
