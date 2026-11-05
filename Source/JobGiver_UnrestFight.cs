using RimWorld;
using System.Collections.Generic;
using UnityEngine;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 继承原版征召战斗 AI（会用枪、找掩体、换弹），但把"打谁"钉死在我们的锁定目标上。
    /// 原版 AI 找不到射击位时会返回 null；有枪的人不能因此改去近战，
    /// 应该先换弹、再走到目标周围约 10 格的射击位，最后才让没枪的人近战。
    /// </summary>
    public class JobGiver_UnrestFight : JobGiver_AIFightEnemies
    {
        private const float MaxStandDistance = 10f;

        private static bool IsUnrestState(Pawn pawn)
        {
            if (pawn == null || !pawn.InMentalState || pawn.MentalStateDef == null) return false;
            string n = pawn.MentalStateDef.defName;
            return n == "DshEco_Riot" || n == "DshEco_Revolution";
        }

        protected override void UpdateEnemyTarget(Pawn pawn)
        {
            // 普通征召/非暴动革命不碰原版目标；即使 giver 被误挂到别的树，也不会自动靠近。
            if (!IsUnrestState(pawn)) return;

            Pawn t = EconomyUnrest.LockedTargetOf(pawn);
            if (t != null && t.Spawned && !t.Dead && !t.Downed)
            {
                pawn.mindState.enemyTarget = t;
                return;
            }
            pawn.mindState.enemyTarget = null;
        }

        protected override Job TryGiveJob(Pawn pawn)
        {
            if (!IsUnrestState(pawn)) return null;

            Pawn t = EconomyUnrest.LockedTargetOf(pawn);
            if (t == null)
            {
                if (pawn.mindState != null) pawn.mindState.enemyTarget = null;
                return null;
            }

            // 先把敌人钉死，再让原版征召 AI 决定用枪还是用刀
            if (pawn.mindState != null) pawn.mindState.enemyTarget = t;

            bool hasGun = HasRangedWeapon(pawn);
            object ammo = hasGun ? CECompat.GetAmmoUser(pawn) : null;
            bool gunUsable = hasGun && (ammo == null || CECompat.CanFire(ammo) || CECompat.HasAmmo(ammo));

            // CE：空弹/弹匣不满且身上有备弹时，先换弹
            if (hasGun && ammo != null && !CECompat.CanFire(ammo) && CECompat.HasAmmo(ammo))
            {
                Job reload = CECompat.TryMakeReloadJob(ammo);
                if (reload != null) return reload;
            }

            Job job = base.TryGiveJob(pawn);

            if (job != null && gunUsable && job.def == JobDefOf.AttackMelee)
            {
                Log.Message("[UF] " + pawn.LabelShort + " 有枪但原版派了近战，改走射击/靠近流程");
                job = null;
            }

            // 原版可能直接在当前位置 Wait_Combat；若友军挡线，CE 会拒绝开火，必须换安全射击位。
            if (job != null && gunUsable && job.def == JobDefOf.Wait_Combat
                && !FriendlyLineClear(pawn, pawn.Position, t))
            {
                Log.Message("[UF] " + pawn.LabelShort + " 当前位置友军挡线，寻找安全射击位");
                job = null;
            }

            if (job != null) return job;

            // 有枪：走到目标周围约 10 格、有视线的射击位，再站定开火
            if (gunUsable)
            {
                if (!pawn.CanReach(t, PathEndMode.Touch, Danger.Some, true, false)) return null;

                float range = RangedRange(pawn);
                float standDist = Mathf.Clamp(range * 0.8f, 6f, MaxStandDistance);
                IntVec3 cell = FindFiringCell(pawn, t, standDist);
                if (cell.IsValid && pawn.Position.DistanceTo(cell) > 1.5f)
                {
                    return JobMaker.MakeJob(JobDefOf.Goto, cell);
                }

                if (pawn.Position.DistanceTo(t.Position) > MaxStandDistance + 6f)
                {
                    IntVec3 closer = CellFinder.RandomClosewalkCellNear(t.Position, pawn.Map, (int)MaxStandDistance);
                    if (closer.IsValid) return JobMaker.MakeJob(JobDefOf.Goto, closer);
                }

                // 已经处在合适距离：站定，让 JobDriver_Wait 自己索敌开火
                return JobMaker.MakeJob(JobDefOf.Wait_Combat, 120, checkOverrideOnExpiry: true);
            }

            // 没枪、枪无弹且无备弹，或只拿近战武器，才允许近战兜底
            if (!pawn.CanReach(t, PathEndMode.Touch, Danger.Some, true, false)) return null;
            return JobMaker.MakeJob(JobDefOf.AttackMelee, t);
        }

        private static float RangedRange(Pawn pawn)
        {
            ThingWithComps eq = pawn != null && pawn.equipment != null ? pawn.equipment.Primary : null;
            CompEquippable ce = eq != null ? eq.GetComp<CompEquippable>() : null;
            Verb v = ce != null ? ce.PrimaryVerb : null;
            if (v == null || v.verbProps == null) return 10f;
            return Mathf.Max(4f, v.verbProps.range);
        }

        /// <summary>在目标周围 standDist 格的环上，找一个能站、有视线、能走到的射击位。</summary>
        private static IntVec3 FindFiringCell(Pawn pawn, Pawn target, float standDist)
        {
            if (pawn == null || target == null || pawn.Map == null) return IntVec3.Invalid;

            int radius = Mathf.RoundToInt(standDist);
            int tested = 0;
            foreach (IntVec3 c in GenRadial.RadialCellsAround(target.Position, radius, true))
            {
                if (!c.InBounds(pawn.Map) || !c.Standable(pawn.Map)) continue;
                if (!GenSight.LineOfSight(c, target.Position, pawn.Map, true)) continue;
                if (!FriendlyLineClear(pawn, c, target)) continue;
                if (++tested > 32) break;   // 限制 CanReach 次数，避免每次派 job 都做大量寻路
                if (!pawn.CanReach(c, PathEndMode.OnCell, Danger.Some, true, false)) continue;
                return c;
            }
            return IntVec3.Invalid;
        }

        /// <summary>射击线路上不能有友军，避免 CE 因友伤安全判定拒绝开火。</summary>
        private static bool FriendlyLineClear(Pawn pawn, IntVec3 from, Pawn target)
        {
            if (pawn == null || pawn.Map == null || target == null) return true;

            foreach (IntVec3 cell in GenSight.BresenhamCellsBetween(from, target.Position))
            {
                if (cell == from || cell == target.Position) continue;
                List<Thing> things = pawn.Map.thingGrid.ThingsListAt(cell);
                for (int i = 0; i < things.Count; i++)
                {
                    Pawn other = things[i] as Pawn;
                    if (other == null || other == pawn || other == target) continue;
                    if (other.Dead || other.Downed) continue;
                    if (other.Faction == pawn.Faction) return false;
                }
            }
            return true;
        }

        private static bool HasRangedWeapon(Pawn pawn)
        {
            return pawn != null
                && pawn.equipment != null
                && pawn.equipment.Primary != null
                && pawn.equipment.Primary.def != null
                && pawn.equipment.Primary.def.IsRangedWeapon;
        }
    }
}
