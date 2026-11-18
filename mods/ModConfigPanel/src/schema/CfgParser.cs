using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace ModConfigPanel.Schema
{
    /// <summary>从 BepInEx cfg 自动解析出的配置项。</summary>
    public class ParsedKey
    {
        public string Key;
        public string Value;
        public string Type;          // Single / Boolean / String / ...
        public string Comment;
        public float? Min, Max;
        public float? DefaultValue;
        public string DefaultRaw;
    }

    public class ParsedSection
    {
        public string Name;
        public List<ParsedKey> Keys = new List<ParsedKey>();
    }

    public class ParsedCfg
    {
        public string FilePath;
        public string Guid;
        public string PluginName;
        public string PluginVersion;
        public List<ParsedSection> Sections = new List<ParsedSection>();

        public int TotalKeys
        {
            get { int n = 0; foreach (var s in Sections) n += s.Keys.Count; return n; }
        }
    }

    /// <summary>
    /// BepInEx cfg 解析器。
    /// 只读取，不写入；写入由 io/CfgFileIO 负责。
    /// </summary>
    public static class CfgParser
    {
        private static readonly Regex RangeRe =
            new Regex(@"From\s+(-?[\d.]+)\s+to\s+(-?[\d.]+)", RegexOptions.Compiled);
        private static readonly Regex VersionRe =
            new Regex(@"\sv([\d.]+)\s*$", RegexOptions.Compiled);

        public static ParsedCfg Parse(string path)
        {
            var doc = new ParsedCfg { FilePath = path };
            string[] lines;
            try { lines = File.ReadAllLines(path); }
            catch (Exception e) { throw new IOException("读取失败: " + e.Message, e); }

            ParsedSection cur = null;
            ParsedKey pending = null;
            string lastComment = null;

            foreach (string raw in lines)
            {
                string line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.StartsWith("## Plugin GUID:"))
                {
                    doc.Guid = line.Substring(15).Trim();
                    continue;
                }
                if (line.StartsWith("## Settings file was created by plugin"))
                {
                    string rest = line.Substring(39).Trim();
                    var m = VersionRe.Match(rest);
                    if (m.Success)
                    {
                        doc.PluginVersion = m.Groups[1].Value;
                        doc.PluginName = rest.Substring(0, m.Index).Trim();
                    }
                    else doc.PluginName = rest;
                    continue;
                }

                // section 行判定：以 [ 开头、以 ] 结尾，且行内不含 =（避免误判 [Key = ] 这类值行）
                if (line.StartsWith("[") && line.EndsWith("]") && line.IndexOf('=') < 0)
                {
                    cur = new ParsedSection { Name = line.Substring(1, line.Length - 2) };
                    doc.Sections.Add(cur);
                    pending = null;
                    lastComment = null;
                    continue;
                }

                if (line.StartsWith("# Setting type:"))
                {
                    if (pending == null) pending = new ParsedKey();
                    pending.Type = line.Substring(15).Trim();
                    continue;
                }
                if (line.StartsWith("# Default value:"))
                {
                    if (pending == null) pending = new ParsedKey();
                    string dv = line.Substring(16).Trim();
                    pending.DefaultRaw = dv;
                    float f;
                    if (float.TryParse(dv, NumberStyles.Float, CultureInfo.InvariantCulture, out f))
                        pending.DefaultValue = f;
                    continue;
                }
                if (line.StartsWith("# Acceptable value range:"))
                {
                    if (pending == null) pending = new ParsedKey();
                    var m = RangeRe.Match(line);
                    if (m.Success)
                    {
                        pending.Min = float.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
                        pending.Max = float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                    }
                    continue;
                }
                if (line.StartsWith("##"))
                {
                    if (pending == null) lastComment = line.Substring(2).Trim();
                    continue;
                }
                if (line.StartsWith("#")) continue;

                int eq = line.IndexOf('=');
                if (eq <= 0 || cur == null) continue;

                string key = line.Substring(0, eq).Trim();
                string val = line.Substring(eq + 1).Trim();

                if (pending != null && pending.Type != null)
                {
                    pending.Key = key;
                    pending.Value = val;
                    pending.Comment = lastComment;
                    cur.Keys.Add(pending);
                    pending = null;
                }
                else
                {
                    cur.Keys.Add(new ParsedKey
                    {
                        Key = key, Value = val, Type = "Unknown", Comment = lastComment
                    });
                }
                lastComment = null;
            }

            if (string.IsNullOrEmpty(doc.Guid))
                doc.Guid = Path.GetFileNameWithoutExtension(path);

            return doc;
        }

        /// <summary>根据 cfg 里声明的类型推断控件类型。</summary>
        public static ItemKind InferKind(ParsedKey k)
        {
            if (k.Type == null) return ItemKind.Number;
            switch (k.Type)
            {
                case "Boolean": return ItemKind.Toggle;
                case "Single":
                case "Double":
                case "Int32":
                case "Int64": return ItemKind.Number;
                default: return ItemKind.Number;
            }
        }
    }
}
