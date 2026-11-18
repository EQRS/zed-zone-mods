using System;
using System.IO;
using Microsoft.Win32;

namespace ModConfigPanel
{
    /// <summary>
    /// 游戏根目录定位。
    /// 关键：工坊插件的 DLL 位于 steamapps\workshop\content\1211600\<ID>\，
    /// 从那里向上永远找不到 ZEDZONE.exe，必须用 Steam 目录结构反推。
    /// 按可靠性从高到低尝试多种策略。
    /// </summary>
    public static class GameLocator
    {
        public static string Find(string assemblyPath, Action<string> log, string manualOverride = null)
        {
            try
            {
                // ---- 策略 0：玩家手动指定（最高优先级）----
                if (!string.IsNullOrWhiteSpace(manualOverride))
                {
                    string ov = manualOverride.Trim().Trim('"');
                    if (IsGameRoot(ov))
                    {
                        log?.Invoke("[定位] 使用手动指定的根目录: " + ov);
                        return ov;
                    }
                    log?.Invoke("[定位] 手动指定的路径无效（缺 ZEDZONE.exe）: " + ov + "，继续自动检测");
                }

                // ---- 策略 1：从 DLL 位置向上找 ZEDZONE.exe（本地 Mods 安装）----
                string p = TryWalkUpForExe(assemblyPath);
                if (p != null) { log?.Invoke("[定位] 策略1 向上找到 ZEDZONE.exe: " + p); return p; }

                // ---- 策略 2：从 DLL 位置识别 workshop\content\<appid> 结构，反推到游戏目录 ----
                p = TryFromWorkshopPath(assemblyPath);
                if (p != null) { log?.Invoke("[定位] 策略2 从工坊路径反推: " + p); return p; }

                // ---- 策略 3：BepInEx 已加载，取其所在目录的父级 ----
                p = TryFromBepInExDir();
                if (p != null) { log?.Invoke("[定位] 策略3 从 BepInEx 目录: " + p); return p; }

                // ---- 策略 4：读注册表 Steam 库 + appmanifest ----
                p = TryFromSteamLibraries();
                if (p != null) { log?.Invoke("[定位] 策略4 从 Steam 库: " + p); return p; }

                // ---- 策略 5：跑着的进程路径 ----
                p = TryFromProcess();
                if (p != null) { log?.Invoke("[定位] 策略5 从进程: " + p); return p; }

                // ---- 策略 6：当前工作目录 ----
                p = TryFromCurrentDir();
                if (p != null) { log?.Invoke("[定位] 策略6 从工作目录: " + p); return p; }

                log?.Invoke("[定位] 全部失败，回退到 DLL 目录");
                return Path.GetDirectoryName(assemblyPath);
            }
            catch (Exception e)
            {
                log?.Invoke("[定位] 异常: " + e.Message);
                return Path.GetDirectoryName(assemblyPath);
            }
        }

        private static bool IsGameRoot(string d)
        {
            if (string.IsNullOrEmpty(d) || !Directory.Exists(d)) return false;
            return File.Exists(Path.Combine(d, "ZEDZONE.exe"))
                && Directory.Exists(Path.Combine(d, "ZEDZONE_Data"));
        }

        private static string TryWalkUpForExe(string asmPath)
        {
            string d = Path.GetDirectoryName(asmPath);
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(d); i++)
            {
                if (IsGameRoot(d)) return d;
                d = Path.GetDirectoryName(d);
            }
            return null;
        }

        /// <summary>
        /// 从 ...\steamapps\workshop\content\1211600\&lt;ID&gt;\xxx.dll 反推：
        /// 找到 steamapps 那一级，再拼 common\ZED ZONE。
        /// </summary>
        private static string TryFromWorkshopPath(string asmPath)
        {
            try
            {
                string d = Path.GetDirectoryName(asmPath);
                string steamApps = null;

                // 向上找名为 steamapps 的目录
                for (int i = 0; i < 10 && !string.IsNullOrEmpty(d); i++)
                {
                    if (string.Equals(Path.GetFileName(d), "steamapps", StringComparison.OrdinalIgnoreCase))
                    { steamApps = d; break; }
                    d = Path.GetDirectoryName(d);
                }
                if (steamApps == null) return null;

                // 从 appmanifest 读 installdir（最准）
                string acf = Path.Combine(steamApps, "appmanifest_1211600.acf");
                if (File.Exists(acf))
                {
                    string txt = File.ReadAllText(acf);
                    var m = System.Text.RegularExpressions.Regex.Match(txt, "\"installdir\"\\s+\"([^\"]+)\"");
                    if (m.Success)
                    {
                        string cand = Path.Combine(steamApps, "common", m.Groups[1].Value);
                        if (IsGameRoot(cand)) return cand;
                    }
                }

                // 没有 manifest 就按默认目录名试
                foreach (string name in new[] { "ZED ZONE", "ZEDZONE" })
                {
                    string cand = Path.Combine(steamApps, "common", name);
                    if (IsGameRoot(cand)) return cand;
                }
            }
            catch { }
            return null;
        }

