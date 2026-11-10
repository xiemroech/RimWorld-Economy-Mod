using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 仓库补货：按建筑的补货设置，把货从殖民地其它地方运进来。
    /// 默认模式 = 把空格补满；细化模式 = 按每种物品的下限/上限补。
    /// </summary>
    public static class EconomyRestock
    {
        public const int Interval = 1000;          // 约 0.4 游戏小时检查一次
        public const int MaxJobsPerPass = 3;       // 每次最多派几个搬运任务，别抢光劳动力
        public const int DefaultFillStacks = 2;    // 默认模式下每个仓库每次最多补几堆

        private static int lastTick;

        // 本轮已经派过活的人：不重复派，否则同一个人会被连派好几单，
        // 后一单把前一单顶掉（InterruptForced），永远搬不完。
        private static readonly HashSet<int> assignedPawns = new HashSet<int>();

        // 派出去的补货单：记下 job.loadID，任务结束时打一行"这单怎么结束的"。
        // 否则任务被静默打断/失败时，日志里什么都看不到。
        private static readonly Dictionary<int, string> watchJobs = new Dictionary<int, string>();

        public static void Watch(Job job, string what)
        {
            if (job == null || watchJobs.Count > 200) return;
            watchJobs[job.loadID] = what;
        }

        public static void NoteEnd(Job job, JobCondition condition, Pawn p)
        {
            if (job == null) return;
            string what;
            if (!watchJobs.TryGetValue(job.loadID, out what)) return;
            watchJobs.Remove(job.loadID);
            DevLog.Msg("[RESTOCK] 这单结束：" + (p != null ? p.LabelShort : "?")
                + " " + what + " 原因=" + condition);
        }

        public const float MealsTarget = 6f;          // 料斗里保证 6 顿饭的营养
        public const float NutritionPerMeal = 0.9f;

        public static void Tick(Map map)
        {
            // 料斗补货已按用户要求移除（没有应用到实际玩法，且会和 [PUB] 的收回逻辑互相拉扯）。
            if (map == null) return;
            int now = Find.TickManager.TicksGame;
            if (now - lastTick < Interval) return;
            lastTick = now;
            assignedPawns.Clear();

            int jobs = 0;
            List<Building_Storage> stores = map.listerBuildings.AllBuildingsColonistOfClass<Building_Storage>().ToList();
            System.Text.StringBuilder scan = new System.Text.StringBuilder("[RESTOCK] 轮询：本图仓库 " + stores.Count + " 个 →");
            for (int i = 0; i < stores.Count; i++)
            {
                Building_Storage s0 = stores[i];
                if (s0 == null) continue;
                RestockSettings c0 = RestockStore.Lookup(s0);
                scan.Append(" [").Append(s0.LabelShort)
                    .Append(" 归属=").Append(EconomyRealEstate.OwnerOfBuilding(s0) != null ? "私有" : "公共")
                    .Append(" 配置=").Append(c0 == null ? "无" : (c0.detailed ? "细化" : "默认"))
                    .Append(" 规则=").Append(c0 == null || c0.rules == null ? 0 : c0.rules.Count)
                    .Append("]");
                if (c0 != null && c0.rules != null)
                {
                    for (int k = 0; k < c0.rules.Count; k++)
                    {
                        RestockRule r0 = c0.rules[k];
                        if (r0 == null) { scan.Append(" {空规则}"); continue; }
                        scan.Append(" {").Append(r0.def != null ? r0.def.defName : "def空")
                            .Append(" 占比=").Append(r0.pct)
                            .Append(" 定额=").Append(r0.exactItems)
                            .Append(" 目标=").Append(RestockStore.TargetItems(s0, r0))
                            .Append(" 现有=").Append(r0.def != null ? CountIn(s0, r0.def) : 0)
                            .Append("}");
                    }
                }
            }
            DevLog.Msg(scan.ToString());

            // 先处理"配了细化规则"的仓库，再处理只求填满的仓库。
            // 否则前一个 FillEmpty 一次就把 MaxJobsPerPass 用光，细化仓库永远轮不到（这就是之前一直不生效的原因）。
            List<Building_Storage> ordered = new List<Building_Storage>();
            List<Building_Storage> plain = new List<Building_Storage>();
            for (int i = 0; i < stores.Count; i++)
            {
                Building_Storage s1 = stores[i];
                if (s1 == null) continue;
                RestockSettings c1 = RestockStore.Lookup(s1);
                if (c1 != null && c1.detailed && c1.rules != null && c1.rules.Count > 0) ordered.Add(s1);
                else plain.Add(s1);
            }
            ordered.AddRange(plain);

            foreach (Building_Storage st in ordered)
            {
                if (jobs >= MaxJobsPerPass) break;
                if (st == null || st.Destroyed) continue;
                // 私有容器不参与公共补货：谁的东西谁自己搬（否则就是"公共货进私有箱"）
                if (EconomyRealEstate.OwnerOfBuilding(st) != null) continue;
                // 市场摊位也走补货：卖掉的货由搬运工从公共库存补回去

                RestockSettings comp = RestockStore.Lookup(st);
                if (comp == null) continue;

                if (comp.detailed && comp.rules.Count > 0)
                {
                    // 缺货时把仓库提到最高优先级：这样原版的手动指派/自动搬运也会先送这里，
                    // 而不是只靠我们插队派活（插队对"玩家手动点搬运"不起作用）。
                    bool needFill = DetailedNeedsFill(st, comp);
                    if (needFill || originalPriority.ContainsKey(st.thingIDNumber))
                    {
                        DevLog.Msg("[RESTOCK] 优先级判定：" + st.LabelShort + " 详细=True 规则=" + comp.rules.Count
                            + " 需要补=" + needFill);
                    }
                    EnsurePriority(st, comp, needFill);
                    jobs += RestockDetailed(map, st, comp, jobs);
                }
                else
                    jobs += FillEmpty(map, st, jobs);
            }
        }

        // ---------- 细化模式 ----------

        /// <summary>记录被我们临时改过优先级的仓库，以及它原来的优先级。</summary>
        private static readonly Dictionary<int, StoragePriority> originalPriority = new Dictionary<int, StoragePriority>();

        /// <summary>????????????? def??????????</summary>
        private static readonly Dictionary<int, List<ThingDef>> addedAllows = new Dictionary<int, List<ThingDef>>();

        /// <summary>细化模式下是否还有规则缺货（缺到"目标一半以下"就算）。</summary>
        private static bool DetailedNeedsFill(Building_Storage st, RestockSettings comp)
        {
            if (comp == null || comp.rules == null) return false;
            for (int i = 0; i < comp.rules.Count; i++)
            {
                RestockRule rule = comp.rules[i];
                if (rule == null || rule.def == null) continue;
                int target = RestockStore.TargetItems(st, rule);
                if (target <= 0) continue;
                if (CountIn(st, rule.def) < Mathf.Max(1, target / 2)) return true;
            }
            return false;
        }

        /// <summary>缺货时把优先级提到最高，不缺了还原。【过滤规则一律不动】</summary>
        private static void EnsurePriority(Building_Storage st, RestockSettings comp, bool needsFill)
        {
            try
            {
                EnsurePriorityInner(st, comp, needsFill);
            }
            catch (System.Exception e)
            {
                Log.Error("[RESTOCK] 优先级处理失败：" + e.GetType().Name + " " + e.Message + "\n" + e.StackTrace);
            }
        }

        private static void EnsurePriorityInner(Building_Storage st, RestockSettings comp, bool needsFill)
        {
            if (st == null || st.Destroyed) return;
            StorageSettings s = st.GetStoreSettings();
            if (s == null) return;
            int id = st.thingIDNumber;

            if (needsFill)
            {
                if (!originalPriority.ContainsKey(id))
                {
                    originalPriority[id] = s.Priority;
                    s.Priority = StoragePriority.Critical;
                    DevLog.Msg("[RESTOCK] 提优先级：" + st.LabelShort + " → 最高（原 " + originalPriority[id] + "）");
                }

                // 原版搬运只认"仓库过滤"，不认我们的补货规则：
                // 过滤里没这个 def，优先级再高也不会被选中 → 手动指派仍旧送去地窖。
                // 这里把规则里的 def 临时加进过滤，恢复时再撤掉（只动我们加的那几个）。
                List<ThingDef> added;
                if (!addedAllows.TryGetValue(id, out added))
                {
                    added = new List<ThingDef>();
                    addedAllows[id] = added;
                }
                int newly = 0;
                foreach (RestockRule rule in comp.rules)
                {
                    if (rule == null || rule.def == null) continue;
                    if (s.filter.Allows(rule.def)) continue;
                    s.filter.SetAllow(rule.def, true);
                    if (!added.Contains(rule.def)) { added.Add(rule.def); newly++; }
                }
                if (newly > 0)
                    DevLog.Msg("[RESTOCK] 过滤放行：" + st.LabelShort + " 新增允许 " + newly + " 种（共 " + added.Count + " 种由补货规则加入）");
            }
            else
            {
                StoragePriority orig;
                if (originalPriority.TryGetValue(id, out orig))
                {
                    s.Priority = orig;
                    originalPriority.Remove(id);
                    DevLog.Msg("[RESTOCK] 恢复优先级：" + st.LabelShort + " → " + orig);
                }
                List<ThingDef> added;
                if (addedAllows.TryGetValue(id, out added))
                {
                    for (int i = 0; i < added.Count; i++)
                    {
                        if (added[i] != null && s.filter.Allows(added[i])) s.filter.SetAllow(added[i], false);
                    }
                    addedAllows.Remove(id);
                    DevLog.Msg("[RESTOCK] 过滤收紧：" + st.LabelShort + " 撤掉 " + added.Count + " 种");
                }
            }
        }


        private static int RestockDetailed(Map map, Building_Storage st, RestockSettings comp, int already)
        {
            int jobs = 0;
            foreach (RestockRule rule in comp.rules)
            {
                if (jobs + already >= MaxJobsPerPass) break;
                if (rule == null || rule.def == null) continue;
                int target = RestockStore.TargetItems(st, rule);
                if (target <= 0) continue;
                int have = CountIn(st, rule.def);
                // 低于目标的一半才补，避免"掉一件就搬一次"
                if (have >= Mathf.Max(1, target / 2)) continue;
                int need = target - have;
                if (need <= 0) continue;

                Thing source = FindSource(map, st, rule.def);
                if (source == null) continue;

                Pawn pawn = FindHauler(map, source, st);
                if (pawn == null) continue;

                // mod 容器：容量与目标都在 inner ThingOwner 里，必须用 HaulToContainerJob，不能指单格
                ThingOwner innerBox = ContainerUtil.InnerOf(st);
                if (innerBox != null)
                {
                    if (ContainerUtil.CanAcceptCount(innerBox, rule.def) <= 0) continue;
                    Job jb = HaulAIUtility.HaulToContainerJob(pawn, source, st);
                    if (jb == null) continue;
                    int canTake = Mathf.Max(1, ContainerUtil.CanAcceptCount(innerBox, rule.def));
                    jb.count = Mathf.Clamp(Mathf.Min(need, canTake), 1, source.stackCount);
                    Watch(jb, "搬 " + rule.def.defName + " ×" + jb.count + " → " + st.LabelShort + "（容器）");
                    if (pawn.jobs.TryTakeOrderedJob(jb, JobTag.Misc)) { jobs++; assignedPawns.Add(pawn.thingIDNumber); }
                    else DevLog.Msg("[RESTOCK] 派活被拒（容器路径）：" + pawn.LabelShort + " → " + st.LabelShort
                        + " 当前=" + (pawn.CurJobDef != null ? pawn.CurJobDef.defName : "无"));
                    continue;
                }

                // 不能再用 HaulAIUtility.HaulToStorageJob：它内部走原版"只找比现在更好的格"，
                // 摊位和货源地常常同为最高优先级 → 永远不算更好 → 直接返回 null。
                // 这里自己挑一格：空着或能叠、可到达，然后直接下 HaulToCell。
                IntVec3 cell = FindCell(st, rule.def, pawn);
                if (!cell.IsValid)
                {
                    DevLog.Msg("[RESTOCK] 找不到格位：" + st.LabelShort + " → " + rule.def.defName);
                    continue;
                }
                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, source, cell);
                job.count = UnityEngine.Mathf.Clamp(need, 1, UnityEngine.Mathf.Min(source.stackCount, rule.def.stackLimit));
                job.haulMode = HaulMode.Undefined;
                Watch(job, "搬 " + rule.def.defName + " ×" + job.count + " → " + st.LabelShort + " @ " + cell);
                if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc))
                {
                    jobs++;
                    assignedPawns.Add(pawn.thingIDNumber);
                    DevLog.Msg("[RESTOCK] 已派活：" + pawn.LabelShort + " 搬 " + rule.def.defName
                        + " ×" + job.count + " → " + st.LabelShort + " @ " + job.targetB.Cell);
                }
                else
                {
                    DevLog.Msg("[RESTOCK] 派活被拒：" + pawn.LabelShort + " → " + st.LabelShort
                        + " 当前=" + (pawn.CurJobDef != null ? pawn.CurJobDef.defName : "无"));
                }
            }
            return jobs;
        }

        // ---------- 默认模式：补满空格 ----------

        /// <summary>一次性诊断：把这条补货规则卡在哪一步打到日志里。</summary>
        public static void Diagnose(Map map, Building_Storage st)
        {
            try
            {
                if (map == null || st == null) { DevLog.Msg("[RESTOCK] 诊断为空的仓库"); return; }
                RestockSettings comp = RestockStore.Lookup(st);
                DevLog.Msg("[RESTOCK] 诊断 " + st.LabelShort
                    + " 详细模式=" + (comp != null && comp.detailed)
                    + " 规则数=" + (comp != null ? comp.rules.Count : 0)
                    + " 归属=" + (EconomyRealEstate.OwnerOfBuilding(st) != null ? "私有" : "公共"));

                if (comp == null) { DevLog.Msg("[RESTOCK] 没有补货组件"); return; }

                for (int i = 0; i < comp.rules.Count; i++)
                {
                    RestockRule rule = comp.rules[i];
                    if (rule == null || rule.def == null) continue;
                    int target = RestockStore.TargetItems(st, rule);
                    int have = CountIn(st, rule.def);
                    Thing source = target > 0 ? FindSource(map, st, rule.def) : null;
                    Pawn hauler = source != null ? FindHauler(map, source, st) : null;
                    IntVec3 cell = st != null ? FindCell(st, rule.def, hauler) : IntVec3.Invalid;
                    DevLog.Msg("[RESTOCK]   " + rule.def.defName
                        + " 目标=" + target + " 现有=" + have
                        + " 需要补=" + (target - have)
                        + " 货源=" + (source != null ? source.LabelShort : "找不到")
                        + " 搬运工=" + (hauler != null ? hauler.LabelShort : "找不到")
                        + " 格位=" + (cell.IsValid ? cell.ToString() : "没有"));
                }
            }
            catch (System.Exception e)
            {
                Log.Warning("[RESTOCK] 诊断失败：" + e.Message);
            }
        }

        private static int FillEmpty(Map map, Building_Storage st, int already)
        {
            int jobs = 0;
            int tries = 0;

            // mod 容器：不看格子，直接补进去
            ThingOwner innerFill = ContainerUtil.InnerOf(st);
            if (innerFill != null)
            {
                foreach (Thing t in map.listerThings.AllThings)
                {
                    if (jobs + already >= MaxJobsPerPass || jobs >= DefaultFillStacks) break;
                    if (t == null || t.Destroyed || t.def == null || t.def.category != ThingCategory.Item) continue;
                    if (EconomyPrivate.OwnerOf(t) != null) continue;
                    if (InStorage(t, st)) continue;
                    if (t.IsForbidden(Faction.OfPlayer) || t.IsBurning()) continue;
                    if (EconomyRealEstate.OwnerOfBuilding(st) != null) break;
                    if (!ContainerUtil.CanAccept(innerFill, t)) continue;
                    Pawn hauler = FindHauler(map, t, st);
                    if (hauler == null) continue;
                    Job jb = HaulAIUtility.HaulToContainerJob(hauler, t, st);
                    if (jb == null) continue;
                    int canFill = Mathf.Max(1, ContainerUtil.CountCanAccept(innerFill, t));
                    jb.count = Mathf.Min(t.stackCount, canFill);
                    if (hauler.jobs.TryTakeOrderedJob(jb, JobTag.Misc)) { jobs++; assignedPawns.Add(hauler.thingIDNumber); }
                }
                return jobs;
            }

            foreach (IntVec3 cell in st.AllSlotCellsList())
            {
                if (jobs + already >= MaxJobsPerPass) break;
                if (jobs >= DefaultFillStacks) break;
                if (++tries > 40) break;
                if (cell.GetThingList(map).Count > 0) continue;

                Thing source = FindAnySourceFor(map, st, cell);
                if (source == null) continue;

                Pawn pawn = FindHauler(map, source, st);
                if (pawn == null) continue;
                if (!StoreUtility.IsGoodStoreCell(cell, map, source, pawn, pawn.Faction)) continue;

                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, source, cell);
                job.count = source.stackCount;
                job.haulMode = HaulMode.ToCellStorage;
                if (pawn.jobs.TryTakeOrderedJob(job, JobTag.Misc)) { jobs++; assignedPawns.Add(pawn.thingIDNumber); }
            }
            return jobs;
        }

        // ---------- 工具 ----------

        public static int CountIn(Building_Storage st, ThingDef def)
        {
            int n = 0;
            ThingOwner innerCount = ContainerUtil.InnerOf(st);
            if (innerCount != null)
            {
                try
                {
                    foreach (Thing t0 in innerCount)
                        if (t0 != null && !t0.Destroyed && t0.def == def) n += t0.stackCount;
                }
                catch { }
                return n;
            }
            SlotGroup group = st.GetSlotGroup();
            if (group == null) return 0;
            foreach (Thing t in group.HeldThings)
            {
                if (t != null && !t.Destroyed && t.def == def) n += t.stackCount;
            }
            return n;
        }

        private static Thing FindSource(Map map, Building_Storage st, ThingDef def)
        {
            foreach (Thing t in map.listerThings.ThingsOfDef(def))
            {
                if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue;
                if (InStorage(t, st)) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                if (t.IsBurning()) continue;
                return t;
            }
            return null;
        }

        private static Thing FindAnySourceFor(Map map, Building_Storage st, IntVec3 cell)
        {
            foreach (Thing t in map.listerThings.AllThings)
            {
                if (t == null || t.Destroyed || t.stackCount <= 0) continue;
                if (t.def.category != ThingCategory.Item) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue;
                if (InStorage(t, st)) continue;
                if (t.IsForbidden(Faction.OfPlayer) || t.IsBurning()) continue;
                if (!st.GetStoreSettings().AllowedToAccept(t)) continue;
                return t;
            }
            return null;
        }

        private static bool InStorage(Thing t, Building_Storage st)
        {
            SlotGroup g = t.GetSlotGroup();
            return g != null && g.parent == st;
        }


        // 料斗补货（RestockHoppers）已删除：
        // 它和 [PUB] 的"公共物品收回"逻辑会互相拉扯（补进去的公共食物又被判定为可回收），
        // 而且这套玩法用户并未实际启用。将来要恢复，从版本历史里找回即可。


        private static Thing FindFoodForHopper(Map map, Building hopper, float need)
        {
            Thing found = null;
            float best = float.MaxValue;
            foreach (Thing t in map.listerThings.ThingsInGroup(ThingRequestGroup.HaulableEver))
            {
                if (t == null || t.def == null || t.Destroyed) continue;
                if (!t.def.IsNutritionGivingIngestible) continue;
                if (t.IsForbidden(Faction.OfPlayer)) continue;
                if (EconomyPrivate.OwnerOf(t) != null) continue;   // 只搬公共食物
                if (t.stackCount <= 0) continue;
                float d = t.Position.DistanceToSquared(hopper.Position);
                if (d < best && map.reachability.CanReach(t.Position, hopper.Position, PathEndMode.Touch, TraverseMode.PassDoors, Danger.Some))
                {
                    best = d;
                    found = t;
                }
            }
            return found;
        }

        private static Pawn FindHaulerAny(Map map, Thing source)
        {
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p == null || p.Dead || p.Downed || p.InMentalState) continue;
                if (p.CurJobDef != null && p.CurJobDef != JobDefOf.Wait && p.CurJobDef != JobDefOf.Wait_Wander && p.CurJobDef != JobDefOf.GotoWander) continue;
                if (p.workSettings == null || !p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling)) continue;
                if (!p.CanReach(source, PathEndMode.ClosestTouch, Danger.Some)) continue;
                return p;
            }
            return null;
        }

        private static Pawn FindHauler(Map map, Thing source, Building_Storage st)
        {
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p == null || p.Downed || p.Dead) continue;
                if (p.InMentalState || p.InAggroMentalState) continue;
                if (assignedPawns.Contains(p.thingIDNumber)) continue;
                if (p.workSettings == null || !p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling)) continue;
                if (!p.CanReach(source, PathEndMode.ClosestTouch, Danger.Some)) continue;
                if (!p.CanReach(st, PathEndMode.Touch, Danger.Some)) continue;
                if (p.CurJobDef == JobDefOf.Ingest) continue;
                if (p.CurJobDef != null && !IsFreeJob(p.CurJobDef)) continue;
                return p;
            }

            // 第二遍：没有闲人时，允许从"正在搬运别处"的人里挑一个。
            // 否则只要地图上有活，补货永远排不上队 —— 表现就是"货一直往地窖搬，摊位不补"。
            foreach (Pawn p in map.mapPawns.FreeColonistsSpawned)
            {
                if (p == null || p.Downed || p.Dead) continue;
                if (p.InMentalState || p.InAggroMentalState || p.Drafted) continue;
                if (assignedPawns.Contains(p.thingIDNumber)) continue;
                // 手上正拿着东西的人不要插队：强行改任务会让他把手里的货丢在地上。
                if (p.carryTracker != null && p.carryTracker.CarriedThing != null) continue;
                if (p.workSettings == null || !p.workSettings.WorkIsActive(WorkTypeDefOf.Hauling)) continue;
                if (p.CurJobDef != JobDefOf.HaulToCell && p.CurJobDef != JobDefOf.HaulToContainer) continue;
                if (!p.CanReach(source, PathEndMode.ClosestTouch, Danger.Some)) continue;
                if (!p.CanReach(st, PathEndMode.Touch, Danger.Some)) continue;
                DevLog.Msg("[RESTOCK] 插队：" + p.LabelShort + " 正在搬 "
                    + (p.carryTracker != null && p.carryTracker.CarriedThing != null ? p.carryTracker.CarriedThing.LabelShort : "空手")
                    + "，改去补 " + st.LabelShort);
                return p;
            }
            return null;
        }

        private static bool IsFreeJob(JobDef def)
        {
            return def == JobDefOf.Wait
                || def == JobDefOf.Wait_Wander
                || def == JobDefOf.GotoWander
                || def == JobDefOf.Goto
                || def.joyKind != null;
        }

        private static IntVec3 FindCell(Building_Storage st, ThingDef def, Pawn p)
        {
            Map map = st.Map;
            IntVec3 best = IntVec3.Invalid;
            int bestCount = int.MaxValue;
            foreach (IntVec3 c in st.AllSlotCellsList())
            {
                List<Thing> things = c.GetThingList(map);
                bool stackable = false;
                int itemCount = 0;      // 只数"货"，不数这口仓库本身和地上的装饰物
                foreach (Thing t in things)
                {
                    if (t == null || t == st || t.def == null) continue;
                    if (t.def.category != ThingCategory.Item) continue;
                    itemCount++;
                    if (!stackable && t.def == def) stackable = true;
                }
                // 格子上有别的货、又不能叠 → 这格用不了
                if (!stackable && itemCount > 0) continue;
                // 这格堆数已满（maxItemsInCell，摊位=25/格）→ 收不下新的一堆
                int perCell = (st.def != null && st.def.building != null) ? st.def.building.maxItemsInCell : 1;
                if (!stackable && itemCount >= perCell) continue;
                if (stackable && itemCount >= perCell + 1) continue;
                if (!p.CanReach(c, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                if (!stackable && !StoreUtility.IsGoodStoreCell(c, map, ThingMaker.MakeThing(def), p, p.Faction)) continue;
                if (things.Count < bestCount)
                {
                    bestCount = things.Count;
                    best = c;
                }
            }

            // 第二遍（兜底）：上面的 IsGoodStoreCell 对"建筑本体占着自己格子"的仓库
            // （市场摊位、货架）经常一律返回 false，于是永远找不到格位。
            // 这里只按"这格有没有别种货"来挑，能叠或空着就可用。
            if (!best.IsValid)
            {
                foreach (IntVec3 c in st.AllSlotCellsList())
                {
                    List<Thing> things = c.GetThingList(map);
                    bool hasOther = false;
                    bool hasSame = false;
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing t = things[i];
                        if (t == null || t == st || t.def == null) continue;
                        if (t.def.category != ThingCategory.Item) continue;
                        if (t.def == def) hasSame = true; else hasOther = true;
                    }
                    if (hasOther && !hasSame) continue;
                    int cnt2 = 0;
                    for (int i = 0; i < things.Count; i++)
                    {
                        Thing t = things[i];
                        if (t != null && t != st && t.def != null && t.def.category == ThingCategory.Item) cnt2++;
                    }
                    int cap2 = (st.def != null && st.def.building != null) ? st.def.building.maxItemsInCell : 1;
                    if (!hasSame && cnt2 >= cap2) continue;
                    if (!p.CanReach(c, PathEndMode.ClosestTouch, Danger.Deadly)) continue;
                    best = c;
                    break;
                }
            }
            return best;
        }
    }
}

