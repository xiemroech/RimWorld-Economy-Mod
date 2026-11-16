using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using Verse;

namespace DshEconomy
{
    /// <summary>
    /// 让本 mod 的所有窗口在任何分辨率 / UI 缩放下都不会跑到屏幕外。
    ///
    /// 本 mod 的窗口尺寸是按 900×580 这类固定数值写的，原版 Window 打开时
    /// 不会检查屏幕能不能装下；1280×720 或把 UI 缩放调到 1.5 倍时，
    /// 窗口就有一半在屏幕外，看起来像"不适配分辨率"。
    ///
    /// 这里在 Window.SetInitialSizeAndPosition 之后统一收一次：
    /// 只处理命名空间是 DshEconomy 的窗口，不碰原版与其他 mod 的窗口。
    /// </summary>
    public static class UiScalePatch
    {
        public static void Apply(Harmony harmony)
        {
            MethodInfo m = AccessTools.Method(typeof(Verse.Window), "SetInitialSizeAndPosition");
            if (m == null)
            {
                Log.Warning("[DSH-ECONOMY] 窗口尺寸适配：没找到 Window.SetInitialSizeAndPosition");
                return;
            }

            harmony.Patch(m, postfix: new HarmonyMethod(typeof(UiScalePatch), nameof(Postfix)));
        }

        /// <summary>把窗口压进屏幕内（留 12 像素边）。</summary>
        private static void Postfix(Verse.Window __instance)
        {
            if (__instance == null) return;

            Type t = __instance.GetType();
            if (t.Namespace != "DshEconomy") return;      // 只动自家窗口

            try
            {
                Rect r = __instance.windowRect;

                float maxW = Mathf.Max(200f, UI.screenWidth - 24f);
                float maxH = Mathf.Max(150f, UI.screenHeight - 24f);

                float w = Mathf.Min(r.width, maxW);
                float h = Mathf.Min(r.height, maxH);

                r = new Rect(
                    Mathf.Clamp(r.x, 0f, Mathf.Max(0f, UI.screenWidth - w)),
                    Mathf.Clamp(r.y, 0f, Mathf.Max(0f, UI.screenHeight - h)),
                    w, h);

                __instance.windowRect = r;
            }
            catch (Exception e)
            {
                Log.Warning("[DSH-ECONOMY] 窗口尺寸适配失败：" + e.Message);
            }
        }
    }
}
