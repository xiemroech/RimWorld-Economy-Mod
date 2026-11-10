using System.Collections.Generic;
using RimWorld;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 继承与充公：某人死亡时，私产按直系亲属（子女 / 兄弟姐妹 / 父母 / 孙辈）继承；
    /// 没有这些亲属，全部私有标签清掉，自动充公。
    /// </summary>
    public static class EconomyInheritance
    {
        private static readonly HashSet<string> KinDefs = new HashSet<string>
        {
            "Spouse", "Child", "Sibling", "Brother", "Sister", "Parent", "Grandchild", "Grandparent"
        };

        public static void Postfix(Pawn __instance)
        {
            try
            {
                if (__instance == null || !__instance.Dead || __instance.relations == null) return;
                // 只处理自由殖民者：囚犯、奴隶、客人、动物都没有"继承"这回事，
                // 在面板上列出来只会是噪音（它们本来也不会有私产）。
                if (!__instance.IsColonist) return;

                Pawn heir = FindHeir(__instance);
                List<Thing> owned = Collect(__instance);

                if (heir != null && heir != __instance && !heir.Dead)
                {
                    for (int i = 0; i < owned.Count; i++) EconomyPrivate.SetPrivate(owned[i], heir);
                    int saved = EconomySavings.Of(__instance);
                    if (saved > 0) { EconomySavings.Take(__instance, saved); EconomySavings.Deposit(heir, saved); }
                    DevLog.Msg("[DSH-ECONOMY] 继承：" + __instance.LabelShort + " 的 " + owned.Count
                        + " 件私产由 " + heir.LabelShort + " 继承");
                    Messages.Message(__instance.LabelShort + " 去世，遗产（" + owned.Count + " 件 + "
                        + saved + " 银）由 " + heir.LabelShort + " 继承。",
                        MessageTypeDefOf.PositiveEvent);
                }
                else
                {
                    for (int i = 0; i < owned.Count; i++) EconomyPrivate.SetPrivate(owned[i], null);
                    DevLog.Msg("[DSH-ECONOMY] 充公：" + __instance.LabelShort + " 没有直系亲属，"
                        + owned.Count + " 件私产收归集体");
                    Messages.Message(__instance.LabelShort + " 没有直系亲属，"
                        + owned.Count + " 件私产（含身上装备）收归集体。",
                        MessageTypeDefOf.NeutralEvent);
                }
            }
            catch (System.Exception e) { EcoLog.WarnOnce("inherit", e); }
        }

        private static Pawn FindHeir(Pawn dead)
        {
            Pawn best = null;
            int bestRank = 99;
            List<DirectPawnRelation> rels = dead.relations.DirectRelations;
            if (rels == null) return null;
            for (int i = 0; i < rels.Count; i++)
            {
                DirectPawnRelation r = rels[i];
                if (r == null || r.def == null || r.otherPawn == null) continue;
                if (!KinDefs.Contains(r.def.defName)) continue;
                Pawn other = r.otherPawn;
                if (other.Dead || other.Map != dead.Map) continue;
                string dn = r.def.defName;
                int rank = dn == "Spouse" ? 0
                    : dn == "Child" ? 1
                    : (dn == "Sibling" || dn == "Brother" || dn == "Sister") ? 2
                    : 3;
                if (rank < bestRank) { bestRank = rank; best = other; }
            }
            return best;
        }

        /// <summary>收集这个人名下所有还存在的实体私产。</summary>
        private static List<Thing> Collect(Pawn p)
        {
            List<Thing> list = new List<Thing>();
            Map map = p.MapHeld ?? p.Map;
            if (map == null) return list;

            if (p.inventory != null && p.inventory.innerContainer != null)
                foreach (Thing t in p.inventory.innerContainer)
                    if (t != null && EconomyPrivate.OwnerOf(t) == p) list.Add(t);

            if (p.apparel != null && p.apparel.WornApparel != null)
                foreach (Apparel a in p.apparel.WornApparel)
                    if (a != null && EconomyPrivate.OwnerOf(a) == p) list.Add(a);

            if (p.equipment != null && p.equipment.AllEquipmentListForReading != null)
                foreach (ThingWithComps t in p.equipment.AllEquipmentListForReading)
                    if (t != null && EconomyPrivate.OwnerOf(t) == p) list.Add(t);

            if (p.carryTracker != null && p.carryTracker.CarriedThing != null)
                if (EconomyPrivate.OwnerOf(p.carryTracker.CarriedThing) == p) list.Add(p.carryTracker.CarriedThing);

            List<Thing> all = map.listerThings.AllThings;
            for (int i = 0; i < all.Count; i++)
            {
                Thing t = all[i];
                if (t == null || t.Destroyed) continue;
                if (EconomyPrivate.OwnerOf(t) == p && !list.Contains(t)) list.Add(t);
            }
            return list;
        }
    }
}

