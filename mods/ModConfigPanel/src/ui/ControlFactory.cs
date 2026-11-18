using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.UI;
using ModConfigPanel.Schema;

namespace ModConfigPanel.UI
{
    /// <summary>可编辑控件的抽象：负责“读取当前值”和“写回值”。</summary>
    public abstract class Control
    {
        public ConfigItem Item;
        /// <summary>初始值（打开面板时从 cfg 读到的值）。</summary>
        public string OriginalValue;
        /// <summary>是否被玩家改动过（脏标记）。</summary>
        public bool Dirty;
        /// <summary>当前的内存值（保存时从这里取）。</summary>
        public abstract string GetValue();
        /// <summary>恢复为该配置项的默认值。</summary>
        public virtual void ResetToDefault() { }
        /// <summary>恢复为打开面板时的值。</summary>
        public virtual void Revert() { }

        /// <summary>每次值变化后调用：更新脏标记并刷新视觉提示。</summary>
        protected void MarkDirty()
        {
            string cur = GetValue();
            bool wasDirty = Dirty;
            Dirty = !string.Equals(cur, OriginalValue, StringComparison.Ordinal);
            if (Dirty && !wasDirty)
                Logx.Info($"[脏] {Item.Key}: '{OriginalValue}' -> '{cur}'");
            if (OnDirtyChanged != null) OnDirtyChanged(this, Dirty);
        }

        /// <summary>脏状态变化回调（由 UI 层挂接，用于刷新视觉与底部提示）。</summary>
        public static Action<Control, bool> OnDirtyChanged;

        /// <summary>构造结束后校正初始脏标记（防止构造期回调误判）。</summary>
        public void InitDirtyState()
        {
            if (!string.IsNullOrEmpty(OriginalValue))
            {
                string cur = GetValue();
                Dirty = !string.Equals(cur, OriginalValue, StringComparison.Ordinal);
                if (Dirty)
                    Logx.Warn($"[脏] 初始即脏 {Item.Key}: orig='{OriginalValue}' cur='{cur}'");
            }
            Dirty = false;   // 无论如何，刚打开面板不应有脏项
        }
    }

    public class ToggleControl : Control
    {
        private bool _value;
        private readonly RectTransform _knob;
        private readonly Image _bg;
        private readonly Text _label;

        public ToggleControl(Transform parent, ConfigItem item, Vector2 pos)
        {
            Item = item;
            bool initial = item.CurrentValue != null
                ? string.Equals(item.CurrentValue, "true", StringComparison.OrdinalIgnoreCase)
                : item.DefaultBool;
            _value = initial;
            OriginalValue = initial ? "true" : "false";

            float rowW = 0f;
            try { rowW = parent.GetComponent<RectTransform>().sizeDelta.x; } catch { }
            if (rowW <= 0) rowW = 1000f;

            var row = UiKit.Rect("Toggle", parent, new Vector2(180, 40),
                new Vector2(rowW / 2f - 16f - 120f, pos.y));

            var boxRt = UiKit.Rect("Box", row, new Vector2(84, 38), new Vector2(-40, 0));
            _bg = UiKit.AddImage(boxRt, UiKit.ColBtn);
            var btn = boxRt.gameObject.AddComponent<Button>();
            btn.targetGraphic = _bg;
            btn.transition = Selectable.Transition.None;

            var knobRt = UiKit.Rect("Knob", boxRt, new Vector2(36, 34), new Vector2(24, 0));
            var kImg = UiKit.AddImage(knobRt, Color.white, false);
            _knob = knobRt;

            _label = UiKit.Text(row, _value ? "开" : "关", 19,
                new Vector2(80, 40), new Vector2(45, 0), UiKit.ColText, TextAnchor.MiddleLeft);

            btn.onClick.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction>(
                (Action)(() =>
                {
                    _value = !_value;
                    Refresh();
                    MarkDirty();
                })));

            Refresh();
        }

