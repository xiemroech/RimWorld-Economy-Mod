using System.Collections.Generic;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 公共装备（无主的武器/衣物）不该长期挂在某个人身上：
    /// 不征召、不在战斗/精神状态下时，把它放回公共仓库；仓库放不下就丢在脚下。
    /// 私有装备（市场买的、有物主）不碰。
    /// </summary>
    public static class EconomyPublicGear
    {
        public const int Interval = 2500;      // 1 游戏小时
        private static int lastTick = -99999;

        public static void ResetRuntime()
        {
            lastTick = -99999;
        }

        public static void Tick(Map map)
        {
            if (map == null || map.mapPawns == null) return;
            int now = Find.TickManager != null ? Find.TickManager.TicksGame : 0;
            if (now - lastTick < Interval) return;
            lastTick = now;

            IReadOnlyList<Pawn> colonists = map.mapPawns.FreeColonistsSpawned;
            for (int i = 0; i < colonists.Count; i++)
            {
                Pawn p = colonists[i];
                if (p == null || p.Dead || p.Downed) continue;
                if (p.Drafted || p.InMentalState || p.InAggroMentalState) continue;
                if (EconomyMilitary.IsSoldier(p)) continue;   // 军人保持武装，只有非军人才交枪
                try { TryReturnWeapon(p, map); } catch (System.Exception e) { EcoLog.WarnOnce("pubgear-weapon", e); }
                try { TryReturnApparel(p, map); } catch (System.Exception e) { EcoLog.WarnOnce("pubgear-apparel", e); }
            }
        }

        private static void TryReturnWeapon(Pawn p, Map map)
        {
            if (p.equipment == null) return;
            ThingWithComps weapon = p.equipment.Primary;
            if (weapon == null || weapon.Destroyed) return;
            if (EconomyPrivate.OwnerOf(weapon) != null) return;   // 私有武器不动
            // 不再要求"闲着"：任务只是排进队列（EnqueueFirst），不会打断手上的活。

            ThingWithComps dropped;
            if (!p.equipment.TryDropEquipment(weapon, out dropped, p.PositionHeld, false)) return;
            if (dropped == null) return;

            DevLog.Msg("[GEAR] " + p.LabelShort + " 放下公共武器（" + dropped.LabelShort + "），准备收回仓库");
            SendToStorage(p, dropped, map);
        }

        private static void TryReturnApparel(Pawn p, Map map)
        {
            if (p.apparel == null) return;
            List<Apparel> worn = p.apparel.WornApparel;
            for (int i = worn.Count - 1; i >= 0; i--)
            {
                Apparel ap = worn[i];
                if (ap == null || ap.Destroyed) continue;
                if (EconomyPrivate.OwnerOf(ap) != null) continue;   // 私有衣物不动
                // 同上：不做"是否闲着"的限制
                if (!p.apparel.TryDrop(ap, out Apparel dropped, p.PositionHeld, false)) continue;
                if (dropped == null) continue;
                DevLog.Msg("[GEAR] " + p.LabelShort + " 脱下公共衣物（" + dropped.LabelShort + "），准备收回仓库");
                SendToStorage(p, dropped, map);
            }
        }

        private static bool IsBusy(Pawn p)
        {
            JobDef def = p.CurJobDef;
            if (def == null) return false;
            return def != JobDefOf.Wait && def != JobDefOf.Wait_Wander
                && def != JobDefOf.GotoWander && def != JobDefOf.Goto && def.joyKind == null;
        }

        private static void SendToStorage(Pawn p, Thing item, Map map)
        {
            IntVec3 cell;
            if (StoreFallback.TryFindPublicCell(item, p, map, out cell) && cell.IsValid)
            {
                Job job = JobMaker.MakeJob(JobDefOf.HaulToCell, item, cell);
                job.count = item.stackCount;
                job.haulMode = HaulMode.ToCellStorage;
                p.jobs.jobQueue.EnqueueFirst(job, JobTag.Misc);
                return;
            }

            Thing container;
            if (StoreFallback.TryFindPublicContainer(item, p, map, out container))
            {
                Job cj = HaulAIUtility.HaulToContainerJob(p, item, container);
                if (cj != null)
                {
                    p.jobs.jobQueue.EnqueueFirst(cj, JobTag.Misc);
                    return;
                }
            }

            DevLog.Msg("[GEAR] 没有可放位置，留在脚下：" + item.LabelShort);
        }
    }
}

