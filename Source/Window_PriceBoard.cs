using UnityEngine;
using Verse;
using RimWorld;
using System;
using System.Collections.Generic;

namespace DshEconomy
{
    /// <summary>
    /// 物品价格小窗：点物品的 ⓘ 时在屏幕右下角浮出一个小方块。
    /// - 每点一次 ⓘ 就多开一个，可以同时看几个物品。
    /// - 默认跟着开它的那个信息卡：信息卡一关，它自己也关。
    /// - 拖动一下、或者点右上角的「钉」，就固定住不再自动关（钉住时按钮变金色）。
    /// 注意：关闭判定放在 WindowUpdate()（每帧跑），不能用 MapComponentTick（游戏暂停时它不跑）。
    /// </summary>
    public class Window_PriceBoard : Window
    {
        private static readonly List<Window_PriceBoard> live = new List<Window_PriceBoard>();

        public Thing thing;

        /// <summary>true = 用户钉住了，不再随信息卡关闭。</summary>
        public bool isPinned;

        /// <summary>打开它的那个信息卡窗口；null 表示还没认到。</summary>
        private Window owner;

        private Vector2 startPos;
        private bool startPosSet;

        public override Vector2 InitialSize => new Vector2(340f, 150f);

        public Window_PriceBoard()
        {
            doCloseX = true;
            doCloseButton = false;
            // 默认值是"点窗口外面就关"，于是点下一个 ⓘ 时，上一个小窗就被关掉了
            closeOnClickedOutside = false;
            // 默认同类窗口只允许一个，新开一个会把上一个关掉
            onlyOneOfTypeAllowed = false;
            forcePause = false;
            absorbInputAroundWindow = false;
            closeOnCancel = false;
            closeOnAccept = false;
            draggable = true;
            preventCameraMotion = false;
            layer = WindowLayer.Super;
        }

        protected override void SetInitialSizeAndPosition()
        {
            base.SetInitialSizeAndPosition();
            int slot = live.Count % 6;
            windowRect.x = UI.screenWidth - windowRect.width - 16f - slot * 40f;
            windowRect.y = UI.screenHeight - windowRect.height - 96f - slot * 44f;
        }

        /// <summary>点 ⓘ 时调用：每次新建一个窗口。</summary>
        public static void ShowFor(Thing t)
        {
            try
            {
                if (t == null || t.def == null) return;
                if (Find.WindowStack == null) return;
                Window_PriceBoard w = new Window_PriceBoard { thing = t, owner = FindInfoCard() };
                Find.WindowStack.Add(w);
                live.Add(w);
            }
            catch (Exception e) { Log.Warning("[DSH-ECONOMY] 价格小窗打开失败：" + e.Message); }
        }

        /// <summary>每帧由窗口栈调用：认领信息卡、拖动即钉住、信息卡关了就跟着关。</summary>
        public override void WindowUpdate()
        {
            try
            {
                if (Find.WindowStack == null) return;

                if (!startPosSet) { startPos = windowRect.position; startPosSet = true; }
                else if (!isPinned && Vector2.Distance(windowRect.position, startPos) > 2f)
                {
                    Pin();
                    return;
                }

                if (isPinned) return;

                if (owner == null)
                {
                    Window fresh = FindInfoCard();
                    if (fresh != null) owner = fresh;
                    return;
                }

                if (!Find.WindowStack.IsOpen(owner) || FindInfoCard() == null) Close();
            }
            catch (Exception e) { Log.Warning("[DSH-ECONOMY] 价格小窗更新异常：" + e.Message); }
        }

        /// <summary>窗口栈里那个物品信息卡（Dialog_InfoCard）。它打开后就一直在，点别的 ⓘ 只是换内容。</summary>
        private static Window FindInfoCard()
        {
            try
            {
                if (Find.WindowStack == null) return null;
                IList<Window> ws = Find.WindowStack.Windows;
                for (int i = ws.Count - 1; i >= 0; i--)
                {
                    Window w = ws[i];
                    if (w == null || w is Window_PriceBoard) continue;
                    if (w.GetType().Name.IndexOf("InfoCard", StringComparison.OrdinalIgnoreCase) >= 0) return w;
                }
            }
            catch { }
            return null;
        }

        /// <summary>钉住：不再跟信息卡一起关。</summary>
        public void Pin()
        {
            if (isPinned) return;
            isPinned = true;
        }

        public override void PostClose()
        {
            live.Remove(this);
        }

        public override void DoWindowContents(Rect inRect)
        {
            try
            {
                Thing t = thing;
                if (t == null || t.def == null) { Close(); return; }
                Map map = t.MapHeld ?? Find.CurrentMap;
                if (map == null) { Close(); return; }

                ThingDef d = t.def;
                float price = EconomyPrice.UnitPrice(t, map);
                float c = EconomyValue.MaterialCost(d, map);
                float lab = EconomyValue.LaborValue(d, map);
                float realIn = EconomyValue.RealIngredientCost(t);
                if (realIn > 0.01f) c = realIn;
                float value = c + lab;

                Text.Font = GameFont.Small;
                Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 24f),
                    d.LabelCap + "　现价 " + price.ToString("F2") + " 银/件");
                float y = inRect.y + 28f;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "价值 " + value.ToString("F1") + " = 原料 " + c.ToString("F1") + " + 劳动 " + lab.ToString("F1"));
                y += 22f;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "整叠 " + t.stackCount + " 件 ≈ " + (price * t.stackCount).ToString("F0") + " 银");
                y += 22f;
                Widgets.Label(new Rect(inRect.x, y, inRect.width, 20f),
                    "稀缺 ×" + EconomyPrice.ScarcityFactor(d, map).ToString("0.00")
                    + "　系数 ×" + EconomyPrice.CombinedFactor(d, map).ToString("0.00"));

                // 右上角 × 左边一个小小的「钉」
                const float closeSize = 24f;
                Rect btn = new Rect(inRect.xMax - closeSize - 22f, inRect.y - 2f, 20f, 20f);
                GameFont oldFont = Text.Font;
                Color oldColor = GUI.color;
                Text.Font = GameFont.Small;
                if (isPinned) GUI.color = new Color(1f, 0.85f, 0.4f);
                bool clicked = Widgets.ButtonText(btn, "钉");
                GUI.color = oldColor;
                Text.Font = oldFont;
                if (clicked)
                {
                    if (!isPinned) Pin();
                    else isPinned = false;
                }
            }
            catch { }
        }
    }
}
