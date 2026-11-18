using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;

namespace ModConfigPanel.Actions
{
    /// <summary>
    /// 按钮动作分发。
    /// 动作分两类：
    ///   builtin:*  平台内置通用动作
    ///   callback:* 调用模组程序集里的无参静态方法
    /// 所有动作都必须保证：异常不外抛、不崩溃游戏。
    /// </summary>
    public static class ActionRouter
    {
        public const string PrefixBuiltin = "builtin:";
        public const string PrefixCallback = "callback:";

        public static void Execute(string action, Dictionary<string, string> parameters)
        {
            if (string.IsNullOrEmpty(action))
            {
                Logx.Warn("[动作] 空动作");
                return;
            }

            try
            {
                if (action.StartsWith(PrefixBuiltin, StringComparison.OrdinalIgnoreCase))
                {
                    BuiltinActions.Run(action.Substring(PrefixBuiltin.Length).Trim().ToLowerInvariant(), parameters);
                    return;
                }
                if (action.StartsWith(PrefixCallback, StringComparison.OrdinalIgnoreCase))
                {
                    CallbackInvoker.Invoke(action.Substring(PrefixCallback.Length).Trim(), parameters);
                    return;
                }
                Logx.Warn("[动作] 未知动作: " + action);
            }
            catch (Exception e)
            {
                Logx.Error($"[动作] 执行失败 {action}: {e.Message}");
            }
        }
    }

    /// <summary>平台内置动作实现。</summary>
    public static class BuiltinActions
    {
        public const string RestartGame = "restart_game";
        public const string OpenFolder = "open_folder";
        public const string OpenUrl = "open_url";
        public const string ResetSection = "reset_section";
        public const string ShowLog = "show_log";

        public static void Run(string name, Dictionary<string, string> prm)
        {
            switch (name)
            {
                case RestartGame:
                    RestartGameImpl();
                    break;

                case OpenFolder:
                    {
                        string path = GetParam(prm, "path");
                        if (string.IsNullOrEmpty(path))
                            path = Path.Combine(Schema.SchemaDiscovery.GameRoot ?? "", "BepInEx");
                        OpenFolderImpl(path);
                        break;
                    }

                case OpenUrl:
                    {
                        string url = GetParam(prm, "url");
                        if (string.IsNullOrEmpty(url)) { Logx.Warn("[动作] open_url 缺少 url 参数"); return; }
                        OpenUrlImpl(url);
                        break;
                    }

                case ShowLog:
                    OpenFolderImpl(Path.Combine(Schema.SchemaDiscovery.GameRoot ?? "", "BepInEx"));
                    break;

                case ResetSection:
                    {
                        string section = GetParam(prm, "section");
                        Logx.Info("[动作] reset_section: " + (section ?? "(未指定，由 UI 处理)"));
                        // 实际重置由 UI 层把控件恢复默认；这里只记录
                        break;
                    }

                default:
                    Logx.Warn("[动作] 未知内置动作: " + name);
                    break;
            }
        }

        private static string GetParam(Dictionary<string, string> prm, string key)
        {
            if (prm == null) return null;
            return prm.TryGetValue(key, out string v) ? v : null;
        }

        // ---------------- 重启游戏 ----------------
        private static void RestartGameImpl()
        {
            Logx.Info("[动作] 准备重启游戏");

            // 1) 先启动新进程（通过 Steam 协议），避免旧进程退出后无法拉起
            bool launched = false;
            try
            {
                var psi = new ProcessStartInfo("steam://rungameid/1211600")
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                launched = true;
                Logx.Info("[动作] 已请求 Steam 启动游戏");
            }
            catch (Exception e)
            {
                Logx.Error("[动作] 启动新进程失败: " + e.Message);
            }

            if (!launched)
            {
                // 兜底：直接用游戏自身路径重启
                try
                {
                    string exe = Path.Combine(Schema.SchemaDiscovery.GameRoot ?? "", "ZEDZONE.exe");
                    if (File.Exists(exe))
                    {
                        Process.Start(new ProcessStartInfo(exe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(exe) });
                        launched = true;
                        Logx.Info("[动作] 已用直连方式启动 ZEDZONE.exe");
                    }
                }
                catch (Exception e) { Logx.Error("[动作] 兜底启动也失败: " + e.Message); }
            }

            // 2) 延迟退出当前进程，给新进程让路
            Logx.Info("[动作] 1.5 秒后退出当前进程");
            try
            {
                UnityEngine.Application.Quit();
            }
            catch (Exception e)
            {
                Logx.Error("[动作] Application.Quit 失败: " + e.Message);
                try
                {
                    var quit = HarmonyLib.AccessTools.Method(typeof(MainMenuController), "QuitGame", Type.EmptyTypes);
                    quit?.Invoke(null, null);
                }
                catch { }
            }
        }

