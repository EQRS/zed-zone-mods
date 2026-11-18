using System;
using System.IO;
using System.Text;

namespace ModConfigPanel.UI
{
    /// <summary>
    /// 文档自释放：首次运行时把 docs 释放到 Mods\ModConfigPanel\docs\，
    /// 让模组作者不用上网就能找到接入指南。
    /// </summary>
    public static class DocRelease
    {
        private const string MarkerFile = ".released";

        public static void Ensure(string gameRoot)
        {
            try
            {
                if (string.IsNullOrEmpty(gameRoot)) return;
                string modDir = Path.Combine(gameRoot, "Mods", "ModConfigPanel");
                string docsDir = Path.Combine(modDir, "docs");
                string marker = Path.Combine(docsDir, MarkerFile);

                if (File.Exists(marker)) return;

                Directory.CreateDirectory(docsDir);

                int n = 0;
                n += TryWrite(docsDir, "接入指南.md", GuideText);
                n += TryWrite(docsDir, "API.md", ApiText);
                n += TryWrite(docsDir, "schema.json", SchemaText);

                File.WriteAllText(marker, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), new UTF8Encoding(false));
                Logx.Info($"[文档] 已释放 {n} 个文件到 {docsDir}");
            }
            catch (Exception e)
            {
                Logx.Warn("[文档] 释放失败（不影响功能）: " + e.Message);
            }
        }

        private static int TryWrite(string dir, string name, string content)
        {
            try
            {
                File.WriteAllText(Path.Combine(dir, name), content, new UTF8Encoding(false));
                return 1;
            }
            catch { return 0; }
        }

        // 文档内容以内嵌字符串形式随 DLL 发布（发布时由构建脚本从 package/docs 注入）
        private static readonly string GuideText = Embed("接入指南.md");
        private static readonly string ApiText = Embed("API.md");
        private static readonly string SchemaText = Embed("schema.json");

        private static string Embed(string name)
        {
            // 从 DLL 同级的 docs 目录读取（发布包内就放在这儿）
            try
            {
                string dllDir = Path.GetDirectoryName(typeof(DocRelease).Assembly.Location);
                string p = Path.Combine(dllDir, "docs", name);
                if (File.Exists(p)) return File.ReadAllText(p);
            }
            catch { }
            return "# " + name + "\n\n（文档文件未随包发布，请查看创意工坊页面）\n";
        }
    }
}
