using System;
using System.Collections.Generic;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// 弹匣耐久消耗。
    /// ItemAttr_Magazine.durabilityCostPerShot 在 IL2CPP 里是纯字段访问器，
    /// Harmony 挂不上（日志报 "is a field accessor, it can't be patched"），
    /// 所以改为直接按倍率缩放物品属性表里的字段值。
    /// 用字典记住每个弹匣的原始值，保证反复热重载不会越缩越小。
    /// </summary>
    public static class MagazineCost
    {
        private static readonly Dictionary<int, float> Original = new Dictionary<int, float>();
        private static bool _dumped;
        private static bool _logged;

        public static void Apply()
        {
            try
            {
                float fac = DurabilityConfig.CostFactor((int)ItemType.Magazine);
                if (Math.Abs(fac - 1f) < 0.0001f) { if (!_logged) { _logged = true; DurabilityConfig.Log("[弹匣] 倍率=1，跳过"); } return; }

                ItemManager mgr = ItemManager.instance;
                if (mgr == null)
                {
                    if (!_dumped) { _dumped = true; DurabilityConfig.Log("[弹匣] 物品表未就绪（ItemManager.instance == null）"); }
                    return;
                }
                Il2CppSystem.Collections.Generic.Dictionary<int, ItemAttr> dic = mgr.itemAttrDic;
                if (dic == null) { if (!_logged) { _logged = true; DurabilityConfig.Log("[弹匣] itemAttrDic == null"); } return; }
                if (!_logged) { _logged = true; DurabilityConfig.Log("[弹匣] 物品表就绪，条目数=" + dic.Count); }

                int n = 0;
                int typeCount = 0;
                foreach (var kv in dic)
                {
                    var baseAttr = kv.Value;
                    if (baseAttr == null) continue;

                    // Interop 层拿不到子类型，必须按 itemType 判断后再 TryCast
                    if ((int)baseAttr.itemType != (int)ItemType.Magazine)
                    {
                        if (typeCount < 3 && (int)baseAttr.itemType == 9)
                        {
                            typeCount++;
                            DurabilityConfig.Log($"[弹匣诊断] id={kv.Key} itemType={baseAttr.itemType}");
                        }
                        continue;
                    }

                    ItemAttr_Magazine mag = baseAttr.TryCast<ItemAttr_Magazine>();
                    if (mag == null) { DurabilityConfig.Log($"[弹匣] id={kv.Key} TryCast 失败"); continue; }

                    float cur = mag.durabilityCostPerShot;
                    if (!Original.ContainsKey(kv.Key)) Original[kv.Key] = cur;

                    float baseVal = Original[kv.Key];
                    if (baseVal <= 0f) continue;
                    mag.durabilityCostPerShot = baseVal * fac;
                    n++;
                }
                if (n > 0)
                {
                    DurabilityConfig.Log("弹匣耐久消耗按系数 " + fac + " 应用到 " + n + " 种弹匣");
                    // 读回验证：抽样打印几个弹匣的实际值
                    int shown = 0;
                    foreach (var kv in dic)
                    {
                        ItemAttr_Magazine m2 = kv.Value as ItemAttr_Magazine;
                        if (m2 == null) continue;
                        DurabilityConfig.Log($"  [弹匣验证] id={kv.Key} durabilityCostPerShot={m2.durabilityCostPerShot:F4} (原={Original.GetValueOrDefault(kv.Key):F4})");
                        if (++shown >= 5) break;
                    }
                }
            }
            catch (Exception e)
            {
                DurabilityConfig.Log("弹匣倍率应用失败（已跳过）：" + e.Message);
            }
        }
    }
}