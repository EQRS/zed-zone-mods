using System;
using System.Collections.Generic;
using System.IO;

namespace ModConfigPanel.Schema
{
    /// <summary>
    /// 配置发现器。
    /// 第一层：扫描 <游戏目录>\BepInEx\config\*.cfg，自动生成简易 Schema。
    /// 第二层：扫描 <模组目录>\modconfig.json，覆盖/增强对应 GUID 的 Schema。
    /// </summary>
    public static class SchemaDiscovery
    {
        /// <summary>扫描过程中遇到的问题（供诊断面板展示）。</summary>
        public class ScanIssue
        {
            public string Target;    // 文件路径或模组目录
            public string Reason;    // 失败原因
            public string Stage;     // cfg / json / other
        }

        public static readonly List<ScanIssue> Issues = new List<ScanIssue>();

        public const string DeclarationFileName = "modconfig.json";

        public static string GameRoot { get; private set; }
        public static string ConfigDir { get; private set; }

        public static void Init(string gameRoot)
        {
            GameRoot = gameRoot;
            ConfigDir = Path.Combine(gameRoot, "BepInEx", "config");
        }

        /// <summary>模组可能存放声明文件的目录列表。</summary>
        public static List<string> GetModRoots()
        {
            var list = new List<string>();
            if (string.IsNullOrEmpty(GameRoot)) return list;

            // 本地 Mods
            string mods = Path.Combine(GameRoot, "Mods");
            if (Directory.Exists(mods))
                foreach (var d in Directory.GetDirectories(mods)) list.Add(d);

            // 创意工坊（通过 appmanifest 定位 Steam 库）
            try
            {
                string steamApps = Path.GetFullPath(Path.Combine(GameRoot, "..", ".."));
                string workshop = Path.Combine(steamApps, "workshop", "content", "1211600");
                if (Directory.Exists(workshop))
                    foreach (var d in Directory.GetDirectories(workshop)) list.Add(d);
            }
            catch { }

            return list;
        }

        /// <summary>完整扫描：先 cfg 自动解析，再用 json 声明覆盖。</summary>
        public static List<ModSchema> ScanAll()
        {
            var result = new List<ModSchema>();
            var byGuid = new Dictionary<string, ModSchema>(StringComparer.OrdinalIgnoreCase);
            Issues.Clear();

            // ---- 第一层：cfg ----
            if (!string.IsNullOrEmpty(ConfigDir) && Directory.Exists(ConfigDir))
            {
                foreach (string f in Directory.GetFiles(ConfigDir, "*.cfg"))
                {
                    try
                    {
                        var s = BuildFromCfg(f);
                        if (s == null) continue;
                        result.Add(s);
                        byGuid[s.ModGuid] = s;
                    }
                    catch (Exception e)
                    {
                        Logx.Warn("解析 cfg 失败 " + Path.GetFileName(f) + ": " + e.Message);
                        Issues.Add(new ScanIssue
                        {
                            Target = Path.GetFileName(f),
                            Reason = e.Message,
                            Stage = "cfg",
                        });
                    }
                }
            }

            // ---- 第二层：modconfig.json ----
            foreach (string root in GetModRoots())
            {
                string decl = Path.Combine(root, DeclarationFileName);
                if (!File.Exists(decl)) continue;
                try
                {
                    var s = SchemaLoader.LoadFromJson(decl);
                    if (s == null) continue;
                    if (byGuid.TryGetValue(s.ModGuid, out var existing))
                    {
                        // 增量合并：cfg 自动结果为基底，声明只覆盖/补充指定的项
                        MergeDeclaration(existing, s);
                        Logx.Info("已合并声明: " + s.ModGuid +
                                  $" (items {existing.TotalItems})");
                    }
                    else
                    {
                        result.Add(s);
                        byGuid[s.ModGuid] = s;
                        Logx.Info("仅声明（无对应 cfg）: " + s.ModGuid);
                    }
                }
                catch (Exception e)
                {
                    Logx.Warn("解析 " + DeclarationFileName + " 失败 @ " + root + ": " + e.Message);
                    Issues.Add(new ScanIssue
                    {
                        Target = root,
                        Reason = e.Message,
                        Stage = "json",
                    });
                }
            }

            // 排序：按 DisplayName
            result.Sort((a, b) => string.Compare(
                a.DisplayName ?? a.ModGuid, b.DisplayName ?? b.ModGuid, StringComparison.OrdinalIgnoreCase));

            return result;
        }