        private void Refresh()
        {
            _bg.color = _value ? new Color(0.25f, 0.65f, 0.35f) : UiKit.ColBtn;
            _knob.anchoredPosition = new Vector2(_value ? 24 : -24, 0);
            if (_label != null) _label.text = _value ? "开" : "关";
        }

        public override string GetValue() => _value ? "true" : "false";

        public override void ResetToDefault()
        {
            _value = Item.DefaultBool;
            Refresh();
            MarkDirty();
        }

        public override void Revert()
        {
            _value = string.Equals(OriginalValue, "true", StringComparison.OrdinalIgnoreCase);
            Refresh();
            MarkDirty();
        }
    }

    /// <summary>
    /// 数字控件：[-] [输入框] [+]  + 范围提示。
    /// 布局以行右端为锚，从右往左排列，避免不同宽度行错位。
    /// </summary>
    public class NumberControl : Control
    {
        private float _value;
        private readonly InputField _input;

        private const float BtnW = 40f;
        private const float InputW = 130f;
        private const float Gap = 8f;
        private const float HintW = 130f;

        public NumberControl(Transform parent, ConfigItem item, Vector2 pos, float rowW)
        {
            Item = item;

            float init;
            if (item.CurrentValue != null &&
                float.TryParse(item.CurrentValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out init))
                _value = init;
            else
                _value = item.DefaultFloat;

            OriginalValue = FormatValue(_value);

            bool hasRange = item.Max > item.Min;
            float step = item.Integer ? 1f : (item.Step > 0 ? item.Step : 0.1f);

            // 以行右边缘为基准，从右往左排：hint | + | input | -
            float rightEdge = rowW / 2f - 16f;
            float hintX = rightEdge - HintW / 2f;
            float plusX = rightEdge - HintW - Gap - BtnW / 2f;
            float inputX = plusX - BtnW / 2f - Gap - InputW / 2f;
            float minusX = inputX - InputW / 2f - Gap - BtnW / 2f;
            float centerY = pos.y;

            // 减号
            UiKit.Button(parent, "−", new Vector2(BtnW, 34), new Vector2(minusX, centerY),
                () => { _value -= step; ClampAndSet(); }, UiKit.ColBtn, 22);

            // 输入框
            var iptRt = UiKit.Rect("Input", parent, new Vector2(InputW, 34), new Vector2(inputX, centerY));
            UiKit.AddImage(iptRt, new Color(0.18f, 0.18f, 0.24f));
            _input = iptRt.gameObject.AddComponent<InputField>();

            var txtRt = UiKit.Stretch("Text", iptRt, 6, 6, 3, 3);
            var txt = txtRt.gameObject.AddComponent<Text>();
            txt.font = UiKit.GetFont();
            txt.fontSize = 19;
            txt.color = UiKit.ColAccent;
            txt.alignment = TextAnchor.MiddleCenter;
            txt.raycastTarget = false;
            txt.supportRichText = false;

            // 占位提示：让玩家知道可以直接输入
            var phRt2 = UiKit.Stretch("Placeholder", iptRt, 6, 6, 3, 3);
            var ph2 = phRt2.gameObject.AddComponent<Text>();
            ph2.font = UiKit.GetFont();
            ph2.fontSize = 15;
            ph2.color = new Color(0.42f, 0.42f, 0.50f);
            ph2.alignment = TextAnchor.MiddleCenter;
            ph2.raycastTarget = false;
            ph2.text = "可输入";

            _input.textComponent = txt;
            _input.placeholder = ph2;
            _input.text = FormatValue(_value);
            _input.contentType = InputField.ContentType.Standard;
            _input.characterLimit = 12;
            _input.onEndEdit.AddListener(Il2CppInterop.Runtime.DelegateSupport
                .ConvertDelegate<UnityEngine.Events.UnityAction<string>>(
                    (Action<string>)(s => OnInputChanged(s))));

            // 加号
            UiKit.Button(parent, "+", new Vector2(BtnW, 34), new Vector2(plusX, centerY),
                () => { _value += step; ClampAndSet(); }, UiKit.ColBtn, 22);

            // 范围提示
            string hint = hasRange
                ? $"{item.Min.ToString("0.##")} ~ {item.Max.ToString("0.##")}"
                : (item.Integer ? "整数" : "任意值");
            UiKit.Text(parent, hint, 14, new Vector2(HintW, 34),
                new Vector2(hintX, centerY), UiKit.ColTextDim, TextAnchor.MiddleLeft);
        }

        private void OnInputChanged(string s)
        {
            float v;
            if (float.TryParse(s, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
            {
                _value = v;
                ClampAndSet();
            }
            else
            {
                _input.text = FormatValue(_value);
            }
        }

        private void ClampAndSet()
        {
            if (Item.Max > Item.Min) _value = Mathf.Clamp(_value, Item.Min, Item.Max);
            if (Item.Integer) _value = Mathf.Round(_value);
            _input.text = FormatValue(_value);
            MarkDirty();
        }

        private string FormatValue(float v)
        {
            if (Item.Integer) return ((int)v).ToString();
            return v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        }

        public override string GetValue() => FormatValue(_value);

        public override void ResetToDefault()
        {
            _value = Item.DefaultFloat;
            if (Item.Max > Item.Min) _value = Mathf.Clamp(_value, Item.Min, Item.Max);
            if (Item.Integer) _value = Mathf.Round(_value);
            _input.text = FormatValue(_value);
            MarkDirty();
        }

        public override void Revert()
        {
            float v;
            if (float.TryParse(OriginalValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out v))
                _value = v;
            else
                _value = Item.DefaultFloat;

            if (Item.Max > Item.Min) _value = Mathf.Clamp(_value, Item.Min, Item.Max);
            if (Item.Integer) _value = Mathf.Round(_value);
            _input.text = FormatValue(_value);
            MarkDirty();
        }
    }

    /// <summary>滑条控件（声明里显式 type=slider 的项）。</summary>
    public class SliderControl : Control
    {
        private float _value;
        private readonly Slider _slider;
        private readonly Text _valueText;

        public SliderControl(Transform parent, ConfigItem item, Vector2 pos, float rowW)
        {
            Item = item;

            float init;
            if (item.CurrentValue != null &&
                float.TryParse(item.CurrentValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out init))
                _value = init;
            else _value = item.DefaultFloat;

            OriginalValue = FormatValue(_value);

            float min = item.Min, max = item.Max > item.Min ? item.Max : item.Min + 1f;
            float step = item.Step > 0 ? item.Step : 0.05f;

            float rowW2 = 0f;
            try { rowW2 = parent.GetComponent<RectTransform>().sizeDelta.x; } catch { }
            if (rowW2 <= 0) rowW2 = 1000f;

            // 右端锚定：数值文字在右，滑条在其左
            float rightEdge = rowW2 / 2f - 16f;
            float valX = rightEdge - 70f;
            float barX = valX - 70f - 160f;

            var barRt = UiKit.Rect("Slider", parent, new Vector2(300, 30), new Vector2(barX, pos.y));

            var bgRt = UiKit.Stretch("Background", barRt, 0, 0, 12, 12);
            var bgImg = UiKit.AddImage(bgRt, new Color(0.18f, 0.18f, 0.24f), false);

            var fillArea = UiKit.Stretch("FillArea", barRt, 6, 6, 12, 12);
            var fillRt = new GameObject("Fill").AddComponent<RectTransform>();
            fillRt.SetParent(fillArea, false);
            fillRt.anchorMin = new Vector2(0, 0);
            fillRt.anchorMax = new Vector2(1, 1);
            fillRt.offsetMin = Vector2.zero;
            fillRt.offsetMax = Vector2.zero;
            var fillImg = UiKit.AddImage(fillRt, UiKit.ColAccent, false);

            var handleArea = UiKit.Stretch("HandleArea", barRt, 6, 6, 0, 0);
            var handleRt = UiKit.Rect("Handle", handleArea, new Vector2(20, 34), Vector2.zero);
            var hImg = UiKit.AddImage(handleRt, new Color(1f, 0.85f, 0.65f));

            _slider = barRt.gameObject.AddComponent<Slider>();
            _slider.fillRect = fillRt;
            _slider.handleRect = handleRt;
            _slider.targetGraphic = hImg;
            _slider.direction = Slider.Direction.LeftToRight;
            _slider.minValue = min;
            _slider.maxValue = max;
            _slider.value = Mathf.Clamp(_value, min, max);

            _slider.onValueChanged.AddListener(DelegateSupport.ConvertDelegate<UnityEngine.Events.UnityAction<float>>(
                (Action<float>)(v => { _value = Step(v); RefreshText(); MarkDirty(); })));

            _valueText = UiKit.Text(parent, FormatValue(_value), 19, new Vector2(140, 30),
                new Vector2(valX, pos.y), UiKit.ColAccent, TextAnchor.MiddleLeft);

            _value = _slider.value;
            RefreshText();
        }

        private float Step(float v)
        {
            float step = Item.Step > 0 ? Item.Step : 0.05f;
            return Mathf.Round(v / step) * step;
        }

        private void RefreshText()
        {
            string s = FormatValue(_value);
            if (!string.IsNullOrEmpty(Item.Unit)) s += " " + Item.Unit;
            _valueText.text = s;
        }

        private string FormatValue(float v) =>
            v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

        public override string GetValue() => FormatValue(_value);

        public override void ResetToDefault()
        {
            _value = Item.DefaultFloat;
            if (Item.Max > Item.Min) _value = Mathf.Clamp(_value, Item.Min, Item.Max);
            _slider.value = _value;
            RefreshText();
            MarkDirty();
        }

        public override void Revert()
        {
            float.TryParse(OriginalValue, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out _value);
            if (Item.Max > Item.Min) _value = Mathf.Clamp(_value, Item.Min, Item.Max);
            _slider.value = _value;
            RefreshText();
            MarkDirty();
        }
    }

    /// <summary>文本输入控件（String 类型配置）。</summary>
    public class TextInputControl : Control
    {
        private string _value;
        private readonly InputField _input;

        public TextInputControl(Transform parent, ConfigItem item, Vector2 pos, float rowW)
        {
            Item = item;
            _value = item.CurrentValue ?? "";
            OriginalValue = _value;

            float w = 420f;
            float x = rowW / 2f - 16f - w / 2f;

            var rt = UiKit.Rect("Input", parent, new Vector2(w, 36), new Vector2(x, pos.y));
            UiKit.AddImage(rt, new Color(0.18f, 0.18f, 0.24f));

            _input = rt.gameObject.AddComponent<InputField>();

            var txtRt = UiKit.Stretch("Text", rt, 8, 8, 3, 3);
            var txt = txtRt.gameObject.AddComponent<Text>();
            txt.font = UiKit.GetFont();
            txt.fontSize = 17;
            txt.color = UiKit.ColAccent;
            txt.alignment = TextAnchor.MiddleLeft;
            txt.raycastTarget = false;
            txt.supportRichText = false;

            // 占位提示
            var phRt = UiKit.Stretch("Placeholder", rt, 8, 8, 3, 3);
            var ph = phRt.gameObject.AddComponent<Text>();
            ph.font = UiKit.GetFont();
            ph.fontSize = 16;
            ph.color = new Color(0.45f, 0.45f, 0.52f);
            ph.alignment = TextAnchor.MiddleLeft;
            ph.raycastTarget = false;
            ph.text = "(留空)";

            _input.textComponent = txt;
            _input.placeholder = ph;
            _input.text = _value;
            _input.contentType = InputField.ContentType.Standard;
            _input.lineType = InputField.LineType.SingleLine;

            _input.onValueChanged.AddListener(Il2CppInterop.Runtime.DelegateSupport
                .ConvertDelegate<UnityEngine.Events.UnityAction<string>>(
                    (Action<string>)(v => { _value = v ?? ""; MarkDirty(); })));
        }

        public override string GetValue() => _value ?? "";

        public override void ResetToDefault()
        {
            _value = Item.CurrentValue ?? "";
            if (_input != null) _input.text = _value;
            MarkDirty();
        }

        public override void Revert()
        {
            _value = OriginalValue ?? "";
            if (_input != null) _input.text = _value;
            MarkDirty();
        }
    }

    /// <summary>
    /// 按键绑定控件。
    /// IL2CPP 环境下无法可靠地实时捕获按键（场景组件 Update 探针不可用、
    /// 自定义 MonoBehaviour 受泛型限制无法挂载），
    /// 因此改为【点击后弹出按键选择器】，从常用键列表中点选。
    /// 虽然比“按一下即记住”多一步，但 100% 可靠。
    /// </summary>
    public class KeybindControl : Control
    {
        private string _value;
        private readonly Text _display;
        private readonly Transform _rowParent;
        private readonly float _rowW;

        public KeybindControl(Transform parent, ConfigItem item, Vector2 pos, float rowW)
        {
            Item = item;
            _rowParent = parent;
            _rowW = rowW;
            _value = string.IsNullOrWhiteSpace(item.CurrentValue) ? "None" : item.CurrentValue.Trim();
            OriginalValue = _value;

            float x = rowW / 2f - 16f - 110f;
            var rt = UiKit.Rect("Keybind", parent, new Vector2(220, 36), new Vector2(x, pos.y));
            var img = UiKit.AddImage(rt, new Color(0.18f, 0.18f, 0.24f));
            var btn = rt.gameObject.AddComponent<UnityEngine.UI.Button>();
            btn.targetGraphic = img;
            btn.transition = UnityEngine.UI.Selectable.Transition.None;

            _display = UiKit.Text(rt, _value, 18, new Vector2(220, 36),
                Vector2.zero, UiKit.ColAccent, TextAnchor.MiddleCenter);

            btn.onClick.AddListener(Il2CppInterop.Runtime.DelegateSupport
                .ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(() => OpenPicker())));
        }

        private void OpenPicker()
        {
            try
            {
                KeyPicker.Show(_value, (picked) =>
                {
                    if (string.IsNullOrEmpty(picked)) return;
                    _value = picked;
                    _display.text = picked;
                    MarkDirty();
                    Logx.Info("[UI] 按键选择 " + Item.Key + " -> " + picked);
                });
            }
            catch (Exception e) { Logx.Error("[UI] 打开按键选择器失败: " + e.Message); }
        }

        public override string GetValue() => _value;

        public override void ResetToDefault()
        {
            _value = Item.CurrentValue ?? "None";
            _display.text = _value;
            MarkDirty();
        }

        public override void Revert()
        {
            _value = OriginalValue ?? "None";
            _display.text = _value;
            MarkDirty();
        }
    }

    /// <summary>按钮控件（M3 只做视觉与日志，动作在 M5 实现）。</summary>
    public class ButtonControl : Control
    {
        public ButtonControl(Transform parent, ConfigItem item, Vector2 pos, Action onClick)
        {
            Item = item;
            OriginalValue = null;   // 按钮不参与脏标记

            Color bg = UiKit.ColBtn;
            if (item.Action == "builtin:restart_game") bg = new Color(0.55f, 0.28f, 0.12f);
            else if (item.Action != null && item.Action.StartsWith("builtin:reset"))
                bg = new Color(0.35f, 0.22f, 0.22f);

            // 与数字/滑条一致：以行中心为 y，从右端往左定位
            float rowW = 0f;
            try { rowW = parent.GetComponent<RectTransform>().sizeDelta.x; } catch { }
            if (rowW <= 0) rowW = 1000f;

            float btnW = 200f;
            float x = rowW / 2f - 16f - btnW / 2f;

            UiKit.Button(parent, item.Label ?? item.Key, new Vector2(btnW, 38),
                new Vector2(x, pos.y), onClick, bg, 18);
        }

        public override string GetValue() => null;   // 按钮不参与保存
    }
}
