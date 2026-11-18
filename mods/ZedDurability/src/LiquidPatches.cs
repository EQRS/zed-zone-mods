using System;
using System.Reflection;
using HarmonyLib;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// 液体消耗倍率（独立档 LiquidUse_液体消耗）。
    /// 游戏里喝水/灌装的天花板入口是 HumanCharacterController.DrinkLiquidFromContainer(containerData, drinkVolume)
    /// 与 DrinkLiquid(liquidData, drinkVolume)，参数就是本次要消耗的液体体积。
    /// 这里只缩小这个体积，因此：
    ///   - 容器里液体的存量消耗变慢（水更耐用）
    ///   - 容器本体耐久（durability）完全不受影响，仍由 Container_容器 档控制
    /// </summary>
    public static class LiquidPatches
    {
        internal static float Factor()
        {
            try { return DurabilityConfig.CostFactor(DurabilityConfig.IdxLiquidUse); }
            catch { return 1f; }
        }

        internal static bool Active(float f) => Math.Abs(f - 1f) >= 0.0001f;

        /// <summary>从容器取液体喝（矿泉水等）。</summary>
        [HarmonyPatch]
        public static class DrinkFromContainer
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(HumanCharacterController), "DrinkLiquidFromContainer",
                new[] { typeof(ItemData), typeof(float) });

            [HarmonyPrefix]
            public static void Prefix(ref float drinkVolume)
            {
                try
                {
                    float fac = Factor();
                    if (fac <= 0f) { drinkVolume = 0f; return; }
                    if (!Active(fac)) return;
                    drinkVolume = drinkVolume * fac;
                }
                catch { }
            }
        }

        /// <summary>直接饮用液体数据（部分流程走这里）。</summary>
        [HarmonyPatch]
        public static class DrinkLiquidDirect
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(HumanCharacterController), "DrinkLiquid",
                new[] { typeof(ItemData), typeof(float) });

            [HarmonyPrefix]
            public static void Prefix(ref float drinkVolume)
            {
                try
                {
                    float fac = Factor();
                    if (fac <= 0f) { drinkVolume = 0f; return; }
                    if (!Active(fac)) return;
                    drinkVolume = drinkVolume * fac;
                }
                catch { }
            }
        }
    }
}