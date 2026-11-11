using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 新加入殖民地的人（招募的俘虏、加入的访客、招募的难民……），
    /// 身上已经穿着/带着的东西自动记成他自己的私产。
    ///
    /// 不加这个的话，新人一进殖民地，他身上的衣服、武器都算“集体所有”，
    /// 别人会去穿、去买，看起来非常抽象。
    /// </summary>
    public static class NewColonistGear
    {
        // 已经处理过的人，避免每帧重复扫
        private static readonly HashSet<int> done = new HashSet<int>();

        public static void Apply(Harmony harmony)
        {
            int hooked = 0;
            List<System.Reflection.MethodInfo> targets = new List<System.Reflection.MethodInfo>();
            foreach (System.Reflection.MethodInfo m in typeof(Pawn).GetMethods(
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance))
            {
                if (m.Name != "SetFaction" && m.Name != "SetFactionDirect") continue;
                targets.Add(m);
            }
            for (int i = 0; i < targets.Count; i++)
            {
                try
                {
                    harmony.Patch(targets[i],
                        postfix: new HarmonyMethod(typeof(NewColonistGear).GetMethod(
                            nameof(AfterSetFaction),
                            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)));
                    hooked++;
                }
                catch { }
            }
            DevLog.Msg("[DSH-ECONOMY] 新人装备私有化：已挂 " + hooked + " 个换阵营入口");
        }

        public static void AfterSetFaction(Pawn __instance)
        {
            try { MarkGear(__instance); }
            catch (System.Exception e) { DevLog.Msg("[DSH-ECONOMY] 新人装备私有化失败：" + e.Message); }
        }

        /// <summary>把身上穿着、背包里带着的东西记成这个人的私产（已经是别人的就不动）。</summary>
        public static void MarkGear(Pawn p)
        {
            if (p == null || !p.IsColonistPlayerControlled || p.def == null) return;
            int key = p.thingIDNumber;
            if (done.Contains(key)) return;
            done.Add(key);

            int count = 0;
            if (p.apparel != null && p.apparel.WornApparel != null)
            {
                List<Apparel> worn = p.apparel.WornApparel;
                for (int i = 0; i < worn.Count; i++)
                {
                    Apparel a = worn[i];
                    if (a == null || a.Destroyed) continue;
                    if (EconomyPrivate.OwnerOf(a) != null) continue;
                    EconomyPrivate.SetPrivate(a, p);
                    count++;
                }
            }
            if (p.inventory != null && p.inventory.innerContainer != null)
            {
                foreach (Thing t in p.inventory.innerContainer)
                {
                    if (t == null || t.Destroyed) continue;
                    if (t.def == null || t.def.category != ThingCategory.Item) continue;
                    if (EconomyPrivate.OwnerOf(t) != null) continue;
                    EconomyPrivate.SetPrivate(t, p);
                    count++;
                }
            }
            if (count > 0)
            {
                DevLog.Msg("[APPAUTO] " + p.LabelShort + " 加入殖民地，身上 " + count + " 件已记为私有");
            }
        }
    }
}
