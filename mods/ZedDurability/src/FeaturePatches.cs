using System;
using System.Reflection;
using HarmonyLib;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// 各类「独立耐久消耗特征」的补丁。
    /// 游戏里除了通用 CostItemDurability，还有一批自带消耗数值的特征/属性字段，
    /// 例如弹匣每次射击、容器每毫升、手电每秒、喷漆每次、鱼饵每次、近战附件每次攻击。
    /// 这里按对应类别倍率在「属性值被读取」的瞬间临时缩放，读完立刻还原——
    /// 不写回物品属性、不改存档，异常时一律回退原版数值。
    /// </summary>
    public static class FeaturePatches
    {
        internal static bool Active(float m) => m > 0f && Math.Abs(m - 1f) >= 0.0001f;

        /// <summary>把 getter 的返回值按倍率缩小；<paramref name="itemType"/> 为对应 ItemType 整数。</summary>
        internal static void ScaleGetter(float value, int itemType, ref float __result)
        {
            try
            {
                
                float fac = DurabilityConfig.CostFactor(itemType);
                if (fac <= 0f) { __result = 0f; return; }
                if (!Active(fac)) return;
                __result = value * fac;
            }
            catch { }
        }

        /// <summary>容器：每毫升耐久消耗 ItemAttr_Container.EffectiveDurabilityCostPerML。</summary>
        [HarmonyPatch]
        public static class ContainerCostPerML
        {
            static MethodBase TargetMethod() => AccessTools.PropertyGetter(
                typeof(ItemAttr_Container), "EffectiveDurabilityCostPerML");

            [HarmonyPostfix]
            public static void Postfix(ref float __result)
            {
                try
                {
                    float fac = DurabilityConfig.CostFactor((int)ItemType.Container);
                    if (fac <= 0f) __result = 0f;
                    else if (Active(fac)) __result = __result * fac;
                }
                catch { }
            }
        }

        /// <summary>手电等照明工具：每秒耐久消耗。</summary>
        [HarmonyPatch]
        public static class LightingToolCostPerSecond
        {
            static MethodBase TargetMethod() => AccessTools.PropertyGetter(
                typeof(ItemFeature_LightingTool), "durabilityCostPerSecondFloat");

            [HarmonyPostfix]
            public static void Postfix(ref float __result)
            {
                try
                {
                    // 照明工具使用独立档位，不再借用材料档
                    
                    float fac = DurabilityConfig.CostFactor(DurabilityConfig.IdxLightingTool);
                    if (fac <= 0f) __result = 0f;
                    else if (Active(fac)) __result = __result * fac;
                }
                catch { }
            }
        }

        /// <summary>喷漆：每次喷涂的耐久消耗。</summary>
        [HarmonyPatch]
        public static class SprayPaintCost
        {
            static MethodBase TargetMethod() => AccessTools.PropertyGetter(
                typeof(ItemFeature_SprayPaint), "paintDurabilityCostFloat");

            [HarmonyPostfix]
            public static void Postfix(ref float __result)
            {
                try
                {
                    float fac = DurabilityConfig.CostFactor((int)ItemType.Material);
                    if (fac <= 0f) __result = 0f;
                    else if (Active(fac)) __result = __result * fac;
                }
                catch { }
            }
        }

        /// <summary>鱼饵：每次使用的耐久消耗。</summary>
        [HarmonyPatch]
        public static class FishBaitCost
        {
            static MethodBase TargetMethod() => AccessTools.PropertyGetter(
                typeof(ItemFeature_FishBait), "durabilityCostFloat");

            [HarmonyPostfix]
            public static void Postfix(ref float __result)
            {
                try
                {
                    // 鱼饵使用独立档位，不再借用食物档
                    
                    float fac = DurabilityConfig.CostFactor(DurabilityConfig.IdxFishBait);
                    if (fac <= 0f) __result = 0f;
                    else if (Active(fac)) __result = __result * fac;
                }
                catch { }
            }
        }

        /// <summary>近战武器附件（刃口等）：每次攻击的耐久消耗。</summary>
        [HarmonyPatch]
        public static class MeleeAttachmentCost
        {
            static MethodBase TargetMethod() => AccessTools.PropertyGetter(
                typeof(ItemFeature_MeleeWeaponAttachment), "durabilityCostPerAttackFloat");

            [HarmonyPostfix]
            public static void Postfix(ref float __result)
            {
                try
                {
                    float fac = DurabilityConfig.CostFactor((int)ItemType.MeleeWeapon);
                    if (fac <= 0f) __result = 0f;
                    else if (Active(fac)) __result = __result * fac;
                }
                catch { }
            }
        }
    }
}