using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ModConfigPanel.Schema
{
    /// <summary>
    /// modconfig.json 加载器。
    /// 使用 System.Text.Json 解析，任何字段错误都只影响该模组。
    /// </summary>
    public static class SchemaLoader
    {
        public const int SupportedSchemaVersion = 1;

        public static ModSchema LoadFromJson(string path)
        {
            string text = File.ReadAllText(path);
            using (var doc = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
            }))
            {
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    throw new FormatException("根节点必须是对象");

                int ver = GetInt(root, "schemaVersion", 1);
                if (ver > SupportedSchemaVersion)
                    throw new FormatException($"schemaVersion={ver} 高于本平台支持的 {SupportedSchemaVersion}，请升级平台");

                var schema = new ModSchema
                {
                    SchemaVersion = ver,
                    ModGuid = GetString(root, "modGuid", null),
                    DisplayName = GetString(root, "displayName", null),
                    Description = GetString(root, "description", ""),
                    ConfigFile = GetString(root, "configFile", null),
                    RestartRequired = GetBool(root, "restartRequired", true),
                    Source = "json",
                };

                if (string.IsNullOrEmpty(schema.ModGuid))
                    throw new FormatException("缺少 modGuid");
                if (string.IsNullOrEmpty(schema.ConfigFile))
                    throw new FormatException("缺少 configFile");

                if (root.TryGetProperty("groups", out var groups) && groups.ValueKind == JsonValueKind.Array)
                {
                    foreach (var g in groups.EnumerateArray())
                        schema.Groups.Add(ParseGroup(g));
                }

                // 记录 cfg 绝对路径
                schema.ConfigAbsPath = ResolveConfigPath(schema.ConfigFile);
                return schema;
            }
        }

        private static string ResolveConfigPath(string rel)
        {
            try
            {
                string root = SchemaDiscovery.GameRoot;
                if (string.IsNullOrEmpty(root)) return null;
                return Path.GetFullPath(Path.Combine(root, rel.Replace('/', Path.DirectorySeparatorChar)));
            }
            catch { return null; }
        }

        private static ConfigGroup ParseGroup(JsonElement g)
        {
            var grp = new ConfigGroup
            {
                Section = GetString(g, "section", ""),
                Title = GetString(g, "title", GetString(g, "section", "")),
            };
            if (g.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                foreach (var it in items.EnumerateArray())
                    grp.Items.Add(ParseItem(it));
            return grp;
        }

        private static ConfigItem ParseItem(JsonElement it)
        {
            string typeStr = GetString(it, "type", "number");
            var item = new ConfigItem
            {
                Key = GetString(it, "key", ""),
                Label = GetString(it, "label", GetString(it, "key", "")),
                Kind = ParseKind(typeStr),
                Description = GetString(it, "description", null),
                DependsOn = GetString(it, "dependsOn", null),
                Unit = GetString(it, "unit", null),
                Confirm = GetString(it, "confirm", null),
                Action = GetString(it, "action", null),
                Content = GetString(it, "content", null),
            };

            if (item.Kind == ItemKind.Slider || item.Kind == ItemKind.Number)
            {
                item.Min = GetFloat(it, "min", 0f);
                item.Max = GetFloat(it, "max", 100f);
                item.Step = GetFloat(it, "step", 0.01f);
                item.Integer = GetBool(it, "integer", false);
                item.DefaultFloat = GetFloat(it, "default", 0f);
            }
            else if (item.Kind == ItemKind.Toggle)
            {
                item.DefaultBool = GetBool(it, "default", false);
            }
            else if (item.Kind == ItemKind.Button)
            {
                if (string.IsNullOrEmpty(item.Action))
                    throw new FormatException($"button '{item.Key}' 缺少 action");
                if (it.TryGetProperty("params", out var p) && p.ValueKind == JsonValueKind.Object)
                {
                    item.Params = new Dictionary<string, string>();
                    foreach (var prop in p.EnumerateObject())
                        item.Params[prop.Name] = prop.Value.ToString();
                }
            }

            if (string.IsNullOrEmpty(item.Key))
                throw new FormatException("item 缺少 key");

            return item;
        }

        private static ItemKind ParseKind(string s)
        {
            switch ((s ?? "").ToLowerInvariant())
            {
                case "slider": return ItemKind.Slider;
                case "number": return ItemKind.Number;
                case "toggle": return ItemKind.Toggle;
                case "button": return ItemKind.Button;
                case "text": return ItemKind.Text;
                case "keybind": return ItemKind.Keybind;
                case "input": return ItemKind.Input;
                default: throw new FormatException("未知控件类型: " + s);
            }
        }

        private static string GetString(JsonElement e, string name, string def)
        {
            if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String)
                return v.GetString();
            return def;
        }
        private static bool GetBool(JsonElement e, string name, bool def)
        {
            if (e.TryGetProperty(name, out var v))
            {
                if (v.ValueKind == JsonValueKind.True) return true;
                if (v.ValueKind == JsonValueKind.False) return false;
            }
            return def;
        }
        private static float GetFloat(JsonElement e, string name, float def)
        {
            if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
                return (float)v.GetDouble();
            return def;
        }
        private static int GetInt(JsonElement e, string name, int def)
        {
            if (e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number)
                return v.GetInt32();
            return def;
        }
    }
}
