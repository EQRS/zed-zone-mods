using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// ZED ZONE 代码模组：自定义耐久 (Custom Durability)。
    /// 只做一件事：把游戏里每一次「扣物品耐久」请求的消耗量，按物品类别乘上玩家配置的倍率。
    /// 物品耐久上限本身不改，因此老存档天然兼容。
    /// </summary>
    [BepInPlugin(Guid, "Custom Durability", Version)]
    public class Plugin : BasePlugin
    {
        public const string Guid = "zedzone.durabilitymultiplier";
        public const string NameEn = "Custom Durability";
        public const string Version = "1.0.4";

        public static Plugin Instance { get; private set; }

        public override void Load()
        {
            Instance = this;

            DurabilityConfig.Init(Config);

            var harmony = new Harmony(Guid);
            int patched = DurabilityPatches.Apply(harmony);

            MagazineCost.Apply();

            Log.LogInfo($"[自定义耐久] {NameEn} {Version} loaded, patched {patched} method(s).");
            if (patched == 0)
                Log.LogWarning("[自定义耐久] 没有任何补丁挂上，请检查游戏版本 / BepInEx interop 是否与当前游戏一致。");
        }
    }
}