using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>给堆放区加一个「所有权」按钮，点一下在公共与各殖民者之间轮换。</summary>
    public static class ZoneOwnershipGizmoPatch
    {
        public static void Postfix(Zone __instance, ref IEnumerable<Gizmo> __result)
        {
            Zone_Stockpile stockpile = __instance as Zone_Stockpile;
            if (stockpile == null || stockpile.Map == null) return;

            List<Gizmo> gizmos = new List<Gizmo>(__result);

            Pawn owner = EconomyRealEstate.OwnerOf(stockpile);
            Command_Action cmd = new Command_Action();
            cmd.defaultLabel = owner != null ? ("所有权：" + owner.LabelShort) : "所有权：公共";
            cmd.defaultDesc = "点一下轮换这个堆放区的所有者：公共 → 各殖民者 → 公共。\n私人堆放区只接收物主自己的东西，且只让物主本人搬运。";
            Texture2D icon = ContentFinder<Texture2D>.Get("UI/Issues/DshEco_Ownership", false);
            if (icon != null) cmd.icon = icon;
            cmd.action = delegate
            {
                Zone zone = stockpile;
                Find.WindowStack.Add(new Window_PawnPicker(delegate(Pawn p)
                {
                    EconomyRealEstate.SetOwner(zone, p);
                }, stockpile.Map.mapPawns.FreeColonists));
            };
            gizmos.Add(cmd);
            __result = gizmos;
        }
    }
}
