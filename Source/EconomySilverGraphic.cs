using System;
using RimWorld;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 把原版白银的图标换成银锭。
    ///
    /// 直接把同名文件放进 Textures/ 不一定能覆盖 Core 的贴图（Core 的资产优先级更高），
    /// 所以这里在启动时改 ThingDef 的 texPath 并重建 graphic，保证一定生效。
    /// </summary>
    public static class EconomySilverGraphic
    {
        public const string TexPath = "Things/Item/Resource/DshEco_SilverIngot";

        /// <summary>我们构造好的银锭图形，ThingDef.graphic 的 getter 补丁会直接返回它。</summary>
        public static Graphic Cached;

        /// <summary>贴图没加载成功的判定（null / 无材质 / BadTexture）。</summary>
        private static bool IsBad(Graphic g)
        {
            try
            {
                if (g == null) return true;
                if (g.MatSingle == null || g.MatSingle.mainTexture == null) return true;
                if (g.MatSingle.mainTexture.name == "BadTexture") return true;
                return false;
            }
            catch { return true; }
        }

        public static void Apply()
        {
            ThingDef silver = ThingDefOf.Silver;
            if (silver == null || silver.graphicData == null) return;

            try
            {
                if (silver.graphicData.texPath == TexPath && silver.graphic != null) return;

                silver.graphicData.texPath = TexPath;
                silver.graphicData.graphicClass = typeof(Graphic_StackCount);

                Texture2D tex = ContentFinder<Texture2D>.Get(TexPath, false);
                if (tex == null)
                {
                    Log.Warning("[DSH-ECONOMY] 银锭贴图没找到：" + TexPath);
                    return;
                }

                Graphic made = GraphicDatabase.Get<Graphic_StackCount>(
                    TexPath, ShaderDatabase.Cutout, silver.graphicData.drawSize, Color.white);

                // 运行时动态加载有时会拿到 BadTexture，退回单图 Graphic_Single
                if (IsBad(made))
                {
                    made = GraphicDatabase.Get<Graphic_Single>(
                        TexPath, ShaderDatabase.Cutout, silver.graphicData.drawSize, Color.white);
                }

                // 两种都拿不到就保持原版图标，不要把坏 graphic 塞进去——
                // 坏 graphic 会在渲染线程上炸掉整个游戏（表现为进图就闪退）。
                if (IsBad(made))
                {
                    DevLog.Msg("[DSH-ECONOMY] 银锭贴图不可用，保持原版图标");
                    return;
                }
                silver.graphic = made;
                Cached = made;

                string texName = "-";
                try
                {
                    if (silver.graphic != null && silver.graphic.MatSingle != null
                        && silver.graphic.MatSingle.mainTexture != null)
                    {
                        texName = silver.graphic.MatSingle.mainTexture.name;
                    }
                }
                catch { }
                DevLog.Msg("[DSH-ECONOMY] 世界贴图检查：graphic=" + (silver.graphic != null ? silver.graphic.GetType().Name : "null")
                    + " tex=" + texName);

                // UI 图标是单独缓存的（uiIcon / uiIconPath），只改 graphic 不会变
                silver.uiIcon = tex;
                silver.uiIconPath = TexPath;

                DevLog.Msg("[DSH-ECONOMY] 银锭贴图已替换（含 UI 图标）：" + TexPath);
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 替换银锭贴图失败：" + e.Message);
            }
        }
    }
}

