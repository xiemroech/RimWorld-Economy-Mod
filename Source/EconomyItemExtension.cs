using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 挂在 ThingDef 上的经济元数据。别的 mod 只需要在自己的 ThingDef 里加：
    ///
    ///   &lt;modExtensions&gt;
    ///     &lt;li Class="DshEconomy.EconomyItemExtension"&gt;
    ///       &lt;category&gt;DshEco_Medicine&lt;/category&gt;
    ///       &lt;priceFactor&gt;1.3&lt;/priceFactor&gt;
    ///     &lt;/li&gt;
    ///   &lt;/modExtensions&gt;
    ///
    /// 不写这个扩展也能被自动归类（按 ThingCategoryDef），所以永远不会出现"白名单外无效"。
    /// </summary>
    public class EconomyItemExtension : DefModExtension
    {
        // 强制指定分类，留空则走自动匹配
        public EconomyCategoryDef category;

        // 物品级价格倍率，会乘在分类倍率上
        public float priceFactor = 1f;

        // 是否是货币。货币不参与定价，也不进任何分类。
        public bool isCurrency;

        // 完全排除出经济系统
        public bool exclude;

        // 物品级公共化覆盖，0-100；负数表示继承分类
        public float publicizationOverride = -1f;
    }
}
