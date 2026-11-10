using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using HarmonyLib;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// RimHUD 兼容：RimHUD 不绘制原版角色卡（CharacterCardUtility），而是自己按
    /// Config/RimHUD/Docked.xml 的布局自绘 HUD，所以往原版绘制函数里插内容不会显示。
    /// 这里直接把一行 &lt;Need DefName="DshEco_Satisfaction"/&gt; 写进用户布局里，
    /// 让 RimHUD 自己去读我们注册的原生 Need。
    /// </summary>
    public static class RimHudIntegration
    {
        public const string NeedDefName = "DshEco_Satisfaction";

        /// <summary>
        /// RimHUD 的 NeedBar 工具提示是构造时传进来的，通用 &lt;Need DefName&gt; 传的是 null，
        /// 所以鼠标移上去什么都不显示。这里给 NeedBar 的构造函数加前缀，专门给我们这一条补上明细。
        /// </summary>
        public static bool Patch(HarmonyLib.Harmony harmony)
        {
            try
            {
                Type needBar = AccessTools.TypeByName("RimHUD.Interface.Hud.Models.Bars.NeedBar");
                if (needBar == null)
                {
                    DevLog.Msg("[DSH-ECONOMY] RimHUD 提示补丁：找不到 NeedBar，跳过");
                    return false;
                }
                Type funcType = typeof(Func<string>);
                var ctor = needBar.GetConstructors()
                    .FirstOrDefault(c => c.GetParameters().Length >= 2 && c.GetParameters()[1].ParameterType == funcType);
                if (ctor == null)
                {
                    Log.Warning("[DSH-ECONOMY] RimHUD 提示补丁：needbar 构造函数签名不匹配");
                    return false;
                }
                harmony.Patch(ctor, prefix: new HarmonyLib.HarmonyMethod(
                    AccessTools.Method(typeof(RimHudIntegration), "NeedBarPrefix")));
                DevLog.Msg("[DSH-ECONOMY] RimHUD 满意度提示：已挂载");
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] RimHUD 提示补丁失败：" + e.Message);
                return false;
            }
        }

        public static void NeedBarPrefix(RimWorld.NeedDef def, ref Func<string> tooltip)
        {
            if (def == null || def.defName != NeedDefName) return;
            tooltip = () =>
            {
                try
                {
                    Pawn pawn = RimHudPawn();
                    return pawn == null ? null : EconomySatisfaction.Breakdown(pawn);
                }
                catch (Exception e)
                {
                    return "满意度明细读取失败：" + e.Message;
                }
            };
        }
        private static System.Reflection.PropertyInfo pawnProperty;

        private static Pawn RimHudPawn()
        {
            if (pawnProperty == null)
            {
                Type active = AccessTools.TypeByName("RimHUD.Interface.Hud.Models.Active");
                pawnProperty = active == null ? null : active.GetProperty("Pawn", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (pawnProperty == null) return null;
            }
            return pawnProperty.GetValue(null) as Pawn;
        }

        public static bool TryApply()
        {
            try
            {
                bool installed = AppDomain.CurrentDomain.GetAssemblies()
                    .Any(a => a.GetName().Name == "RimHUD");
                if (!installed)
                {
                    DevLog.Msg("[DSH-ECONOMY] RimHUD 兼容：未安装 RimHUD，跳过（原版角色卡走绘制补丁）");
                    return false;
                }

                string dir = Path.Combine(GenFilePaths.ConfigFolderPath, "RimHUD");
                if (!Directory.Exists(dir))
                {
                    DevLog.Msg("[DSH-ECONOMY] RimHUD 兼容：尚未生成配置目录，跳过 " + dir);
                    return false;
                }

                bool changed = false;
                foreach (string name in new[] { "Docked.xml", "Floating.xml" })
                {
                    string file = Path.Combine(dir, name);
                    if (File.Exists(file)) changed |= PatchLayout(file);
                }

                DevLog.Msg("[DSH-ECONOMY] RimHUD 兼容：" + (changed ? "已把工作满意度写入 RimHUD 布局" : "布局已包含工作满意度"));
                return true;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] RimHUD 兼容失败：" + e);
                return false;
            }
        }

        static bool PatchLayout(string path)
        {
            XDocument doc = XDocument.Load(path);
            if (doc.Descendants().Any(e => (string)e.Attribute("DefName") == NeedDefName))
                return false;

            // 找左边那一栏（含 娱乐 需求行），在它后面插一行
            XElement panel = doc.Descendants("Panel")
                .FirstOrDefault(p => p.Elements("Row").Any(r => r.Elements("NeedRecreation").Any()));
            if (panel == null)
            {
                // 没有娱乐行就退而求其次，插在最后一条 Need 行之后
                XElement anyNeed = doc.Descendants("Need").LastOrDefault();
                if (anyNeed == null) return false;
                XElement row = anyNeed.Ancestors("Row").FirstOrDefault();
                if (row == null) return false;
                row.AddAfterSelf(new XElement("Row", new XAttribute("Targets", "H"),
                    new XElement("Need", new XAttribute("DefName", NeedDefName))));
                doc.Save(path);
                return true;
            }

            XElement anchor = panel.Elements("Row")
                .LastOrDefault(r => r.Elements().Any(e => e.Name.LocalName.StartsWith("Need")))
                ?? panel.Elements("Row").Last();
            anchor.AddAfterSelf(new XElement("Row", new XAttribute("Targets", "H"),
                new XElement("Need", new XAttribute("DefName", NeedDefName))));
            doc.Save(path);
            return true;
        }
    }
}

