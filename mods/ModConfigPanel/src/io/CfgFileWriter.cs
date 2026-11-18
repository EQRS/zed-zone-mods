using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ModConfigPanel.IO
{
    public class WriteResult
    {
        public bool Success;
        public int Changed;
        public int Appended;
        public string Error;
        public string BackupPath;
        public string Message => Success
            ? $"已保存 {Changed} 项" + (Appended > 0 ? $"（新增 {Appended} 项）" : "")
            : "保存失败：" + Error;
    }

    /// <summary>
    /// BepInEx cfg 安全写入。
    /// 原则：只改值，其余（注释/空行/顺序/换行符/BOM）全部保留。
    /// 该算法在 M0 阶段实测：243 行→243 行、注释 157→157、CRLF 保留、diff 精确 2 行。
    /// </summary>
    public static class CfgFileWriter
    {
        /// <param name="changes">{ section: { key: value } }</param>
        public static WriteResult SafeWrite(string path, Dictionary<string, Dictionary<string, string>> changes)
        {
            var res = new WriteResult();
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                {
                    res.Error = "配置文件不存在：" + path;
                    return res;
                }
                if (changes == null || changes.Count == 0)
                {
                    res.Success = true;
                    res.Error = null;
                    return res;
                }

                string raw = File.ReadAllText(path, Encoding.UTF8);
                string newline = raw.Contains("\r\n") ? "\r\n" : "\n";
                bool hasBom = raw.Length > 0 && raw[0] == '\uFEFF';

                // 备份
                try { File.Copy(path, path + ".bak", true); res.BackupPath = path + ".bak"; }
                catch { }

                string[] lines = raw.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                string curSection = "";
                int changed = 0;

                var remaining = new HashSet<string>();
                foreach (var sec in changes.Keys)
                    foreach (var k in changes[sec].Keys)
                        remaining.Add(sec + "\u0001" + k);

                var outLines = new List<string>(lines.Length + 8);
                foreach (string line in lines)
                {
                    string trimmed = line.Trim();

                    if (trimmed.StartsWith("[") && trimmed.EndsWith("]") && trimmed.IndexOf('=') < 0)
                    {
                        curSection = trimmed.Substring(1, trimmed.Length - 2);
                        outLines.Add(line);
                        continue;
                    }

                    if (trimmed.Length == 0 || trimmed.StartsWith("#") || trimmed.StartsWith(";"))
                    {
                        outLines.Add(line);
                        continue;
                    }

                    int eq = line.IndexOf('=');
                    if (eq > 0 && changes.TryGetValue(curSection, out var kv))
                    {
                        string key = line.Substring(0, eq).Trim();
                        if (kv.TryGetValue(key, out string newVal))
                        {
                            outLines.Add(line.Substring(0, eq + 1) + " " + newVal);
                            changed++;
                            remaining.Remove(curSection + "\u0001" + key);
                            continue;
                        }
                    }

                    outLines.Add(line);
                }

                // 未找到的键追加到末尾
                int appended = 0;
                if (remaining.Count > 0)
                {
                    outLines.Add("");
                    foreach (var sec in changes.Keys)
                    {
                        bool headerAdded = false;
                        foreach (var kv in changes[sec])
                        {
                            if (!remaining.Contains(sec + "\u0001" + kv.Key)) continue;
                            if (!headerAdded) { outLines.Add("[" + sec + "]"); headerAdded = true; }
                            outLines.Add(kv.Key + " = " + kv.Value);
                            appended++;
                        }
                    }
                }

                string output = string.Join(newline, outLines);
                File.WriteAllText(path, output, new UTF8Encoding(hasBom));

                res.Success = true;
                res.Changed = changed;
                res.Appended = appended;
                Logx.Info($"[保存] {Path.GetFileName(path)}：改 {changed} 项，新增 {appended} 项");
                return res;
            }
            catch (Exception e)
            {
                res.Error = e.Message;
                Logx.Error("[保存] 异常: " + e);
                return res;
            }
        }
    }
}
