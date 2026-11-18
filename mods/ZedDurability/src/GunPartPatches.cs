using System;
using System.Reflection;
using HarmonyLib;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// 枪械相关的真实耐久通路。
    /// 游戏事实（反编译核实）：ItemAttr_RangedWeapon 没有耐久字段；
    /// 会磨损的是挂在枪上的 GunPart（枪管等），其耐久上限在 ItemAttr_GunPart.firingDurability，
    /// 开火时 BasicRangedWeapon 用 m_gunpartDurabilityCostFloat 累积磨损。
    /// 因此这里按「枪械零件 GunPart」这一档倍率处理，不去碰枪械本体。
    /// </summary>
    public static class GunPartPatches
    {
        internal static float Factor()
        {
            try { return DurabilityConfig.CostFactor((int)ItemType.GunPart); }
            catch { return 1f; }
        }

        internal static bool Active(float f) => Math.Abs(f - 1f) >= 0.0001f;

        /// <summary>开火（含点射/连发）结算后，把本次累积的配件磨损按倍率缩小，等于配件更耐用。</summary>
        [HarmonyPatch]
        public static class AfterRangedWeaponFire
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(BasicRangedWeapon), "Fire", new[] { typeof(float) });

            // 开火前记下枪械本体耐久（properties[0]），开火后按倍率修正损耗；
            // 同时处理配件的消耗系数（Prefix 改、Postfix 还原）。
            /// <summary>Harmony 只允许一个 __state，用这个类同时携带两个值。</summary>
            public class FireState
            {
                public float GunPartCost = -1f;
                public float WeaponDura = -1f;
            }

            [HarmonyPrefix]
            public static void Prefix(BasicRangedWeapon __instance, out FireState __state)
            {
                __state = new FireState();
                try
                {
                    if (__instance == null) return;

                    // 配件消耗系数
                    float f = Factor();
                    if (Active(f))
                    {
                        float cost = __instance.m_gunpartDurabilityCostFloat;
                        if (cost > 0f)
                        {
                            __state.GunPartCost = cost;
                            __instance.m_gunpartDurabilityCostFloat = cost * f;
                        }
                    }

                    // 枪械本体耐久（properties[0]，已由日志实测确认）
                    var wd = __instance.weaponData;
                    if (wd?.properties != null && wd.properties.Length > 0)
                        __state.WeaponDura = wd.properties[0];
                }
                catch { }
            }

            [HarmonyPostfix]
            public static void Postfix(BasicRangedWeapon __instance, FireState __state)
            {
                try
                {
                    if (__instance == null || __state == null) return;

                    // 还原配件系数
                    if (__state.GunPartCost >= 0f)
                        __instance.m_gunpartDurabilityCostFloat = __state.GunPartCost;

                    // 修正枪械本体损耗
                    if (__state.WeaponDura < 0f) return;
                    int wt = (int)ItemType.RangedWeapon;
                    float wf = DurabilityConfig.CostFactor(wt);
                    if (wf < 0f || Math.Abs(wf - 1f) < 0.0001f) return;

                    var wd = __instance.weaponData;
                    if (wd?.properties == null || wd.properties.Length == 0) return;

                    float after = wd.properties[0];
                    float loss = __state.WeaponDura - after;
                    if (loss <= 0f) return;   // 没损耗（含修复）

                    float newLoss = loss * wf;
                    wd.properties[0] = __state.WeaponDura - newLoss;
                    DurabilityConfig.Log($"[枪械本体] {__state.WeaponDura:F2} 损失 {loss:F3} x{wf:F2} -> {newLoss:F3} 结果 {wd.properties[0]:F2}");
                }
                catch { }
            }
        }

        /// <summary>配件聚合（含耐久汇总）被计算时不再重复缩放，保持与游戏一致。</summary>
        [HarmonyPatch]
        public static class GunPartAggregateRead
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(ItemAttr_GunPart), "Aggregate", new[] { typeof(ItemData) });

            [HarmonyPostfix]
            public static void Postfix() { /* 预留：便于日后统计/调试，不做修改 */ }
        }
    }

    
}