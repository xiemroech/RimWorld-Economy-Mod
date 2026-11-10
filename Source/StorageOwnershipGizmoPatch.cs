using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>给存储建筑（货架/冰箱/储物箱）加「所有权」按钮，规则与私人堆放区一致。</summary>
    public static class StorageOwnershipGizmoPatch
    {
        private static bool reportedIcon;

        public static void Postfix(ThingWithComps __instance, ref IEnumerable<Gizmo> __result)
        {
            Building_Storage storage = __instance as Building_Storage;
            if (storage == null || storage.Map == null) return;

            List<Gizmo> gizmos = new List<Gizmo>(__result);

            Pawn owner = EconomyRealEstate.OwnerOfBuilding(storage);
            Command_Action cmd = new Command_Action();
            cmd.defaultLabel = owner != null ? ("所有权：" + owner.LabelShort) : "所有权：公共";
            cmd.defaultDesc = "点一下设置这个存储建筑的所有者：公共 / 某个殖民者。\n"
                + "私有存储只收物主自己的东西，且只让物主本人搬运。";
            Texture2D icon = ContentFinder<Texture2D>.Get("UI/Issues/DshEco_Ownership", false);
            if (icon == null && !reportedIcon)
            {
                reportedIcon = true;
                Log.Warning("[DSH-ECONOMY] 贴图 UI/Issues/DshEco_Ownership 载入失败，改用占位图");
            }
            cmd.icon = icon != null ? icon : BaseContent.BadTex;
            cmd.action = delegate
            {
                Building b = storage;
                Find.WindowStack.Add(new Window_PawnPicker(delegate(Pawn p)
                {
                    EconomyRealEstate.SetBuildingOwner(b, p);
                }, storage.Map.mapPawns.FreeColonists));
            };
            gizmos.Add(cmd);

            bool forSale = EconomyRealEstate.IsForSale(storage);
            Command_Action sale = new Command_Action();
            sale.defaultLabel = forSale ? "可购买：是" : "可购买：否";
            sale.defaultDesc = "小人是否会把这个储物建筑买成私有。点一下切换。\n设为「否」相当于上锁：永远不会被买走。";
            Texture2D coin = ContentFinder<Texture2D>.Get("UI/Icons/DshEco_Coin", false);
            if (coin != null) sale.icon = coin;
            sale.action = delegate
            {
                bool nowSale = !EconomyRealEstate.IsForSale(storage);
                    if (nowSale) EconomyRealEstate.ForceEnableSale();
                    EconomyRealEstate.SetForSale(storage, nowSale);
            };
            gizmos.Add(sale);

            __result = gizmos;
        }
    }
}