        /// <summary>
        /// 把声明合并进自动解析结果。
        /// 规则：
        ///   1. 顶层字段：声明有值则覆盖
        ///   2. 分组：按 section 名匹配；声明里有、自动结果里没有的分组 → 新增
        ///   3. 配置项：按 key 匹配；匹配到 → 用声明的显示信息覆盖（保留自动解析的当前值与范围兜底）
        ///              未匹配到 → 新增到该分组末尾
        ///   4. 自动结果里存在但声明未提及的项 → 原样保留
        /// </summary>
        private static void MergeDeclaration(ModSchema auto, ModSchema decl)
        {
            if (!string.IsNullOrEmpty(decl.DisplayName)) auto.DisplayName = decl.DisplayName;
            if (!string.IsNullOrEmpty(decl.Description)) auto.Description = decl.Description;
            if (!string.IsNullOrEmpty(decl.ConfigFile)) auto.ConfigFile = decl.ConfigFile;
            auto.RestartRequired = decl.RestartRequired;
            auto.Source = "json+cfg";

            foreach (var dg in decl.Groups)
            {
                ConfigGroup target = null;
                foreach (var g in auto.Groups)
                {
                    if (string.Equals(g.Section, dg.Section, StringComparison.OrdinalIgnoreCase))
                    { target = g; break; }
                }
                if (target == null)
                {
                    target = new ConfigGroup { Section = dg.Section, Title = dg.Title };
                    auto.Groups.Add(target);
                }
                else if (!string.IsNullOrEmpty(dg.Title))
                {
                    target.Title = dg.Title;
                }

                foreach (var di in dg.Items)
                {
                    ConfigItem exist = null;
                    foreach (var it in target.Items)
                    {
                        if (string.Equals(it.Key, di.Key, StringComparison.Ordinal))
                        { exist = it; break; }
                    }

                    if (exist == null)
                    {
                        target.Items.Add(di);
                        continue;
                    }

                    // 已存在：用声明的显示信息覆盖，但保留自动解析到的当前值
                    string keepValue = exist.CurrentValue;
                    exist.Label = di.Label;
                    exist.Kind = di.Kind;
                    if (!string.IsNullOrEmpty(di.Description)) exist.Description = di.Description;
                    exist.DependsOn = di.DependsOn;
                    exist.Unit = di.Unit;
                    exist.Confirm = di.Confirm;
                    exist.Action = di.Action;
                    exist.Params = di.Params;
                    exist.Content = di.Content;

                    // 范围：声明若显式给了 min/max 就用声明的（当作作者明确指定）
                    if (di.Kind == ItemKind.Slider || di.Kind == ItemKind.Number)
                    {
                        if (di.Max > di.Min) { exist.Min = di.Min; exist.Max = di.Max; }
                        if (di.Step > 0f) exist.Step = di.Step;
                        exist.Integer = di.Integer;
                        // 关键：只有声明显式写了 default 才覆盖，否则保留 cfg 里的 # Default value
                        if (di.DefaultFloat != 0f) { exist.DefaultFloat = di.DefaultFloat; exist.DefaultSource = "decl"; }
                    }
                    else if (di.Kind == ItemKind.Toggle)
                    {
                        // 同理：声明未写 default 时沿用 cfg 的 Default value
                        if (di.DefaultBool) exist.DefaultBool = true;
                    }

                    exist.CurrentValue = keepValue;   // 保留 cfg 里的真实当前值
                }
            }
        }

        /// <summary>从 cfg 文件自动生成简易 Schema。</summary>
        private static ModSchema BuildFromCfg(string path)
        {
            var parsed = CfgParser.Parse(path);
            var schema = new ModSchema
            {
                SchemaVersion = 1,
                ModGuid = parsed.Guid,
                DisplayName = string.IsNullOrEmpty(parsed.PluginName) ? parsed.Guid : parsed.PluginName,
                ConfigFile = "BepInEx/config/" + Path.GetFileName(path),
                ConfigAbsPath = path,
                Source = "cfg-auto",
            };

            foreach (var sec in parsed.Sections)
            {
                var g = new ConfigGroup { Section = sec.Name, Title = sec.Name };
                foreach (var k in sec.Keys)
                {
                    var item = new ConfigItem
                    {
                        Key = k.Key,
                        Label = k.Key,
                        Kind = KindInference.InferKind(k),
                        Description = k.Comment,
                        CurrentValue = k.Value,
                    };
                    if (k.Min.HasValue) item.Min = k.Min.Value;
                    if (k.Max.HasValue) item.Max = k.Max.Value;
                    if (k.DefaultValue.HasValue) { item.DefaultFloat = k.DefaultValue.Value; item.DefaultSource = "cfg"; }
                    if (KindInference.IsInteger(k)) item.Integer = true;

                    // 当前值解析（空值用默认值兜底）
                    if (item.Kind == ItemKind.Number && string.IsNullOrWhiteSpace(k.Value))
                        item.CurrentValue = item.DefaultFloat.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    if (item.Kind == ItemKind.Toggle)
                    {
                        // 默认值优先取 cfg 的 # Default value，没有才用当前值
                        if (!string.IsNullOrEmpty(k.DefaultRaw))
                            item.DefaultBool = string.Equals(k.DefaultRaw, "true", StringComparison.OrdinalIgnoreCase);
                        else
                            item.DefaultBool = string.Equals(k.Value, "true", StringComparison.OrdinalIgnoreCase);
                    }
                    if (KindInference.IsReadOnly(k)) item.Kind = ItemKind.Text;
                    g.Items.Add(item);
                }
                if (g.Items.Count > 0) schema.Groups.Add(g);
            }

            return schema.TotalItems > 0 ? schema : null;
        }
    }
}
