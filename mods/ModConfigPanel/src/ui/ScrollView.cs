using System;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigPanel.UI
{
    /// <summary>
    /// 简易滚动视图。
    /// 要点：
    ///   1. Viewport 必须铺满 ScrollRect（offsetMin/offsetMax = 0）
    ///   2. Content 顶部对齐（anchorMin=(0,1), anchorMax=(1,1), pivot=(0.5,1)）
    ///   3. Content 的 sizeDelta.y 必须 = 内容总高，否则无法滚动
    ///   4. Mask 需要 Graphic 才能裁剪，透明度设极小但必须 > 0
    /// </summary>
    public class ScrollView
    {
        public ScrollRect Rect;
        public RectTransform Content;
        public RectTransform Viewport;
        public float ViewportHeight { get; private set; }

        public static ScrollView Create(string name, Transform parent, Vector2 size, Vector2 pos)
        {
            var sv = new ScrollView();
            sv.ViewportHeight = size.y;

            var root = UiKit.Rect(name, parent, size, pos);
            sv.Rect = root.gameObject.AddComponent<ScrollRect>();

            // Viewport 铺满
            var vp = UiKit.Stretch("Viewport", root, 0, 0, 0, 0);
            sv.Viewport = vp;
            var vpImg = UiKit.AddImage(vp, new Color(1, 1, 1, 0.004f));
            var mask = vp.gameObject.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            // Content：顶部对齐，宽度撑满，高度动态
            var ct = new GameObject("Content");
            ct.transform.SetParent(vp, false);
            var cRt = ct.AddComponent<RectTransform>();
            cRt.anchorMin = new Vector2(0f, 1f);
            cRt.anchorMax = new Vector2(1f, 1f);
            cRt.pivot = new Vector2(0.5f, 1f);
            cRt.offsetMin = new Vector2(0f, 0f);
            cRt.offsetMax = new Vector2(0f, 0f);
            cRt.sizeDelta = new Vector2(0f, size.y);
            sv.Content = cRt;

            sv.Rect.viewport = vp;
            sv.Rect.content = cRt;
            sv.Rect.horizontal = false;
            sv.Rect.vertical = true;
            sv.Rect.movementType = ScrollRect.MovementType.Clamped;
            sv.Rect.scrollSensitivity = 40f;
            sv.Rect.inertia = true;
            sv.Rect.decelerationRate = 0.2f;

            return sv;
        }

        /// <summary>设置内容总高度；小于视口时自动贴顶不滚动。</summary>
        /// <summary>当前滚动位置（0=顶部）。</summary>
        public float ScrollPos
        {
            get
            {
                try { return Rect.verticalNormalizedPosition; }
                catch { return 1f; }
            }
            set
            {
                try { Rect.verticalNormalizedPosition = Mathf.Clamp01(value); }
                catch { }
            }
        }

        public void SetContentHeight(float h)
        {
            float real = Mathf.Max(h, ViewportHeight);
            Content.sizeDelta = new Vector2(Content.sizeDelta.x, real);
            // 注意：这里不再强制归零，交给调用方决定是否重置滚动位置
        }
    }
}
