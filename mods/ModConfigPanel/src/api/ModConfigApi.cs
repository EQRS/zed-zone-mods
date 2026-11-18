using System;
using System.Collections.Generic;
using ModConfigPanel.Schema;

namespace ModConfigPanel.Api
{
    /// <summary>
    /// 给模组作者使用的公开 API。
    /// 作者可以：
    ///   1. 用代码注册配置声明（比写 JSON 更灵活）
    ///   2. 通过反射软调用接入（不必引用本 DLL）
    /// </summary>
    public static class ModConfigApi
    {
        public const int ApiVersion = 1;

        /// <summary>平台是否可用（供软调用方检测）。</summary>
        public static bool IsPlatformAvailable => true;

        /// <summary>注册一个模组的配置声明。重复注册会覆盖旧的。</summary>
        public static void Register(ModConfigDeclaration decl)
        {
            try
            {
                if (decl == null) { Logx.Warn("[API] Register 收到 null"); return; }
                if (string.IsNullOrEmpty(decl.ModGuid))
                {
                    Logx.Warn("[API] 缺少 ModGuid，注册被忽略");
                    return;
                }

                var schema = decl.ToSchema();

                // 与已有列表合并（无论之前来自 cfg 还是 json，SDK 注册优先级最高）
                var list = PanelPlugin.Schemas ?? new List<ModSchema>();
                int idx = -1;
                for (int i = 0; i < list.Count; i++)
                {
                    if (string.Equals(list[i].ModGuid, schema.ModGuid, StringComparison.OrdinalIgnoreCase))
                    { idx = i; break; }
                }

                if (idx >= 0)
                {
                    // 保留自动解析得到的 CurrentValue
                    var old = list[idx];
                    foreach (var g in schema.Groups)
                    {
                        foreach (var it in g.Items)
                        {
                            foreach (var og in old.Groups)
                            {
                                foreach (var oi in og.Items)
                                {
                                    if (og.Section == g.Section && oi.Key == it.Key)
                                    {
                                        it.CurrentValue = oi.CurrentValue;
                                        break;
                                    }
                                }
                            }
                        }
                    }
                    list[idx] = schema;
                    Logx.Info($"[API] 已替换声明: {schema.ModGuid} ({schema.TotalItems} 项)");
                }
                else
                {
                    list.Add(schema);
                    Logx.Info($"[API] 已注册声明: {schema.ModGuid} ({schema.TotalItems} 项)");
                }

                list.Sort((a, b) => string.Compare(
                    a.DisplayName ?? a.ModGuid, b.DisplayName ?? b.ModGuid, StringComparison.OrdinalIgnoreCase));

                PanelPlugin.Schemas = list;
            }
            catch (Exception e)
            {
                Logx.Error("[API] Register 失败: " + e);
            }
        }

        /// <summary>注销一个模组的声明。</summary>
        public static void Unregister(string modGuid)
        {
            try
            {
                if (string.IsNullOrEmpty(modGuid) || PanelPlugin.Schemas == null) return;
                int removed = PanelPlugin.Schemas.RemoveAll(
                    s => string.Equals(s.ModGuid, modGuid, StringComparison.OrdinalIgnoreCase));
                if (removed > 0) Logx.Info($"[API] 已注销: {modGuid}");
            }
            catch (Exception e) { Logx.Error("[API] Unregister 失败: " + e); }
        }
    }

    // ---------------- 声明结构 ----------------

    public class ModConfigDeclaration
    {
        public string ModGuid;
        public string DisplayName;
        public string Description;
        public string ConfigFile;      // 相对游戏根目录，如 "BepInEx/config/xxx.cfg"
        public bool RestartRequired = true;
        public List<GroupDecl> Groups = new List<GroupDecl>();

        public GroupDecl AddGroup(string section, string title)
        {
            var g = new GroupDecl { Section = section, Title = title };
            Groups.Add(g);
            return g;
        }

