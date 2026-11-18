using System.Collections.Generic;

namespace ModConfigPanel.Schema
{
    /// <summary>配置项控件类型。</summary>
    public enum ItemKind
    {
        Slider,
        Number,
        Toggle,
        Button,
        Text,
        Keybind,       // 按键绑定：点击后按任意键
        Input,         // 文本输入框
    }

    /// <summary>单个配置项声明。</summary>
    public class ConfigItem
    {
        public string Key;              // cfg 键名
        public string Label;            // 显示名
        public ItemKind Kind;
        public string Description;      // 说明
        public string DependsOn;        // 依赖的另一项 key

        // Slider / Number
        public float Min;
        public float Max;
        public float Step = 0.01f;
        public float DefaultFloat;
        public bool Integer;
        public string Unit;

        // Toggle
        public bool DefaultBool;

        // Button
        public string Action;           // builtin:xxx 或 callback:Ns.Class.Method
        public string Confirm;          // 非空则弹确认框
        public Dictionary<string, string> Params;

        // Text
        public string Content;

        /// <summary>默认值来源：cfg = 来自 cfg 的 # Default value；decl = 声明里显式指定</summary>
        public string DefaultSource = "cfg";

        // 运行时填充：从 cfg 读到的当前值
        public string CurrentValue;
    }

    /// <summary>配置分组，对应 cfg 的一个 section。</summary>
    public class ConfigGroup
    {
        public string Section;
        public string Title;
        public List<ConfigItem> Items = new List<ConfigItem>();
    }

    /// <summary>一个模组的完整配置声明。</summary>
    public class ModSchema
    {
        public int SchemaVersion = 1;
        public string ModGuid;
        public string DisplayName;
        public string Description;
        public string ConfigFile;       // 相对游戏根目录
        public bool RestartRequired = true;
        public List<ConfigGroup> Groups = new List<ConfigGroup>();

        /// <summary>来源：json / cfg-auto / sdk</summary>
        public string Source = "unknown";

        /// <summary>cfg 文件绝对路径（运行时填充）</summary>
        public string ConfigAbsPath;

        public int TotalItems
        {
            get
            {
                int n = 0;
                foreach (var g in Groups) n += g.Items.Count;
                return n;
            }
        }
    }
}
