using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using ModConfigPanel.Schema;

namespace ModConfigPanel.UI
{
    /// <summary>
    /// 模组配置面板主体。
    /// 布局：左侧模组列表（280px） + 右侧配置区。
    /// </summary>
    public static class ConfigPanel
    {
        private const float W = 1400f;
        private const float H = 820f;
        private const float LeftW = 320f;
        private const float HeaderH = 70f;
        private const float FooterH = 70f;

        private static GameObject _canvasGo;
        private static RectTransform _panelRoot;
        private static RectTransform _rightArea;
        private static ScrollView _leftScroll;
        private static Text _statusText;
        private static ScrollView _rightScroll;
        private static UnityEngine.UI.Button _btnSave;
        private static UnityEngine.UI.Button _btnRevert;
        private static bool _hintLocked;
        private static readonly System.Collections.Generic.Dictionary<ConfigItem, GameObject> _rowMarkers
            = new System.Collections.Generic.Dictionary<ConfigItem, GameObject>();
        // 折叠状态：section 名 -> 是否收起
        private static readonly System.Collections.Generic.HashSet<string> _collapsed
            = new System.Collections.Generic.HashSet<string>();
        private static float _savedScrollPos = -1f;   // <0 表示无记录
        private static Text _saveHint;

        private static List<ModSchema> _schemas;
        private static int _selected = -1;
        private static readonly List<GameObject> _leftRows = new List<GameObject>();
        private static readonly List<Control> _controls = new List<Control>();

        public static bool IsOpen => _panelRoot != null;

        public static void Toggle()
        {
            if (IsOpen) { RequestClose(); return; }
            // 打开前先清理可能存在的残留节点
            Close();
            Open();
        }

        public static void Open()
        {
            try
            {
                _schemas = PanelPlugin.Schemas ?? new List<ModSchema>();
                _selected = _schemas.Count > 0 ? 0 : -1;

                if (_canvasGo == null) BuildCanvas();

                // 关键：打开前重新读取所有 cfg 的当前值，
                // 避免上次保存后重开仍显示旧值（Schema 结构可缓存，但值必须重读）
                ReloadCurrentValues();

                BuildPanel();
                Logx.Info("[UI] 面板已打开，" + _schemas.Count + " 个模组");
            }
            catch (Exception e) { Logx.Error("[UI] 打开失败: " + e); }
        }

        /// <summary>关闭请求（带未保存检查）。</summary>
        public static void RequestClose()
        {
            try
            {
                if (HasUnsavedChanges())
                {
                    ShowConfirm("有未保存的改动，确定放弃吗？", () => Close());
                    return;
                }
                Close();
            }
            catch { Close(); }
        }

