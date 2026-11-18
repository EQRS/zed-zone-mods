using System;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// 游戏内周期任务探针（不提供热重载，只保证弹匣倍率能在物品表就绪后应用）。
    /// 改配置后请重启游戏生效。
    /// </summary>
    public static class FrameProbe
    {
        private static int _lastFrame = -1;

        internal static void Tick()
        {
            try
            {
                if (Time.frameCount == _lastFrame) return;
                _lastFrame = Time.frameCount;

                // 每 60 帧尝试应用一次（物品表可能晚于插件加载就绪）
                if (Time.frameCount % 60 == 0)
                {
                    MagazineCost.Apply();
                    FoodShelfCost.Apply();
                }
            }
            catch { }
        }

        [HarmonyPatch]
        public static class CharacterInputProbe
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(CharacterInput), "Update", Type.EmptyTypes);

            [HarmonyPostfix]
            public static void Postfix() => Tick();
        }

        /// <summary>
        /// 物品表初始化完成后立即应用一次（不依赖游戏内场景）。
        /// 反编译确认：ItemManager.Init() 是实例方法，CallerCount=3，物品表在此时就绪。
        /// </summary>
        [HarmonyPatch]
        public static class ItemManagerInitProbe
        {
            static MethodBase TargetMethod() => AccessTools.Method(typeof(ItemManager), "Init", Type.EmptyTypes);

            [HarmonyPostfix]
            public static void Postfix()
            {
                try
                {
                    DurabilityConfig.Log("[探针] ItemManager.Init() 已完成，开始应用弹匣/食物倍率");
                    MagazineCost.Apply();
                    FoodShelfCost.Apply();
                }
                catch (Exception e) { DurabilityConfig.Log("[探针] 应用失败：" + e.Message); }
            }
        }

        /// <summary>战利品表初始化后也应用一次（Init 之后才真正可用）。</summary>
        [HarmonyPatch]
        public static class LootTableProbe
        {
            static MethodBase TargetMethod() => AccessTools.Method(typeof(ItemManager), "InitializeLootTable", Type.EmptyTypes);

            [HarmonyPostfix]
            public static void Postfix()
            {
                try
                {
                    MagazineCost.Apply();
                    FoodShelfCost.Apply();
                }
                catch { }
            }
        }

        [HarmonyPatch]
        public static class MapControllerProbe
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(MapController), "Update", Type.EmptyTypes);

            [HarmonyPostfix]
            public static void Postfix() => Tick();
        }
    }
}
