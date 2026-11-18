using System;
using System.Reflection;
using HarmonyLib;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// 食物变质速度。
    ///
    /// 反编译确认（Assembly-CSharp 行 581648，宿主类为 ItemData）：
    ///   public static bool TryGetFoodFreshRatio(ItemData itemData, ItemAttr attr, out float freshRatio)
    ///   CallerCount=3，是计算食物新鲜度的唯一入口。
    ///
    /// 语义与其它档位完全一致（配置值即消耗系数）：
    ///   0    = 永不腐烂
    ///   0.5  = 变质速度减半（新鲜度衰减速度 x0.5）
    ///   1    = 原版
    ///   2    = 变质速度翻倍
    ///
    /// 实现：freshRatio 从 1.0（最新鲜）向 0（腐烂）衰减，
    /// 把「已衰减比例 (1 - freshRatio)」乘以系数即可。
    /// </summary>
    public static class FoodShelfCost
    {
        [HarmonyPatch]
        public static class FreshRatioPatch
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(ItemData), "TryGetFoodFreshRatio",
                new[] { typeof(ItemData), typeof(ItemAttr), typeof(float).MakeByRefType() });

            [HarmonyPostfix]
            public static void Postfix(ref bool __result, ref float freshRatio)
            {
                try
                {
                    if (!__result) return;   // 不适用（非易腐物品）

                    float fac = DurabilityConfig.CostFactor((int)ItemType.Food);
                    if (fac < 0f) return;

                    float original = freshRatio;

                    if (fac <= 0f)
                    {
                        // 完全不腐烂
                        freshRatio = 1f;
                    }
                    else if (Math.Abs(fac - 1f) >= 0.0001f)
                    {
                        // 衰减比例 = 1 - freshRatio，按系数缩放
                        float decayed = 1f - freshRatio;
                        if (decayed < 0f) decayed = 0f;
                        freshRatio = 1f - decayed * fac;
                        if (freshRatio < 0f) freshRatio = 0f;
                        if (freshRatio > 1f) freshRatio = 1f;
                    }
                    else return;

                    if (!_logged)
                    {
                        _logged = true;
                        DurabilityConfig.Log($"[食物] 变质系数 {fac:F2}：新鲜度 {original:F3} -> {freshRatio:F3}");
                    }
                }
                catch (Exception e)
                {
                    if (!_logged) { _logged = true; DurabilityConfig.Log("[食物] 补丁异常：" + e.Message); }
                }
            }
        }

        private static bool _logged;

        /// <summary>兼容旧调用点（现在改为补丁方式，无需轮询）。</summary>
        public static void Apply() { }
    }
}
