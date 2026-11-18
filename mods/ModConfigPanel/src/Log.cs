using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BepInEx.Logging;

namespace ModConfigPanel
{
    /// <summary>
    /// 日志封装。
    /// 除 BepInEx 标准日志外，额外写入 ModConfigPanel.log（追加模式），
    /// 避免 BepInEx 的 AppendLog=false 导致重启后丢失证据。
    /// </summary>
    internal static class Logx
    {
        private static ManualLogSource _src;
        private static string _ownLogPath;
        private static readonly object _lock = new object();
        private static bool _ownLogFailed;

        public static void Init(string name, string gameRoot)
        {
            _src = Logger.CreateLogSource(name);

            try
            {
                if (!string.IsNullOrEmpty(gameRoot))
                {
                    string dir = Path.Combine(gameRoot, "BepInEx");
                    if (Directory.Exists(dir))
                    {
                        _ownLogPath = Path.Combine(dir, "ModConfigPanel.log");
                        WriteOwn("===== ModConfigPanel 启动 " +
                                 DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " =====");
                    }
                }
            }
            catch { _ownLogFailed = true; }
        }

        private static void WriteOwn(string msg)
        {
            if (_ownLogPath == null || _ownLogFailed) return;
            try
            {
                lock (_lock)
                {
                    // 防止日志无限增长：超过 2MB 就滚动
                    try
                    {
                        var fi = new FileInfo(_ownLogPath);
                        if (fi.Exists && fi.Length > 2 * 1024 * 1024)
                        {
                            string old = _ownLogPath + ".1";
                            if (File.Exists(old)) File.Delete(old);
                            File.Move(_ownLogPath, old);
                        }
                    }
                    catch { }

                    File.AppendAllText(_ownLogPath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + "\r\n",
                        new UTF8Encoding(false));
                }
            }
            catch { _ownLogFailed = true; }
        }

        public static void Info(string m) { _src?.LogInfo(m); WriteOwn("[INFO] " + m); }
        public static void Warn(string m) { _src?.LogWarning(m); WriteOwn("[WARN] " + m); }
        public static void Error(string m) { _src?.LogError(m); WriteOwn("[ERR ] " + m); }
    }
}
