using System.Collections.Generic;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 经济分类。分类本身是 Def，所以任何 mod 都能新增分类，不需要改本 mod 的代码。
    ///
    /// 物品归属顺序：
    ///   1. ThingDef 上的 EconomyItemExtension.category（最精确）
    ///   2. 按 matchThingCategories / matchTradeTags 自动认领（优先级 priority 从大到小）
    ///   3. isFallback 的兜底分类
    ///   4. 没有任何分类 -> 不纳入经济，按原版市场价处理
    /// </summary>
    public class EconomyCategoryDef : Def
    {
        // 未手动调整时使用的公共化程度：0 = 全市场价，100 = 免费
        public float defaultPublicization = 50f;

        // 自动认领时的优先级，越大越先匹配
        public float priority;

        // 该分类整体的价格倍率
        public float priceFactor = 1f;

        // 基础需求（食物、医疗等），后续制度系统会用到
        public bool essential;

        // 兜底分类：所有没被认领的物品都进这里
        public bool isFallback;

        // 按原版 ThingCategoryDef 认领，自动包含其所有子分类
        public List<ThingCategoryDef> matchThingCategories;

        // 按 tradeTags 认领
        public List<string> matchTradeTags;

        // 按 defName 子串认领（跨 mod 用：仿生体/血清/基因/器官命名各异）
        public List<string> matchDefNameContains;
    }
}
