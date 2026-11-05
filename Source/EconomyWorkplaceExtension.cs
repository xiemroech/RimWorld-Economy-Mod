using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 标记"这是本 mod 的工作场所"，并带上它自己的屋内效率系数。
    /// 这样不同建筑可以有不同的屋内惩罚，而不是一刀切。
    /// </summary>
    public class EconomyWorkplaceExtension : DefModExtension
    {
        public float indoorsFactor = 0.5f;
    }
}