        /// <summary>
        /// 重新从磁盘读取各模组 cfg 的当前值，写回 Schema 的 CurrentValue。
        /// 只更新值，不改变分组/控件声明（保留作者的声明信息）。
        /// </summary>
        private static void ReloadCurrentValues()
        {
            if (_schemas == null) return;
            foreach (var s in _schemas)
            {
                try
                {
                    string path = s.ConfigAbsPath;
                    if (string.IsNullOrEmpty(path))
                    {
                        string root = SchemaDiscovery.GameRoot;
                        if (!string.IsNullOrEmpty(root) && !string.IsNullOrEmpty(s.ConfigFile))
                            path = System.IO.Path.Combine(root, s.ConfigFile.Replace('/', '\\'));
                    }
                    if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path)) continue;

                    var parsed = Schema.CfgParser.Parse(path);

                    // 建立 section -> key -> value 查找表
                    var map = new System.Collections.Generic.Dictionary<string, System.Collections.Generic.Dictionary<string, string>>();
                    foreach (var sec in parsed.Sections)
                    {
                        var d = new System.Collections.Generic.Dictionary<string, string>();
                        foreach (var k in sec.Keys) d[k.Key] = k.Value;
                        map[sec.Name] = d;
                    }

                    int updated = 0;
                    foreach (var g in s.Groups)
                    {
                        if (!map.TryGetValue(g.Section, out var d)) continue;
                        foreach (var it in g.Items)
                        {
                            if (it.Kind == ItemKind.Button) continue;
                            if (d.TryGetValue(it.Key, out string v) && v != it.CurrentValue)
                            {
                                it.CurrentValue = v;
                                updated++;
                            }
                        }
                    }
                    if (updated > 0) Logx.Info($"[UI] 重读 {s.DisplayName}：更新 {updated} 项值");
                }
                catch (Exception e)
                {
                    Logx.Warn("[UI] 重读失败 " + s.ModGuid + ": " + e.Message);
                }
            }
        }

        public static void Close()
        {
            try
            {
                _controls.Clear();
                _leftRows.Clear();

                if (_panelRoot != null)
                {
                    _panelRoot.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(_panelRoot.gameObject);
                    _panelRoot = null;
                }

                // 兜底：清掉 Canvas 下所有残留子节点（防止历史版本遗留的遮罩挡住界面）
                if (_confirmRoot != null) { UnityEngine.Object.Destroy(_confirmRoot); _confirmRoot = null; }
                KeyPicker.Close();
                if (_canvasGo != null) UiKit.DestroyChildren(_canvasGo.transform);

                _rightArea = null;
                _leftScroll = null;
                _statusText = null;

                Logx.Info("[UI] 面板已关闭");
            }
            catch (Exception e) { Logx.Error("[UI] 关闭异常: " + e); }
        }

        private static void BuildCanvas()
        {
            _canvasGo = new GameObject("ModConfigPanelCanvas");
            var canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 32000;
            var scaler = _canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.matchWidthOrHeight = 0.5f;
            _canvasGo.AddComponent<GraphicRaycaster>();
            KeyPicker.SetCanvas(_canvasGo.transform);
            UnityEngine.Object.DontDestroyOnLoad(_canvasGo);
        }

        private static void BuildPanel()
        {
            if (_panelRoot != null) UnityEngine.Object.Destroy(_panelRoot.gameObject);

            // 整体容器：遮罩 + 面板都挂在它下面，关闭时一并销毁
            _panelRoot = UiKit.Stretch("Root", _canvasGo.transform, 0, 0, 0, 0);

            // 底层遮罩（点击遮罩不关闭，避免误触）
            var maskRt = UiKit.Stretch("Mask", _panelRoot, 0, 0, 0, 0);
            UiKit.AddImage(maskRt, new Color(0, 0, 0, 0.55f));

            // 面板本体
            var panel = UiKit.Rect("Panel", _panelRoot, new Vector2(W, H), Vector2.zero);
            UiKit.AddImage(panel, UiKit.ColBg);

            // 标题栏
            var header = UiKit.Stretch("Header", panel, 0, 0, 0, H - HeaderH);
            UiKit.AddImage(header, UiKit.ColHeader);
            // 标题靠左
            UiKit.Text(header, "模组配置", 30, new Vector2(180, HeaderH),
                new Vector2(-W / 2 + 110, 0), UiKit.ColAccent, TextAnchor.MiddleLeft);
            // 状态文字居中偏右，与标题不重叠
            _statusText = UiKit.Text(header, "", 17, new Vector2(560, HeaderH),
                new Vector2(60, 0), UiKit.ColTextDim, TextAnchor.MiddleCenter);

            // 关闭按钮
            UiKit.Button(header, "✕", new Vector2(56, 44), new Vector2(W / 2 - 42, 0), RequestClose,
                new Color(0.45f, 0.18f, 0.18f));

            // 底部栏：左侧按钮组（左对齐累加），右侧提示文字
            var footer = UiKit.Stretch("Footer", panel, 0, 0, H - FooterH, 0);
            UiKit.AddImage(footer, UiKit.ColHeader);

            float bx = -W / 2 + 16f;   // 按钮组左边界
            const float gapB = 8f;

            float w1 = 104f, w2 = 104f, w3 = 104f, w4 = 88f, w5 = 148f;

            bx += w1 / 2f;
            UiKit.Button(footer, "刷新", new Vector2(w1, 44), new Vector2(bx, 0), OnRefresh,
                new Color(0.20f, 0.22f, 0.28f), 18);
            bx += w1 / 2f + gapB;

            bx += w2 / 2f;
            _btnSave = UiKit.Button(footer, "保存", new Vector2(w2, 44), new Vector2(bx, 0), OnSave,
                new Color(0.22f, 0.32f, 0.24f), 18);
            bx += w2 / 2f + gapB;

            bx += w3 / 2f;
            _btnRevert = UiKit.Button(footer, "撤销", new Vector2(w3, 44), new Vector2(bx, 0), OnRevert,
                new Color(0.35f, 0.25f, 0.20f), 18);
            _btnRevert.gameObject.SetActive(false);
            bx += w3 / 2f + gapB;

            bx += w4 / 2f;
            UiKit.Button(footer, "诊断", new Vector2(w4, 44), new Vector2(bx, 0), ShowDiagnostics,
                new Color(0.24f, 0.26f, 0.34f), 17);
            bx += w4 / 2f + gapB;

            bx += w5 / 2f;
            UiKit.Button(footer, "全部恢复默认", new Vector2(w5, 44), new Vector2(bx, 0),
                OnResetAll, new Color(0.36f, 0.26f, 0.22f), 16);
            bx += w5 / 2f + gapB;

            // 提示文字：紧贴按钮组右侧，右端对齐面板内边
            float hintLeft = bx + 12f;
            float hintRight = W / 2f - 16f;
            float hintW = Mathf.Max(120f, hintRight - hintLeft);
            _saveHint = UiKit.Text(footer, "修改后点「保存」写入配置文件，重启游戏生效", 16,
                new Vector2(hintW, FooterH),
                new Vector2(hintLeft + hintW / 2f, 0), UiKit.ColTextDim, TextAnchor.MiddleRight);
            _saveHint.horizontalOverflow = HorizontalWrapMode.Overflow;

            // 左侧列表区
            var leftBg = UiKit.Stretch("LeftArea", panel, 0, W - LeftW, HeaderH, FooterH);
            UiKit.AddImage(leftBg, UiKit.ColPanel);
            _leftScroll = ScrollView.Create("LeftScroll", leftBg,
                new Vector2(LeftW - 20, H - HeaderH - FooterH - 20), new Vector2(0, 0));

            // 右侧内容区
            _rightArea = UiKit.Stretch("RightArea", panel, LeftW, 0, HeaderH, FooterH);

            BuildModList();
            BuildRightContent();
        }

        // ---------------- 左侧：模组列表 ----------------
        private static void BuildModList()
        {
            foreach (var go in _leftRows) if (go != null) UnityEngine.Object.Destroy(go);
            _leftRows.Clear();

            float rowH = 62f, pad = 6f;
            float y = -pad;
            float contentW = LeftW - 40f;

            if (_schemas.Count == 0)
            {
                UiKit.Text(_leftScroll.Content, "未发现可配置的模组", 20,
                    new Vector2(LeftW - 40, 40), new Vector2(0, y - 20), UiKit.ColTextDim, TextAnchor.MiddleCenter);
                _leftScroll.SetContentHeight(80);
                return;
            }

            for (int i = 0; i < _schemas.Count; i++)
            {
                int index = i;
                var s = _schemas[i];

                // 顶部对齐定位：anchorMin/Max=(0.5,1), pivot=(0.5,1)
                var row = CreateTopAnchored("Row" + i, _leftScroll.Content,
                    new Vector2(contentW, rowH), new Vector2(0, y));
                var img = UiKit.AddImage(row, i == _selected ? UiKit.ColSelected : new Color(0, 0, 0, 0.25f));
                var btn = row.gameObject.AddComponent<Button>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(Il2CppInterop.Runtime.DelegateSupport
                    .ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(() => Select(index))));

                UiKit.Text(row, s.DisplayName ?? s.ModGuid, 21,
                    new Vector2(LeftW - 50, 26), new Vector2(6, 14), UiKit.ColText, TextAnchor.MiddleLeft);
                UiKit.Text(row, $"{s.Source} · {s.TotalItems} 项", 15,
                    new Vector2(LeftW - 50, 20), new Vector2(6, -14), UiKit.ColTextDim, TextAnchor.MiddleLeft);

                _leftRows.Add(row.gameObject);
                y -= rowH + pad;
            }

            _leftScroll.SetContentHeight(Mathf.Abs(y) + pad);
        }

        /// <summary>创建顶部对齐的节点（用于滚动内容）。</summary>
        private static RectTransform CreateTopAnchored(string name, Transform parent,
            Vector2 size, Vector2 pos)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 1f);
            rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = size;
            rt.anchoredPosition = pos;
            return rt;
        }

        private static void Select(int index)
        {
            if (index < 0 || index >= _schemas.Count) return;
            bool changed = index != _selected;
            _selected = index;
            if (changed) _savedScrollPos = 1f;   // 切换模组时回到顶部
            BuildModList();
            BuildRightContent();
        }

        // ---------------- 右侧：配置内容 ----------------
        private static void BuildRightContent()
        {
            // 记录旧滚动位置（重建后恢复，避免跳回顶部）
            if (_rightScroll != null && _savedScrollPos < 0f)
            {
                try { _savedScrollPos = _rightScroll.ScrollPos; } catch { }
            }

            _controls.Clear();
            _rowMarkers.Clear();
            _rightScroll = null;
            UiKit.DestroyChildren(_rightArea);

            if (_selected < 0 || _selected >= _schemas.Count)
            {
                UiKit.Text(_rightArea, "← 请选择一个模组", 24, new Vector2(600, 60),
                    Vector2.zero, UiKit.ColTextDim, TextAnchor.MiddleCenter);
                return;
            }

            var s = _schemas[_selected];
            if (_statusText != null) _statusText.text = $"{s.ModGuid}　·　来源 {s.Source}";

            var scroll = ScrollView.Create("RightScroll", _rightArea,
                new Vector2(W - LeftW - 30, H - HeaderH - FooterH - 20), Vector2.zero);
            _rightScroll = scroll;

            float y = -10f;
            float rowW = W - LeftW - 80;

            // 模组标题
            UiKit.TextTop(scroll.Content, s.DisplayName ?? s.ModGuid, 28,
                new Vector2(rowW, 40), new Vector2(0, y), UiKit.ColAccent, TextAnchor.MiddleLeft);
            y -= 48;

            if (!string.IsNullOrEmpty(s.Description))
            {
                UiKit.TextTop(scroll.Content, s.Description, 18,
                    new Vector2(rowW, 28), new Vector2(0, y), UiKit.ColTextDim, TextAnchor.MiddleLeft);
                y -= 34;
            }

            y -= 10;

            foreach (var g in s.Groups)
            {
                // 分组标题（可点击折叠）
                bool collapsed = _collapsed.Contains(g.Section);
                var gBg = CreateTopAnchored("Group", scroll.Content, new Vector2(rowW, 42), new Vector2(0, y));
                var gImg = UiKit.AddImage(gBg, UiKit.ColHeader);
                var gBtn = gBg.gameObject.AddComponent<Button>();
                gBtn.targetGraphic = gImg;

                string arrow = collapsed ? "▶" : "▼";
                string gTitle = $"  {arrow}  {g.Title ?? g.Section}";
                UiKit.Text(gBg, gTitle, 22, new Vector2(rowW - 160, 42),
                    new Vector2(0, 0), UiKit.ColText, TextAnchor.MiddleLeft);
                UiKit.Text(gBg, g.Items.Count + " 项", 15, new Vector2(120, 42),
                    new Vector2(rowW / 2 - 70, 0), UiKit.ColTextDim, TextAnchor.MiddleRight);

                string sec = g.Section;
                gBtn.onClick.AddListener(Il2CppInterop.Runtime.DelegateSupport
                    .ConvertDelegate<UnityEngine.Events.UnityAction>((Action)(() =>
                    {
                        if (_collapsed.Contains(sec)) _collapsed.Remove(sec);
                        else _collapsed.Add(sec);
                        // 保住滚动位置
                        _savedScrollPos = _rightScroll != null ? _rightScroll.ScrollPos : -1f;
                        BuildRightContent();
                    })));

                y -= 50;

                if (!collapsed)
                {
                    foreach (var it in g.Items)
                    {
                        y = BuildItemRow(scroll.Content, it, y, rowW);
                    }
                }
                y -= 12;
            }

            // 顶部状态栏：只显示模组来源
            if (_statusText != null)
                _statusText.text = $"{s.ModGuid}　·　{s.Source}";

            // 绑定脏状态变化的回调
            Control.OnDirtyChanged = (ctl, dirty) =>
            {
                if (_rowMarkers.TryGetValue(ctl.Item, out var dot) && dot != null)
                    dot.SetActive(dirty);
                UpdateFooterState();
            };

            scroll.SetContentHeight(Mathf.Abs(y) + 30);

            // 恢复滚动位置
            if (_savedScrollPos >= 0f)
            {
                try
                {
                    scroll.ScrollPos = _savedScrollPos;
                }
                catch { }
                _savedScrollPos = -1f;
            }
        }

        /// <summary>渲染单个配置项（M3：可编辑控件）。</summary>
        private static float BuildItemRow(Transform parent, ConfigItem it, float y, float rowW)
        {
            bool hasDesc = !string.IsNullOrWhiteSpace(it.Description);
            float rowH = hasDesc ? 66f : 46f;
            var row = CreateTopAnchored("Item", parent, new Vector2(rowW, rowH), new Vector2(0, y));

            // 脏标记圆点（改动后显示）
            var dotRt = UiKit.Rect("Dirty", row, new Vector2(10, 10), new Vector2(-rowW / 2 + 18, 0));
            var dotImg = UiKit.AddImage(dotRt, new Color(0.95f, 0.70f, 0.25f), false);
            dotRt.gameObject.SetActive(false);

            // 左侧标签（有描述时上移）
            float labelY = hasDesc ? 12f : 0f;
            var labelText = UiKit.Text(row, it.Label ?? it.Key, 20, new Vector2(330, 28),
                new Vector2(-rowW / 2 + 185, labelY), UiKit.ColText, TextAnchor.MiddleLeft);

            // 描述文字（灰色小字）
            if (hasDesc)
            {
                string desc = it.Description;
                if (desc.Length > 76) desc = desc.Substring(0, 76) + "…";
                UiKit.Text(row, desc, 14, new Vector2(560, 22),
                    new Vector2(-rowW / 2 + 300, -14f), new Color(0.58f, 0.58f, 0.64f), TextAnchor.MiddleLeft);
            }

            // 依赖项置灰
            if (!string.IsNullOrEmpty(it.DependsOn))
            {
                // M3 暂不做依赖联动，仅标注
                labelText.color = UiKit.ColTextDim;
            }

            try
            {
                switch (it.Kind)
                {
                    case ItemKind.Toggle:
                        _controls.Add(new ToggleControl(row, it, new Vector2(rowW / 2 - 130, labelY)));
                        break;

                    case ItemKind.Slider:
                        _controls.Add(new SliderControl(row, it, new Vector2(0, labelY), rowW));
                        break;

                    case ItemKind.Number:
                        _controls.Add(new NumberControl(row, it, new Vector2(0, labelY), rowW));
                        break;

                    case ItemKind.Input:
                        _controls.Add(new TextInputControl(row, it, new Vector2(0, labelY), rowW));
                        break;

                    case ItemKind.Keybind:
                        _controls.Add(new KeybindControl(row, it, new Vector2(0, labelY), rowW));
                        break;

                    case ItemKind.Button:
                        _controls.Add(new ButtonControl(row, it, new Vector2(0, labelY),
                            () => OnButtonClicked(it)));
                        break;

                    case ItemKind.Text:
                    default:
                        // 只读项：显示原值 + 类型标签
                        string v = it.CurrentValue ?? "";
                        if (v.Length > 48) v = v.Substring(0, 48) + "…";
                        UiKit.Text(row, string.IsNullOrEmpty(v) ? "(空)" : v, 17,
                            new Vector2(420, rowH), new Vector2(rowW / 2 - 300, 0),
                            UiKit.ColTextDim, TextAnchor.MiddleRight);
                        // 类型标签（说明为什么不可编辑）
                        UiKit.Text(row, "只读 · " + (it.Label != it.Key ? "" : "") + "不可编辑", 13,
                            new Vector2(120, rowH), new Vector2(rowW / 2 - 60, 0),
                            new Color(0.42f, 0.42f, 0.48f), TextAnchor.MiddleRight);
                        break;
                }
            }

            catch (Exception e)
            {
                Logx.Error($"[UI] 控件创建失败 {it.Key}: {e.Message}");
                UiKit.Text(row, "⚠ " + e.Message, 16, new Vector2(400, rowH),
                    new Vector2(rowW / 2 - 240, 0), new Color(0.9f, 0.4f, 0.4f), TextAnchor.MiddleRight);
            }
            // 构造结束后校正脏标记（刚打开面板不应有脏项）
            if (_controls.Count > 0)
            {
                var last = _controls[_controls.Count - 1];
                if (last != null && last.Item == it) last.InitDirtyState();
                else
                {
                    // 控件可能创建失败，找到对应项校正
                    foreach (var cc in _controls)
                        if (cc != null && cc.Item == it) { cc.InitDirtyState(); break; }
                }
            }

            // 记录行与脏标记的关联
            _rowMarkers[it] = dotRt.gameObject;


            return y - rowH - 4;
        }

        private static void OnButtonClicked(ConfigItem it)
        {
            try
            {
                Logx.Info($"[UI] 按钮点击: {it.Label} action={it.Action}");

                if (!string.IsNullOrEmpty(it.Confirm))
                {
                    ShowConfirm(it.Confirm, () => DoAction(it));
                    return;
                }
                DoAction(it);
            }
            catch (Exception e)
            {
                Logx.Error("[UI] 按钮处理异常: " + e.Message);
                SetHint("按钮执行失败：" + e.Message, true);
            }
        }

        private static void DoAction(ConfigItem it)
        {
            try
            {
                // reset_section 由 UI 层处理：把当前分组下的控件恢复默认
                if (it.Action != null && it.Action.StartsWith("actions_reset_section", StringComparison.OrdinalIgnoreCase))
                {
                    ResetCurrentGroup(it);
                    return;
                }
                if (it.Action != null && it.Action == "builtin:reset_section")
                {
                    ResetCurrentGroup(it);
                    return;
                }

                SetHint("执行中：" + (it.Label ?? it.Action), false);
                Actions.ActionRouter.Execute(it.Action, it.Params);
                SetHint("已执行：" + (it.Label ?? it.Action), false);
            }
            catch (Exception e)
            {
                SetHint("执行失败：" + e.Message, true);
                Logx.Error("[UI] 动作失败: " + e);
            }
        }

        /// <summary>把按钮所在分组的所有控件恢复默认值。</summary>
        private static void ResetCurrentGroup(ConfigItem btn)
        {
            try
            {
                // 找按钮所属分组
                ConfigGroup target = null;
                if (_selected >= 0 && _selected < _schemas.Count)
                {
                    foreach (var g in _schemas[_selected].Groups)
                    {
                        foreach (var i in g.Items)
                            if (i == btn) { target = g; break; }
                        if (target != null) break;
                    }
                }
                if (target == null) { SetHint("找不到所属分组", true); return; }

                int n = 0;
                foreach (var ctl in _controls)
                {
                    if (ctl == null) continue;
                    // 跳过按钮与只读项：它们没有"默认值"概念
                    if (ctl.Item.Kind == ItemKind.Button || ctl.Item.Kind == ItemKind.Text) continue;

                    bool inGroup = false;
                    foreach (var i in target.Items)
                        if (i == ctl.Item) { inGroup = true; break; }
                    if (!inGroup) continue;
                    ctl.ResetToDefault();
                    n++;
                }

                if (n == 0)
                {
                    SetHint($"「{target.Title ?? target.Section}」分组没有可恢复的配置项", true);
                    return;
                }
                RefreshDirtyVisual();
                UpdateFooterState();
                SetHint($"「{target.Title ?? target.Section}」已恢复 {n} 项为默认值，记得保存", false);
                Logx.Info($"[动作] reset_section {target.Section}：{n} 项");
            }
            catch (Exception e)
            {
                SetHint("恢复默认失败：" + e.Message, true);
                Logx.Error("[动作] reset_section 失败: " + e);
            }
        }

        /// <summary>诊断面板：显示扫描问题与统计信息。</summary>
        private static void ShowDiagnostics()
        {
            try
            {
                if (_confirmRoot != null) { UnityEngine.Object.Destroy(_confirmRoot); _confirmRoot = null; }

                var root = UiKit.Stretch("DiagMask", _canvasGo.transform, 0, 0, 0, 0);
                UiKit.AddImage(root, new Color(0, 0, 0, 0.7f));
                _confirmRoot = root.gameObject;

                float DW = 900f, DH = 620f;
                var box = UiKit.Rect("Box", root, new Vector2(DW, DH), Vector2.zero);
                UiKit.AddImage(box, new Color(0.12f, 0.12f, 0.16f, 1f));

                var bar = UiKit.Stretch("Bar", box, 0, 0, 0, DH - 8);
                UiKit.AddImage(bar, new Color(0.35f, 0.55f, 0.75f));

                UiKit.Text(box, "诊断信息", 26, new Vector2(DW - 40, 40),
                    new Vector2(0, DH / 2 - 40), UiKit.ColText, TextAnchor.MiddleCenter);

                float y = DH / 2 - 90;
                float left = -DW / 2 + 40;
                float w = DW - 80;

                // 统计
                int modCount = _schemas?.Count ?? 0;
                int totalItems = 0;
                if (_schemas != null) foreach (var s in _schemas) totalItems += s.TotalItems;

                // 根目录定位状态
                string gameRoot = SchemaDiscovery.GameRoot ?? "(未知)";
                bool rootOk = !string.IsNullOrEmpty(gameRoot) &&
                              System.IO.File.Exists(System.IO.Path.Combine(gameRoot, "ZEDZONE.exe"));

                AddDiagLine(box, left, ref y, w, "游戏根目录", rootOk ? "✓ 已定位" : "✗ 定位失败",
                    rootOk ? new Color(0.5f, 0.85f, 0.5f) : new Color(0.95f, 0.4f, 0.4f));
                UiKit.Text(box, gameRoot.Length > 70 ? gameRoot.Substring(0, 70) + "…" : gameRoot,
                    14, new Vector2(w - 20, 22), new Vector2(left + w / 2 + 10, y),
                    rootOk ? UiKit.ColTextDim : new Color(0.95f, 0.6f, 0.5f), TextAnchor.MiddleLeft);
                y -= 28;

                if (!rootOk)
                {
                    UiKit.Text(box, "定位失败时，请在 BepInEx\\config\\zedzone.modconfigpanel.cfg 中设置 GameRootOverride",
                        14, new Vector2(w - 20, 22), new Vector2(left + w / 2 + 10, y),
                        new Color(0.95f, 0.75f, 0.4f), TextAnchor.MiddleLeft);
                    y -= 24;
                    UiKit.Text(box, "然后把 BepInEx\\ModConfigPanel.log 反馈给作者",
                        14, new Vector2(w - 20, 22), new Vector2(left + w / 2 + 10, y),
                        new Color(0.95f, 0.75f, 0.4f), TextAnchor.MiddleLeft);
                    y -= 28;
                }

                AddDiagLine(box, left, ref y, w, "已加载模组", modCount.ToString(), UiKit.ColAccent);
                AddDiagLine(box, left, ref y, w, "配置项总数", totalItems.ToString(), UiKit.ColAccent);
                y -= 6;
                UiKit.Text(box, "配置文件目录", 17, new Vector2(200, 26),
                    new Vector2(left + 100, y), UiKit.ColTextDim, TextAnchor.MiddleLeft);
                y -= 26;
                UiKit.Text(box, SchemaDiscovery.ConfigDir ?? "(未知)", 15, new Vector2(w - 20, 26),
                    new Vector2(left + w / 2 + 10, y), UiKit.ColTextDim, TextAnchor.MiddleLeft);
                y -= 30;

                y -= 16;
                var issues = SchemaDiscovery.Issues;
                AddDiagLine(box, left, ref y, w, "扫描问题", issues == null || issues.Count == 0 ? "无" : issues.Count + " 个",
                    issues == null || issues.Count == 0 ? new Color(0.5f, 0.85f, 0.5f) : new Color(0.95f, 0.55f, 0.35f));

                int shown = 0;
                if (issues != null)
                {
                    foreach (var isu in issues)
                    {
                        if (shown++ >= 8) break;
                        string line = $"[{isu.Stage}] {isu.Target} → {isu.Reason}";
                        if (line.Length > 90) line = line.Substring(0, 90) + "…";
                        AddDiagLine(box, left, ref y, w, "", line, new Color(0.95f, 0.6f, 0.5f), 15);
                    }
                }

                y -= 16;
                AddDiagLine(box, left, ref y, w, "日志文件", "BepInEx\\ModConfigPanel.log", UiKit.ColTextDim);
                AddDiagLine(box, left, ref y, w, "提示", "若某个模组未显示，多半是 cfg 或 modconfig.json 格式问题", UiKit.ColTextDim, 15);

                UiKit.Button(box, "打开日志目录", new Vector2(180, 46), new Vector2(-110, -DH / 2 + 40),
                    () => Actions.ActionRouter.Execute("builtin:show_log", null), UiKit.ColBtn, 18);
                UiKit.Button(box, "关闭", new Vector2(140, 46), new Vector2(110, -DH / 2 + 40),
                    () => { UnityEngine.Object.Destroy(_confirmRoot); _confirmRoot = null; },
                    new Color(0.30f, 0.32f, 0.40f), 18);
            }
            catch (Exception e) { Logx.Error("[UI] 诊断面板失败: " + e.Message); }
        }

        private static void AddDiagLine(Transform parent, float left, ref float y, float w,
            string label, string value, Color valColor, int size = 17)
        {
            if (!string.IsNullOrEmpty(label))
                UiKit.Text(parent, label, size, new Vector2(200, 26),
                    new Vector2(left + 100, y), UiKit.ColTextDim, TextAnchor.MiddleLeft);
            UiKit.Text(parent, value ?? "", size, new Vector2(label.Length > 0 ? w - 220 : w - 20, 26),
                new Vector2(label.Length > 0 ? left + w / 2 - 20 : left + w / 2 + 10, y),
                valColor, label.Length > 0 ? TextAnchor.MiddleLeft : TextAnchor.MiddleLeft);
            y -= 30;
        }

        /// <summary>简易确认框：遮罩 + 文字 + 确定/取消。</summary>
        private static GameObject _confirmRoot;
        private static void ShowConfirm(string message, Action onYes)
        {
            try
            {
                if (_confirmRoot != null) { UnityEngine.Object.Destroy(_confirmRoot); _confirmRoot = null; }

                var root = UiKit.Stretch("ConfirmMask", _canvasGo.transform, 0, 0, 0, 0);
                UiKit.AddImage(root, new Color(0, 0, 0, 0.65f));
                _confirmRoot = root.gameObject;

                var box = UiKit.Rect("Box", root, new Vector2(700, 280), Vector2.zero);
                UiKit.AddImage(box, new Color(0.14f, 0.14f, 0.19f, 1f));

                // 边框色条
                var bar = UiKit.Stretch("Bar", box, 0, 0, 0, 262);
                UiKit.AddImage(bar, UiKit.ColAccent);

                UiKit.Text(box, message, 22, new Vector2(640, 100),
                    new Vector2(0, 40), UiKit.ColText, TextAnchor.MiddleCenter);

                UiKit.Button(box, "确定", new Vector2(160, 50), new Vector2(-100, -70), () =>
                {
                    UnityEngine.Object.Destroy(_confirmRoot);
                    _confirmRoot = null;
                    onYes?.Invoke();
                }, new Color(0.30f, 0.55f, 0.30f));

                UiKit.Button(box, "取消", new Vector2(160, 50), new Vector2(100, -70), () =>
                {
                    UnityEngine.Object.Destroy(_confirmRoot);
                    _confirmRoot = null;
                }, UiKit.ColBtn);
            }
            catch (Exception e) { Logx.Error("[UI] 确认框失败: " + e.Message); onYes?.Invoke(); }
        }

        // ---------------- 保存 ----------------
        private static void OnSave()
        {
            try
            {
                if (_selected < 0 || _selected >= _schemas.Count)
                {
                    SetHint("请先选择一个模组", true);
                    return;
                }

                var s = _schemas[_selected];
                string path = s.ConfigAbsPath;
                if (string.IsNullOrEmpty(path))
                {
                    string root = SchemaDiscovery.GameRoot;
                    path = System.IO.Path.Combine(root, s.ConfigFile.Replace('/', '\\'));
                }

                if (!System.IO.File.Exists(path))
                {
                    SetHint("配置文件不存在，请先运行一次游戏生成：" + System.IO.Path.GetFileName(path), true);
                    return;
                }

                // 收集改动：按 section 分组
                var changes = new Dictionary<string, Dictionary<string, string>>();
                int collected = 0;

                foreach (var g in s.Groups)
                {
                    foreach (var it in g.Items)
                    {
                        if (it.Kind == ItemKind.Button || it.Kind == ItemKind.Text) continue;
                        // Keybind 与 Number/Toggle 一样参与保存

                        Control ctl = null;
                        foreach (var x in _controls)
                        {
                            if (x != null && x.Item == it) { ctl = x; break; }
                        }
                        if (ctl == null) continue;
                        if (!ctl.Dirty) continue;      // 只保存改动过的项

                        string val = ctl.GetValue();
                        if (val == null) continue;

                        if (!changes.TryGetValue(g.Section, out var sec))
                        {
                            sec = new Dictionary<string, string>();
                            changes[g.Section] = sec;
                        }
                        sec[it.Key] = val;
                        collected++;
                    }
                }

                if (collected == 0)
                {
                    SetHint("没有需要保存的改动", false);
                    return;
                }

                var res = IO.CfgFileWriter.SafeWrite(path, changes);
                if (res.Success)
                {
                    string suffix = s.RestartRequired ? "，重启游戏后生效" : "";
                    SetHint($"{res.Message}{suffix}", false);
                    Logx.Info("[保存] " + res.Message + suffix);

                    // 清除脏标记：把当前值固化为新的基准值（不重建面板，避免跳动）
                    foreach (var ctl in _controls)
                    {
                        if (ctl == null) continue;
                        ctl.OriginalValue = ctl.GetValue();
                        ctl.Dirty = false;
                    }
                    RefreshDirtyVisual();
                    UpdateFooterState();
                }
                else
                {
                    SetHint(res.Message, true);
                }
            }
            catch (Exception e)
            {
                SetHint("保存异常：" + e.Message, true);
                Logx.Error("[保存] " + e);
            }
        }

        /// <summary>全局恢复：当前模组所有配置项恢复为默认值（不自动保存）。</summary>
        private static void OnResetAll()
        {
            if (_selected < 0 || _selected >= _schemas.Count) return;
            var s = _schemas[_selected];
            ShowConfirm($"把「{s.DisplayName}」的全部配置恢复默认值？\n（不会自动保存，需要点保存才写入）", () =>
            {
                try
                {
                    int n = 0;
                    foreach (var ctl in _controls)
                    {
                        if (ctl == null) continue;
                        if (ctl.Item.Kind == ItemKind.Button || ctl.Item.Kind == ItemKind.Text) continue;
                        ctl.ResetToDefault();
                        n++;
                    }
                    RefreshDirtyVisual();
                    UpdateFooterState();
                    SetHint($"已恢复 {n} 项为默认值，记得保存", false);
                    Logx.Info($"[动作] 全部恢复默认：{n} 项");
                }
                catch (Exception e) { SetHint("恢复失败：" + e.Message, true); }
            });
        }

        private static void OnRevert()
        {
            try
            {
                int n = 0;
                foreach (var ctl in _controls)
                {
                    if (ctl == null || !ctl.Dirty) continue;
                    ctl.Revert();
                    n++;
                }
                RefreshDirtyVisual();
                UpdateFooterState();
                SetHint($"已撤销 {n} 项改动", false);
                Logx.Info($"[UI] 撤销 {n} 项");
            }
            catch (Exception e) { SetHint("撤销失败：" + e.Message, true); }
        }

        /// <summary>刷新所有行的脏标记视觉。</summary>
        private static void RefreshDirtyVisual()
        {
            foreach (var kv in _rowMarkers)
            {
                if (kv.Value == null) continue;
                Control ctl = FindControl(kv.Key);
                bool dirty = ctl != null && ctl.Dirty;
                kv.Value.gameObject.SetActive(dirty);
            }
        }

        private static Control FindControl(ConfigItem it)
        {
            foreach (var ctl in _controls)
                if (ctl != null && ctl.Item == it) return ctl;
            return null;
        }

        /// <summary>更新底部保存按钮与提示（有改动时高亮）。</summary>
        private static void UpdateFooterState()
        {
            int dirtyCount = 0;
            foreach (var ctl in _controls)
                if (ctl != null && ctl.Dirty) dirtyCount++;

            if (_btnSave != null)
            {
                var img = _btnSave.GetComponent<UnityEngine.UI.Image>();
                if (img != null)
                    img.color = dirtyCount > 0
                        ? new Color(0.30f, 0.62f, 0.32f)
                        : new Color(0.22f, 0.32f, 0.24f);
            }
            if (_btnRevert != null) _btnRevert.gameObject.SetActive(dirtyCount > 0);

            if (dirtyCount > 0 && _saveHint != null && !_hintLocked)
            {
                _saveHint.text = $"有 {dirtyCount} 项未保存的改动";
                _saveHint.color = new Color(0.95f, 0.78f, 0.35f);
            }
        }

        /// <summary>是否有未保存改动。</summary>
        private static bool HasUnsavedChanges()
        {
            foreach (var ctl in _controls)
                if (ctl != null && ctl.Dirty) return true;
            return false;
        }

        private static void SetHint(string text, bool isError)
        {
            _hintLocked = false;
            if (_saveHint == null) return;
            _saveHint.text = text;
            _saveHint.color = isError ? new Color(0.95f, 0.45f, 0.45f) : new Color(0.55f, 0.85f, 0.55f);
        }

        /// <summary>重新扫描并保持当前选中项。</summary>
        private static void RefreshSchemasKeepSelection()
        {
            try
            {
                string curGuid = (_selected >= 0 && _selected < _schemas.Count)
                    ? _schemas[_selected].ModGuid : null;

                var list = SchemaDiscovery.ScanAll();
                PanelPlugin.Schemas = list;
                _schemas = list;

                _selected = 0;
                if (curGuid != null)
                {
                    for (int i = 0; i < list.Count; i++)
                        if (list[i].ModGuid == curGuid) { _selected = i; break; }
                }

                BuildModList();
                BuildRightContent();
            }
            catch (Exception e) { Logx.Error("[UI] 复位失败: " + e.Message); }
        }

        // ---------------- 刷新 ----------------
        private static void OnRefresh()
        {
            if (HasUnsavedChanges())
            {
                ShowConfirm("刷新会丢弃未保存的改动，继续吗？", DoRefresh);
                return;
            }
            DoRefresh();
        }

        private static void DoRefresh()
        {
            try
            {
                Logx.Info("[UI] 手动刷新");
                int before = _schemas?.Count ?? 0;
                RefreshSchemasKeepSelection();
                int after = _schemas?.Count ?? 0;

                string msg = after > before
                    ? $"已刷新，新增 {after - before} 个模组（共 {after} 个）"
                    : $"已刷新，共 {after} 个模组";
                SetHint(msg, false);
                if (_statusText != null) _statusText.text = msg;
            }
            catch (Exception e) { Logx.Error("[UI] 刷新失败: " + e); }
        }
    }
}
