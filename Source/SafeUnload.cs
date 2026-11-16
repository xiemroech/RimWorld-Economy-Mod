using System;
using System.Collections.Generic;
using System.Reflection;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 把本 mod 写进存档的东西清干净，之后取消勾选就安全了。
    ///
    /// 之所以需要这一步：私有归属是挂在"每一件物品"上的 CompItemTag，
    /// 直接禁用 mod 会让存档里大量物品带一个不存在的组件类型，读档就坏。
    /// </summary>
    public static class SafeUnload
    {
        private static readonly FieldInfo compsField =
            typeof(ThingWithComps).GetField("comps", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static readonly FieldInfo needsField =
            typeof(Pawn_NeedsTracker).GetField("needs", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        public static string Run()
        {
            int comps = 0, needs = 0, buildings = 0, comps2 = 0, precepts = 0;

            // 1) 物品/小人身物品上的归属组件
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                foreach (Thing t in map.listerThings.AllThings) comps += StripComp(t);
            }
            foreach (Pawn p in Find.WorldPawns.AllPawnsAliveOrDead) comps += StripPawnGear(p, ref comps2);

            // 2) 小人身上的「工作满意度」需求
            foreach (Pawn p in Find.WorldPawns.AllPawnsAliveOrDead) needs += StripNeed(p);
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                foreach (Pawn p in map.mapPawns.AllPawns)
                {
                    needs += StripNeed(p);
                    comps += StripPawnGear(p, ref comps2);
                }
            }

            // 3) 我们加的建筑（摊位、银矿、银台…）
            foreach (Map map in Find.Maps)
            {
                if (map == null) continue;
                List<Building> all = new List<Building>(map.listerBuildings.allBuildingsColonist);
                for (int i = 0; i < all.Count; i++)
                {
                    Building b = all[i];
                    if (b == null || b.def == null) continue;
                    if (!b.def.defName.StartsWith("DshEco_", StringComparison.Ordinal)) continue;
                    try { b.Destroy(DestroyMode.Vanish); buildings++; } catch { }
                }
            }

            // 4) 意识形态里我们注入的财产制度戒律
            precepts = StripPrecepts();

            // 5) 我们的 GameComponent 全部摘掉（账本、房产、存款、工时、购买记录…）
            int compsRemoved = StripGameComponents();

            // 6) 运行时缓存
            try { EconomyMarket.ClearRuntime(); } catch { }

            // 7) RimHUD 布局里指向本 mod 的条目（它只加不删，不清就永远一条红条）
            int rimhud = StripRimHud();

            string msg = "已清理：物品标记 " + comps + " 件、小人装备 " + comps2 + " 件、需求 " + needs
                + " 条、建筑 " + buildings + " 座、戒律 " + precepts + " 条、存档组件 " + compsRemoved + " 个"
                + (rimhud > 0 ? "、RimHUD 配置 " + rimhud + " 处" : "")
                + "。\n\n【下一步】立刻保存（Esc → 保存），再退出、取消勾选本 mod、重新进档。\n不保存就退出的话，这次清理不会写进存档。";
            Log.Message("[DSH-ECONOMY] 卸载清理：" + msg);
            return msg;
        }

        private static int StripComp(Thing t)
        {
            ThingWithComps twc = t as ThingWithComps;
            if (twc == null || compsField == null) return 0;
            List<ThingComp> list = compsField.GetValue(twc) as List<ThingComp>;
            if (list == null) return 0;
            int removed = list.RemoveAll(c => c is CompItemTag);
            if (removed > 0)
            {
                try { twc.InitializeComps(); } catch { }
            }
            return removed;
        }

        private static int StripPawnGear(Pawn p, ref int counter)
        {
            if (p == null) return 0;
            int n = 0;
            try
            {
                if (p.apparel != null && p.apparel.WornApparel != null)
                    foreach (Apparel a in p.apparel.WornApparel) n += StripComp(a);
                if (p.equipment != null && p.equipment.AllEquipmentListForReading != null)
                    foreach (ThingWithComps e in p.equipment.AllEquipmentListForReading) n += StripComp(e);
                if (p.inventory != null && p.inventory.innerContainer != null)
                    foreach (Thing t in p.inventory.innerContainer) n += StripComp(t);
                if (p.carryTracker != null && p.carryTracker.CarriedThing != null)
                    n += StripComp(p.carryTracker.CarriedThing);
            }
            catch { }
            counter += n;
            return n;
        }

        private static int StripNeed(Pawn p)
        {
            if (p == null || p.needs == null || needsField == null) return 0;
            try
            {
                List<Need> list = needsField.GetValue(p.needs) as List<Need>;
                if (list == null) return 0;
                return list.RemoveAll(n => n is Need_Satisfaction);
            }
            catch { return 0; }
        }

        private static int StripPrecepts()
        {
            int removed = 0;
            try
            {
                List<Ideo> ideos = Find.IdeoManager != null ? Find.IdeoManager.IdeosListForReading as List<Ideo> : null;
                if (ideos == null) return 0;
                for (int i = 0; i < ideos.Count; i++)
                {
                    Ideo ideo = ideos[i];
                    if (ideo == null || ideo.precepts == null) continue;
                    removed += ideo.precepts.RemoveAll(pr =>
                        pr != null && pr.def != null &&
                        (pr.def.defName == "DshEco_PreceptPublicOwnership"
                         || pr.def.defName == "DshEco_PreceptPrivateOwnership"));
                }
            }
            catch { }
            return removed;
        }

        /// <summary>清 RimHUD 布局里指向本 mod 的那几行（它自己不会删，会一直画一条红条）。</summary>
        private static int StripRimHud()
        {
            int removed = 0;
            try
            {
                string root = System.IO.Path.Combine(GenFilePaths.ConfigFolderPath, "RimHUD");
                if (!System.IO.Directory.Exists(root)) return 0;
                foreach (string file in System.IO.Directory.GetFiles(root, "*.xml", System.IO.SearchOption.AllDirectories))
                {
                    try
                    {
                        string[] lines = System.IO.File.ReadAllLines(file);
                        List<string> keep = new List<string>(lines.Length);
                        bool changed = false;
                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].IndexOf("DshEco_", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                changed = true;
                                removed++;
                                continue;
                            }
                            keep.Add(lines[i]);
                        }
                        if (!changed) continue;
                        try { System.IO.File.Copy(file, file + ".bak", true); } catch { }
                        System.IO.File.WriteAllLines(file, keep, new System.Text.UTF8Encoding(false));
                    }
                    catch { }
                }
            }
            catch { }
            return removed;
        }

        private static int StripGameComponents()        {
            int removed = 0;
            try
            {
                if (Current.Game == null || Current.Game.components == null) return 0;
                List<GameComponent> all = new List<GameComponent>(Current.Game.components);
                for (int i = 0; i < all.Count; i++)
                {
                    GameComponent gc = all[i];
                    if (gc == null) continue;
                    if (gc.GetType().Namespace != "DshEconomy") continue;
                    try { Current.Game.components.Remove(gc); removed++; } catch { }
                }
            }
            catch { }
            return removed;
        }
    }
}
