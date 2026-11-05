using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 存储建筑上的「补货」页签。
    /// 不走 gizmo，是因为部分存储 mod 重写 GetGizmos 且不调 base，
    /// 我们的组件按钮会被整个吞掉；页签由检查面板统一绘制，不会被覆盖。
    /// </summary>
    public class ITab_StorageRestock : ITab
    {
        public ITab_StorageRestock()
        {
            size = new Vector2(440f, 320f);
            labelKey = "DshEco_RestockTab";
        }

        private Building_Storage Storage => SelThing as Building_Storage;

        public override bool IsVisible => Storage != null;

        protected override void FillTab()
        {
            Building_Storage st = Storage;
            if (st == null) return;

            RestockSettings cfg = RestockStore.For(st);
            if (cfg == null)
            {
                Widgets.Label(new Rect(10f, 10f, 400f, 30f), "这个建筑没有补货设置。");
                return;
            }

            Rect rect = new Rect(0f, 0f, size.x, size.y).ContractedBy(10f);
            float y = rect.y;

            Text.Font = GameFont.Medium;
            Widgets.Label(new Rect(rect.x, y, rect.width, 30f),
                cfg.detailed ? "补货：细化设置" : "补货：默认（补满空格）");
            Text.Font = GameFont.Small;
            y += 34f;

            int slots = RestockStore.TotalSlots(st);
            Widgets.Label(new Rect(rect.x, y, rect.width, 24f),
                "本仓库 " + slots + " 栏位。规则 " + cfg.rules.Count + " 条，占比之和 = "
                + Mathf.RoundToInt(RestockStore.TotalPct(cfg)) + "%");
            y += 26f;

            Widgets.Label(new Rect(rect.x, y, rect.width, 44f),
                "补货由搬运工执行：从公共库存搬到这个仓库。\n"
                + "某物品库存低于目标一半时，补到目标数量。");
            y += 50f;

            if (Widgets.ButtonText(new Rect(rect.x, y, 200f, 34f), "打开补货设置"))
            {
                Find.WindowStack.Add(new Window_Restock(st));
            }
            if (Widgets.ButtonText(new Rect(rect.x + 210f, y, 200f, 34f), "按当前库存生成规则"))
            {
                Window_Restock window = new Window_Restock(st);
                window.SeedFromStockPublic();
                Find.WindowStack.Add(window);
            }
            y += 44f;

            if (cfg.rules.Count > 0)
            {
                Widgets.Label(new Rect(rect.x, y, rect.width, 24f), "规则预览：");
                y += 26f;
                float bottom = rect.yMax;
                foreach (RestockRule r in cfg.rules)
                {
                    if (y + 22f > bottom) break;
                    if (r == null || r.def == null) continue;
                    int share = RestockStore.SlotsFor(st, r.pct);
                    int perSlot = RestockStore.ItemsPerSlot(st, r.def);
                    Widgets.Label(new Rect(rect.x, y, rect.width, 22f),
                        "· " + r.def.label + "　占比 " + Mathf.RoundToInt(r.pct) + "%　≈ "
                        + share + " 组 / " + (share * perSlot) + " 件");
                    y += 22f;
                }
            }
        }
    }
}
