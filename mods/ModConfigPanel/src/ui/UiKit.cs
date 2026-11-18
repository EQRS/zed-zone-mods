using System;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigPanel.UI
{
    /// <summary>
    /// UI 基础工具。
    /// M0 验证得出的要点：
    ///   1. 所有控件统一 anchorMin=anchorMax=(0.5,0.5) + pivot(0.5,0.5)
    ///   2. 装饰性子物体与文字必须 raycastTarget=false，否则拦截父级点击
    ///   3. 字体取游戏自带 ModEditorWindow._cachedFont，中文正常
    /// </summary>
    public static class UiKit
    {
        public static readonly Color ColBg = new Color(0.09f, 0.09f, 0.12f, 0.98f);
        public static readonly Color ColPanel = new Color(0.13f, 0.13f, 0.17f, 1f);
        public static readonly Color ColHeader = new Color(0.16f, 0.16f, 0.21f, 1f);
        public static readonly Color ColAccent = new Color(0.95f, 0.55f, 0.15f, 1f);
        public static readonly Color ColText = new Color(0.92f, 0.92f, 0.94f, 1f);
        public static readonly Color ColTextDim = new Color(0.62f, 0.62f, 0.68f, 1f);
        public static readonly Color ColSelected = new Color(0.80f, 0.42f, 0.10f, 0.35f);
        public static readonly Color ColBtn = new Color(0.22f, 0.22f, 0.28f, 1f);
        public static readonly Color ColBtnHover = new Color(0.30f, 0.30f, 0.38f, 1f);

        private static Font _font;

        /// <summary>
        /// 安全销毁所有子节点。
        /// 注意：绝不能用 foreach (Transform child in parent)，IL2CPP 下枚举器返回 Il2CppSystem.Object，
        /// 强转 Transform 会抛 InvalidCastException。必须用索引 + GetChild。
        /// </summary>
        public static void DestroyChildren(Transform parent)
        {
            if (parent == null) return;
            try
            {
                for (int i = parent.childCount - 1; i >= 0; i--)
                {
                    var child = parent.GetChild(i);
                    if (child != null) UnityEngine.Object.Destroy(child.gameObject);
                }
            }
            catch (Exception e) { Logx.Error("[UI] 清理子节点失败: " + e.Message); }
        }

        public static Font GetFont()
        {
            if (_font != null) return _font;
            try { _font = ModEditorWindow._cachedFont; if (_font != null) return _font; } catch { }
            try { _font = Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
            return _font;
        }

        /// <summary>创建一个标准 RectTransform 节点。</summary>
        public static RectTransform Rect(string name, Transform parent, Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        /// <summary>拉伸铺满父级（用于背景板）。</summary>
        public static RectTransform Stretch(string name, Transform parent,
            float left, float right, float top, float bottom)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);
            return rt;
        }

        public static Image AddImage(RectTransform rt, Color color, bool raycast = true)
        {
            var img = rt.gameObject.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = raycast;
            return img;
        }

        public static Text Text(Transform parent, string content, int size, Vector2 size2, Vector2 pos,
            Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var rt = Rect("Text", parent, size2, pos);
            var t = rt.gameObject.AddComponent<Text>();
            t.text = content;
            t.font = GetFont();
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;      // 关键：不拦截点击
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        /// <summary>顶部对齐的文字（用于滚动内容）。</summary>
        public static Text TextTop(Transform parent, string content, int size, Vector2 size2, Vector2 pos,
            Color color, TextAnchor anchor = TextAnchor.MiddleLeft)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size2;
            rt.anchoredPosition = pos;

            var t = go.AddComponent<Text>();
            t.text = content;
            t.font = GetFont();
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.raycastTarget = false;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            return t;
        }

        public static Button Button(Transform parent, string label, Vector2 size, Vector2 pos,
            Action onClick, Color? bg = null, int fontSize = 20)
        {
            var rt = Rect("Button", parent, size, pos);
            var img = AddImage(rt, bg ?? ColBtn);

            var btn = rt.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.85f, 0.85f, 0.85f, 1f);
            btn.colors = colors;

            btn.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(
                (Action)(() => { try { onClick?.Invoke(); } catch (Exception e) { Logx.Error("按钮点击异常: " + e.Message); } })));

            var t = Text(rt, label, fontSize, size, Vector2.zero, ColText, TextAnchor.MiddleCenter);
            t.raycastTarget = false;
            return btn;
        }
    }
}