        /// <summary>BepInEx 一定在游戏根目录下，用它定位。</summary>
        private static string TryFromBepInExDir()
        {
            try
            {
                // BepInEx.Paths.BepInExRootPath 指向 <游戏>\BepInEx\
                // 遍历已加载程序集找（比 Type.GetType 限定名更稳，不依赖程序集是否已加载）
                Type bepPaths = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        bepPaths = asm.GetType("BepInEx.Paths");
                        if (bepPaths != null) break;
                    }
                    catch { }
                }
                if (bepPaths != null)
                {
                    foreach (string propName in new[] { "BepInExRootPath", "GameRootPath" })
                    {
                        var prop = bepPaths.GetProperty(propName,
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                        if (prop == null) continue;
                        string val = prop.GetValue(null) as string;
                        if (string.IsNullOrEmpty(val)) continue;

                        if (propName == "GameRootPath")
                        {
                            if (IsGameRoot(val)) return val;
                        }
                        else
                        {
                            string parent = Path.GetDirectoryName(val.TrimEnd('\\', '/'));
                            if (IsGameRoot(parent)) return parent;
                        }
                    }
                }
            }
            catch { }

            // 兜底：从 BepInEx 插件目录向上找
            try
            {
                string bep = Path.Combine(AppDomain.CurrentDomain.BaseDirectory ?? "", "BepInEx");
                if (Directory.Exists(bep))
                {
                    string parent = Path.GetDirectoryName(bep.TrimEnd('\\', '/'));
                    if (IsGameRoot(parent)) return parent;
                }
            }
            catch { }
            return null;
        }

        /// <summary>读注册表找 Steam 库，再逐个找 appmanifest。</summary>
        private static string TryFromSteamLibraries()
        {
            try
            {
                string steamPath = null;
                try
                {
                    using (var k = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam"))
                        steamPath = k?.GetValue("SteamPath") as string;
                }
                catch { }
                if (string.IsNullOrEmpty(steamPath))
                {
                    try
                    {
                        using (var k = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\Valve\\Steam"))
                            steamPath = k?.GetValue("InstallPath") as string;
                    }
                    catch { }
                }
                if (string.IsNullOrEmpty(steamPath)) return null;

                string norm = steamPath.Replace('/', '\\');
                var libs = new System.Collections.Generic.List<string> { norm };

                // 读 libraryfolders.vdf 里的其它库
                string vdf = Path.Combine(norm, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdf))
                {
                    string txt = File.ReadAllText(vdf);
                    foreach (System.Text.RegularExpressions.Match m in
                        System.Text.RegularExpressions.Regex.Matches(txt, "\"path\"\\s+\"([^\"]+)\""))
                    {
                        string p2 = m.Groups[1].Value.Replace("\\\\", "\\").Replace('/', '\\');
                        if (!libs.Contains(p2) && Directory.Exists(p2)) libs.Add(p2);
                    }
                }

                foreach (string lib in libs)
                {
                    string sa = Path.Combine(lib, "steamapps");
                    string acf = Path.Combine(sa, "appmanifest_1211600.acf");
                    if (File.Exists(acf))
                    {
                        string txt = File.ReadAllText(acf);
                        var m = System.Text.RegularExpressions.Regex.Match(txt, "\"installdir\"\\s+\"([^\"]+)\"");
                        if (m.Success)
                        {
                            string cand = Path.Combine(sa, "common", m.Groups[1].Value);
                            if (IsGameRoot(cand)) return cand;
                        }
                    }
                    foreach (string name in new[] { "ZED ZONE", "ZEDZONE" })
                    {
                        string cand = Path.Combine(sa, "common", name);
                        if (IsGameRoot(cand)) return cand;
                    }
                }
            }
            catch { }
            return null;
        }

        private static string TryFromProcess()
        {
            try
            {
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                string exe = proc.MainModule?.FileName;
                if (!string.IsNullOrEmpty(exe))
                {
                    string d = Path.GetDirectoryName(exe);
                    if (IsGameRoot(d)) return d;
                }
            }
            catch { }
            return null;
        }

        private static string TryFromCurrentDir()
        {
            try
            {
                string d = Directory.GetCurrentDirectory();
                if (IsGameRoot(d)) return d;
            }
            catch { }
            return null;
        }
    }
}
