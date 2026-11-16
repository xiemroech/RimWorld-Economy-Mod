using System.Reflection;
using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;

namespace DshEconomy
{
    /// <summary>
    /// 「不是自己的私有物不能穿、不能拿」——和"别人的私有食物不能吃"同一套规则。
    /// 只看私有标签：公共物品照常可用，否则开局没私有衣物会光着。
    /// </summary>
    public static class PrivateUseBlock
    {
        public static bool Patch(Harmony harmony)
        {
            bool ok = false;

            MethodInfo wear = AccessTools.Method(typeof(Pawn_ApparelTracker), "Wear",
                new[] { typeof(Apparel), typeof(bool), typeof(bool) });
            if (wear != null)
            {
                harmony.Patch(wear, prefix: new HarmonyMethod(
                    typeof(PrivateUseBlock).GetMethod(nameof(ApparelPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                DevLog.Msg("[DSH-ECONOMY] 别人的私有衣物不能穿：已挂载");
                ok = true;
            }
            else Log.Warning("[DSH-ECONOMY] 没找到 Pawn_ApparelTracker.Wear");

            MethodInfo equip = AccessTools.Method(typeof(Pawn_EquipmentTracker), "AddEquipment",
                new[] { typeof(ThingWithComps) });
            if (equip != null)
            {
                harmony.Patch(equip, prefix: new HarmonyMethod(
                    typeof(PrivateUseBlock).GetMethod(nameof(EquipPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
                DevLog.Msg("[DSH-ECONOMY] 别人的私有武器不能拿：已挂载");
                ok = true;
            }
            else Log.Warning("[DSH-ECONOMY] 没找到 Pawn_EquipmentTracker.AddEquipment");

            return ok;
        }

        /// <summary>
        /// 非自己私有的弹药不许捡：以前小人去给枪装弹，把公共弹药拾进背包，
        /// 回收逻辑又把它当公共物品丢下，任务失败再重来，无限循环。
        /// 这里直接拦在"开始任务"这一步，让他根本不去捡。
        /// </summary>
        public static bool PatchAmmoPickup(Harmony harmony)
        {
            MethodInfo start = AccessTools.Method(typeof(Pawn_JobTracker), "StartJob",
                new[]
                {
                    typeof(Job), typeof(JobCondition), typeof(ThinkNode), typeof(bool), typeof(bool),
                    typeof(ThinkTreeDef), typeof(JobTag?), typeof(bool), typeof(bool),
                    typeof(bool?), typeof(bool), typeof(bool), typeof(bool)
                });
            if (start == null) start = AccessTools.Method(typeof(Pawn_JobTracker), "StartJob");
            if (start == null)
            {
                Log.Warning("[DSH-ECONOMY] 弹药拾取拦截：找不到 Pawn_JobTracker.StartJob");
                return false;
            }
            harmony.Patch(start, prefix: new HarmonyMethod(
                typeof(PrivateUseBlock).GetMethod(nameof(StartJobPrefix), BindingFlags.Static | BindingFlags.NonPublic)));
            DevLog.Msg("[DSH-ECONOMY] 非私有弹药不许捡：已挂载");
            return true;
        }

        private static bool StartJobPrefix(Pawn_JobTracker __instance, Job newJob)
        {
            if (newJob == null || __instance == null) return true;
            if (newJob.def != JobDefOf.TakeInventory) return true;
            Thing t = newJob.targetA.Thing;
            if (t == null || t.Destroyed) return true;

            Pawn p = __instance.pawn;
            if (p == null) return true;
            // 玩家亲自下令（右键"优先搬运/拾取"）一律放行：这是玩家在做私有发放。
            if (newJob.playerForced) return true;

            Pawn owner = EconomyPrivate.OwnerOf(t);
            if (owner == p) return true;   // 自己的私有物，随便拿

            // 别人的私有物：一律不许 AI 捡（捡了会被回收逻辑丢下，反复循环）
            if (owner != null)
            {
                DevLog.Msg("[USE] " + p.LabelShort + " 想拿走 " + owner.LabelShort + " 的 " + t.LabelShort + "，已拒绝");
                return false;
            }

            // 公共物品：只拦"武器/弹药"这类会触发装弹-丢下死循环的，
            // 其余（材料、食物、药品）放行，否则正常搬运/干活会被一起搞死。
            if (!IsAmmoLike(t.def) && !t.def.IsWeapon) return true;
            if (EconomyMarket.HasPending(p)) return true;   // 正在走购买流程
            DevLog.Msg("[USE] " + p.LabelShort + " 想拿走公共的 " + t.LabelShort + "，已拒绝");
            return false;
        }

        /// <summary>弹药类判定：defName 或分类名里带 Ammo / Bullet（CE、鼠族枪械等）。</summary>
        private static bool IsAmmoLike(ThingDef d)
        {
            if (d == null) return false;
            if (d.defName.IndexOf("Ammo", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (d.defName.IndexOf("Bullet", System.StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (d.thingCategories != null)
            {
                for (int i = 0; i < d.thingCategories.Count; i++)
                {
                    ThingCategoryDef c = d.thingCategories[i];
                    if (c != null && c.defName.IndexOf("Ammo", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
            }
            return false;
        }

        private static bool ApparelPrefix(Pawn_ApparelTracker __instance, Thing newApparel)
        {
            Pawn p = __instance != null ? __instance.pawn : null;
            if (p == null || newApparel == null) return true;
            Pawn owner = EconomyPrivate.OwnerOf(newApparel);
            if (owner == null || owner == p) return true;
            DevLog.Msg("[USE] " + p.LabelShort + " 想穿别人的私人物品（" + newApparel.LabelShort + "，物主 "
                + (owner.LabelShort ?? "?") + "），已拒绝");
            return false;
        }

        private static bool EquipPrefix(Pawn_EquipmentTracker __instance, ThingWithComps newEq)
        {
            Pawn p = __instance != null ? __instance.pawn : null;
            if (p == null || newEq == null) return true;
            Pawn owner = EconomyPrivate.OwnerOf(newEq);
            if (owner == null || owner == p) return true;
            DevLog.Msg("[USE] " + p.LabelShort + " 想拿别人的私人物品（" + newEq.LabelShort + "，物主 "
                + (owner.LabelShort ?? "?") + "），已拒绝");
            return false;
        }
    }
}

