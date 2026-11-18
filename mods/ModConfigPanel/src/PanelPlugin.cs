using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using ModConfigPanel.Schema;

namespace ModConfigPanel
{
    [BepInPlugin(Guid, "ModConfig Panel", Version)]
    public class PanelPlugin : BasePlugin
    {
        public const string Guid = "zedzone.modconfigpanel";
        public const string Version = "1.0.2";

        public static PanelPlugin Instance { get; private set; }

        /// <summary>手动指定游戏根目录（定位失败时的兜底）。</summary>
        public static BepInEx.Configuration.ConfigEntry<string> ConfigGameRootOverride;
        /// <summary>定位诊断信息，供面板展示。</summary>
        public static string LocateTrace = "";
        public static List<ModSchema> Schemas = new List<ModSchema>();

        public override void Load()
        {
            Instance = this;

            try
            {
                ConfigGameRootOverride = Config.Bind("General", "GameRootOverride", "",
                    "手动指定游戏根目录（留空则自动检测）。自动检测失败时填写完整路径，例如 <Steam库>\\steamapps\\common\\ZED ZONE");
            }
            catch { }

            // 先用 DLL 位置估一个 BepInEx 目录给日志用（保证定位过程可记录）
            string asmPath = typeof(PanelPlugin).Assembly.Location;
            string preRoot = GuessRootForLog(asmPath);
            Logx.Init("ModConfigPanel", preRoot);

            Logx.Info("===== ModConfig Panel 载入 =====");

            // 再用完整策略定位真实根目录
            string gameRoot = FindGameRoot();

            Logx.Info("游戏根目录: " + gameRoot);

            // 根目录有效性校验：找不到会明显提示，而不是静默显示 0 个模组
            bool rootOk = !string.IsNullOrEmpty(gameRoot)
                          && System.IO.File.Exists(System.IO.Path.Combine(gameRoot, "ZEDZONE.exe"));
            if (!rootOk)
            {
                Logx.Error("========== 游戏根目录定位失败 ==========");
                Logx.Error("识别到的路径: " + gameRoot);
                Logx.Error("该路径下没有 ZEDZONE.exe，配置面板将无法工作。");
                Logx.Error("请把日志 D:\\...\\BepInEx\\ModConfigPanel.log 反馈给作者。");
                Logx.Error("========================================");
            }
            else
            {
                Logx.Info("根目录校验通过（ZEDZONE.exe 存在）");
            }

            SchemaDiscovery.Init(gameRoot);

            // M9：文档自释放
            UI.DocRelease.Ensure(gameRoot);
            Logx.Info("配置目录: " + SchemaDiscovery.ConfigDir);

            // M1：扫描并打印结果
            try
            {
                Schemas = SchemaDiscovery.ScanAll();
                Logx.Info($"扫描完成：共 {Schemas.Count} 个模组");
                foreach (var s in Schemas)
                {
                    Logx.Info($"  [{s.Source}] {s.DisplayName} ({s.ModGuid}) " +
                             $"groups={s.Groups.Count} items={s.TotalItems} " +
                             $"file={(string.IsNullOrEmpty(s.ConfigAbsPath) ? "N/A" : Path.GetFileName(s.ConfigAbsPath))}");
                }
            }
            catch (Exception e)
            {
                Logx.Error("扫描失败: " + e);
            }

            // 主菜单入口（M2 才做面板，这里先只注入按钮）
            try
            {
                var harmony = new Harmony(Guid);
                harmony.PatchAll(typeof(inject.MainMenuInjector.Patch));
                Logx.Info("主菜单注入补丁已挂载（改键使用选键器）");

            }
            catch (Exception e) { Logx.Error("注入补丁失败: " + e); }

            Logx.Info("===== ModConfig Panel 载入完成 =====");
        }

        /// <summary>日志初始化专用的粗略根目录估算（只为了能尽快写日志）。</summary>
        private static string GuessRootForLog(string asmPath)
        {
            try
            {
                string d = Path.GetDirectoryName(asmPath);
                for (int i = 0; i < 8 && !string.IsNullOrEmpty(d); i++)
                {
                    if (Directory.Exists(Path.Combine(d, "BepInEx"))) return d;
                    string name = Path.GetFileName(d);
                    if (string.Equals(name, "steamapps", StringComparison.OrdinalIgnoreCase))
                    {
                        string cand = Path.Combine(d, "common", "ZED ZONE");
                        if (Directory.Exists(Path.Combine(cand, "BepInEx"))) return cand;
                    }
                    d = Path.GetDirectoryName(d);
                }
            }
            catch { }
            // 兜底：从当前进程目录找 BepInEx
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                if (!string.IsNullOrEmpty(baseDir) && Directory.Exists(Path.Combine(baseDir, "BepInEx")))
                    return baseDir;
            }
            catch { }
            return null;
        }

        private static string FindGameRoot()
        {
            string asm = typeof(PanelPlugin).Assembly.Location;
            string manual = null;
            try { manual = ConfigGameRootOverride?.Value; } catch { }

            int attempts = 0;
            string result = GameLocator.Find(asm, msg =>
            {
                attempts++;
                Logx.Info(msg);
                LocateTrace += msg + "\n";
            }, manual);

            LocateTrace += "最终: " + result + "\n";
            return result;
        }
    }
}