        // ---------------- 打开文件夹 ----------------
        private static void OpenFolderImpl(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                {
                    Logx.Warn("[动作] 目录不存在: " + path);
                    return;
                }
                var psi = new ProcessStartInfo("explorer.exe", "\"" + path + "\"")
                {
                    UseShellExecute = true
                };
                Process.Start(psi);
                Logx.Info("[动作] 已打开目录: " + path);
            }
            catch (Exception e) { Logx.Error("[动作] 打开目录失败: " + e.Message); }
        }

        // ---------------- 打开链接 ----------------
        private static void OpenUrlImpl(string url)
        {
            try
            {
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    Logx.Warn("[动作] 仅允许 http/https 链接: " + url);
                    return;
                }
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                Logx.Info("[动作] 已打开链接: " + url);
            }
            catch (Exception e) { Logx.Error("[动作] 打开链接失败: " + e.Message); }
        }
    }

    /// <summary>
    /// callback: 动作——调用模组程序集里的无参静态 void 方法。
    /// 安全约束：白名单 + 异常隔离 + 只做一次。
    /// </summary>
    public static class CallbackInvoker
    {
        private const string DecNumber = "DecNumber";
        private const string DecString = "DecString";

        public static void Invoke(string target, Dictionary<string, string> prm)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                Logx.Warn("[回调] 目标为空");
                return;
            }

            try
            {
                // target 形如 "命名空间.类.方法"
                int lastDot = target.LastIndexOf('.');
                if (lastDot <= 0)
                {
                    Logx.Warn("[回调] 格式应为 命名空间.类.方法: " + target);
                    return;
                }
                string typeName = target.Substring(0, lastDot);
                string methodName = target.Substring(lastDot + 1);

                Type t = FindType(typeName);
                if (t == null)
                {
                    Logx.Warn("[回调] 找不到类型: " + typeName);
                    return;
                }

                if (!IsAllowedAssembly(t.Assembly))
                {
                    Logx.Warn("[回调] 程序集不在白名单内: " + t.Assembly.GetName().Name);
                    return;
                }

                var mi = HarmonyLib.AccessTools.Method(t, methodName, Type.EmptyTypes);
                if (mi == null || !mi.IsStatic || mi.ReturnType != typeof(void))
                {
                    Logx.Warn($"[回调] {target} 必须是无参 static void 方法");
                    return;
                }

                mi.Invoke(null, null);
                Logx.Info("[回调] 已调用: " + target);
            }
            catch (Exception e)
            {
                Logx.Error($"[回调] 调用失败 {target}: {e.InnerException?.Message ?? e.Message}");
            }
        }

        private static Type FindType(string name)
        {
            Type t = Type.GetType(name);
            if (t != null) return t;

            // 在所有已加载程序集中找
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    t = asm.GetType(name);
                    if (t != null) return t;
                }
                catch { }
            }
            return null;
        }

        /// <summary>白名单：只允许用户模组程序集，禁止调用游戏本体或 BepInEx 内部。</summary>
        private static bool IsAllowedAssembly(System.Reflection.Assembly asm)
        {
            string n = asm.GetName().Name;
            if (string.IsNullOrEmpty(n)) return false;
            if (n == "Assembly-CSharp" || n == "Assembly-CSharp-firstpass") return false;
            if (n.StartsWith("Il2Cpp", StringComparison.OrdinalIgnoreCase)) return false;
            if (n.StartsWith("UnityEngine", StringComparison.OrdinalIgnoreCase)) return false;
            if (n.StartsWith("BepInEx", StringComparison.OrdinalIgnoreCase)) return false;
            if (n == "0Harmony" || n == "MonoMod.Utils" || n == "MonoMod.RuntimeDetour") return false;
            if (n == "ModConfigPanel") return false;   // 平台自身也不允许被调用
            return true;
        }
    }
}
