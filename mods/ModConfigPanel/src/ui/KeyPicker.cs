using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ModConfigPanel.UI
{
    /// <summary>
    /// 按键选择器弹窗。
    /// 以网格形式列出常用按键，点击即选中。
    /// </summary>
    public static class KeyPicker
    {
        private static GameObject _root;
        private static Transform _canvas;

        /// <summary>常用键列表（按类别分组显示）。</summary>
        private static readonly string[][] Groups = new string[][]
        {
            new[] { "A","B","C","D","E","F","G","H","I","J","K","L","M",
                    "N","O","P","Q","R","S","T","U","V","W","X","Y","Z" },
            new[] { "Alpha0","Alpha1","Alpha2","Alpha3","Alpha4",
                    "Alpha5","Alpha6","Alpha7","Alpha8","Alpha9" },
            new[] { "F1","F2","F3","F4","F5","F6","F7","F8","F9","F10","F11","F12" },
            new[] { "Space","LeftShift","RightShift","LeftControl","RightControl",
                    "LeftAlt","RightAlt","Tab","Escape","Return","Backspace","CapsLock" },
            new[] { "UpArrow","DownArrow","LeftArrow","RightArrow",
                    "Home","End","PageUp","PageDown","Insert","Delete" },
            new[] { "Comma","Period","Slash","Semicolon","Quote",
                    "LeftBracket","RightBracket","Backslash","Minus","Equals","BackQuote" },
            new[] { "Keypad0","Keypad1","Keypad2","Keypad3","Keypad4",
                    "Keypad5","Keypad6","Keypad7","Keypad8","Keypad9" },
            new[] { "Mouse0","Mouse1","Mouse2","Mouse3","Mouse4","None" },
        };

        public static void SetCanvas(Transform canvas) { _canvas = canvas; }

        public static void Show(string current, Action<string> onPick)
        {
            try
            {
                Close();
                if (_canvas == null) { Logx.Warn("[UI] 选键器缺少 Canvas"); return; }

                var root = UiKit.Stretch("KeyPickerMask", _canvas, 0, 0, 0, 0);
                UiKit.AddImage(root, new Color(0, 0, 0, 0.7f));
                _root = root.gameObject;

                float W = 1100f, H = 780f;
                var box = UiKit.Rect("Box", root, new Vector2(W, H), Vector2.zero);
                UiKit.AddImage(box, new Color(0.12f, 0.12f, 0.16f, 1f));

                var bar = UiKit.Stretch("Bar", box, 0, 0, 0, H - 8);
                UiKit.AddImage(bar, UiKit.ColAccent);

                UiKit.Text(box, "选择按键　当前：" + (current ?? "None"), 24,
                    new Vector2(W - 40, 40), new Vector2(0, H / 2 - 40),
                    UiKit.ColText, TextAnchor.MiddleCenter);

                // 网格区域
                float y = H / 2 - 90;
                float chipW = 78f, chipH = 38f, gap = 8f;

                foreach (var group in Groups)
                {
                    // 每行最多 12 个
                    int perRow = 12;
                    for (int i = 0; i < group.Length; i += perRow)
                    {
                        int count = Math.Min(perRow, group.Length - i);
                        float totalW = count * chipW + (count - 1) * gap;
                        float startX = -W / 2 + 40 + (W - 80 - totalW) / 2f;

                        for (int j = 0; j < count; j++)
                        {
                            string keyName = group[i + j];
                            float x = startX + j * (chipW + gap) + chipW / 2f;
                            bool isCur = string.Equals(keyName, current, StringComparison.OrdinalIgnoreCase);
                            Color bg = isCur ? new Color(0.55f, 0.32f, 0.12f) : UiKit.ColBtn;

                            UiKit.Button(box, keyName, new Vector2(chipW, chipH),
                                new Vector2(x, y), () =>
                                {
                                    onPick?.Invoke(keyName);
                                    Close();
                                }, bg, 15);
                        }
                        y -= chipH + gap;
                    }
                    y -= 6;
                }

                UiKit.Button(box, "取消", new Vector2(160, 46),
                    new Vector2(0, -H / 2 + 40), Close, new Color(0.35f, 0.20f, 0.20f), 18);
            }
            catch (Exception e) { Logx.Error("[UI] 选键器异常: " + e.Message); }
        }

        public static void Close()
        {
            if (_root != null) { UnityEngine.Object.Destroy(_root); _root = null; }
        }

        public static bool IsOpen => _root != null;
    }
}
