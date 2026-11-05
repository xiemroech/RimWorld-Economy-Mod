using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 一个商品税栏位：针对单件商品加税（或负税补贴），税率是价格的百分比。
    /// 存在地图组件里，随存档保存。
    /// </summary>
    public class TaxSlot : IExposable
    {
        public ThingDef thing;
        public float rate;

        public void ExposeData()
        {
            Scribe_Defs.Look<ThingDef>(ref thing, "thing");
            Scribe_Values.Look(ref rate, "rate", 0f);
        }
    }
}