        internal ModSchema ToSchema()
        {
            var s = new ModSchema
            {
                SchemaVersion = 1,
                ModGuid = ModGuid,
                DisplayName = string.IsNullOrEmpty(DisplayName) ? ModGuid : DisplayName,
                Description = Description ?? "",
                ConfigFile = ConfigFile,
                RestartRequired = RestartRequired,
                Source = "sdk",
            };
            foreach (var g in Groups)
            {
                var cg = new ConfigGroup { Section = g.Section, Title = g.Title };
                foreach (var i in g.Items) cg.Items.Add(i.ToItem());
                s.Groups.Add(cg);
            }
            return s;
        }
    }

    public class GroupDecl
    {
        public string Section;
        public string Title;
        public List<ItemDecl> Items = new List<ItemDecl>();

        public void Add(ItemDecl item) { if (item != null) Items.Add(item); }
    }

    /// <summary>配置项声明（用静态工厂方法创建）。</summary>
    public class ItemDecl
    {
        internal ConfigItem Item = new ConfigItem();

        public static ItemDecl Slider(string key, string label, float min, float max,
            float step = 0.05f, float def = 1f, string unit = null, string description = null)
        {
            var d = new ItemDecl();
            d.Item.Key = key; d.Item.Label = label; d.Item.Kind = ItemKind.Slider;
            d.Item.Min = min; d.Item.Max = max; d.Item.Step = step;
            d.Item.DefaultFloat = def; d.Item.Unit = unit; d.Item.Description = description;
            return d;
        }

        public static ItemDecl Number(string key, string label, float min, float max,
            bool integer = false, float def = 0f, string description = null)
        {
            var d = new ItemDecl();
            d.Item.Key = key; d.Item.Label = label; d.Item.Kind = ItemKind.Number;
            d.Item.Min = min; d.Item.Max = max; d.Item.Integer = integer;
            d.Item.DefaultFloat = def; d.Item.Description = description;
            return d;
        }

        public static ItemDecl Toggle(string key, string label, bool def = false, string description = null)
        {
            var d = new ItemDecl();
            d.Item.Key = key; d.Item.Label = label; d.Item.Kind = ItemKind.Toggle;
            d.Item.DefaultBool = def; d.Item.Description = description;
            return d;
        }

        public static ItemDecl Input(string key, string label, string description = null)
        {
            var d = new ItemDecl();
            d.Item.Key = key; d.Item.Label = label; d.Item.Kind = ItemKind.Input;
            d.Item.Description = description;
            return d;
        }

        public static ItemDecl Keybind(string key, string label, string description = null)
        {
            var d = new ItemDecl();
            d.Item.Key = key; d.Item.Label = label; d.Item.Kind = ItemKind.Keybind;
            d.Item.Description = description;
            return d;
        }

        public static ItemDecl Text(string key, string content)
        {
            var d = new ItemDecl();
            d.Item.Key = key; d.Item.Label = key; d.Item.Kind = ItemKind.Text;
            d.Item.Content = content;
            return d;
        }

        /// <summary>按钮：引用内置动作。</summary>
        public static ItemDecl Button(string key, string label, string action,
            string confirm = null, Dictionary<string, string> prm = null)
        {
            var d = new ItemDecl();
            d.Item.Key = key; d.Item.Label = label; d.Item.Kind = ItemKind.Button;
            d.Item.Action = action; d.Item.Confirm = confirm; d.Item.Params = prm;
            return d;
        }

        /// <summary>按钮：回调到作者的静态方法（callback:命名空间.类.方法）。</summary>
        public static ItemDecl Callback(string key, string label, string typeAndMethod,
            string confirm = null)
        {
            return Button(key, label, "callback:" + typeAndMethod, confirm);
        }

        internal ConfigItem ToItem() => Item;
    }

    /// <summary>内置动作常量，避免手写字符串。</summary>
    public static class BuiltinAction
    {
        public const string RestartGame = "builtin:restart_game";
        public const string OpenFolder = "builtin:open_folder";
        public const string OpenUrl = "builtin:open_url";
        public const string ResetSection = "builtin:reset_section";
        public const string ShowLog = "builtin:show_log";
    }
}
