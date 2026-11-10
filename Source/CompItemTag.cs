using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    public class CompProperties_ItemTag : CompProperties
    {
        public CompProperties_ItemTag()
        {
            compClass = typeof(CompItemTag);
        }
    }

    /// <summary>
    /// 物品自带的归属标签。owner 为 null 就是公共；有 owner 就是某个人的私产。
    /// 私有物品选中时会多一个「社会化并丢弃」按钮。
    /// </summary>
    public class CompItemTag : ThingComp
    {
        public Pawn owner;

        public bool IsPrivate
        {
            get { return owner != null; }
        }

        public override void PostExposeData()
        {
            base.PostExposeData();
            Scribe_References.Look(ref owner, "dsh_tagOwner");
        }

        public override string CompInspectStringExtra()
        {
            if (owner == null) return null;
            return "归属：私有（" + owner.LabelShort + "）";
        }

        public override IEnumerable<Gizmo> CompGetGizmosExtra()
        {
            if (owner == null) yield break;

            Command_Action cmd = new Command_Action();
            cmd.defaultLabel = "社会化并丢弃";
            cmd.defaultDesc = "把这件私人物品交还集体并丢到地上，物主会不满（可叠加）。";
            Texture2D icon = ContentFinder<Texture2D>.Get("UI/Issues/DshEco_Ownership", false);
            if (icon == null) icon = ContentFinder<Texture2D>.Get("UI/Commands/Forbid", false);
            if (icon == null) icon = BaseContent.BadTex;
            cmd.icon = icon;
            cmd.action = ConfiscateAndDrop;
            yield return cmd;
        }

        private void ConfiscateAndDrop()
        {
            EconomyPrivate.SocializeAndDrop(parent, parent.MapHeld);
        }
    }
}
