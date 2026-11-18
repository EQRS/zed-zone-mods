using System;
using System.Collections.Generic;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// 物品类别（ItemType）查询。
    /// 游戏里 ItemManager 是普通托管对象，不是 MonoBehaviour，BepInEx 的 IL2CPP 探针看不到它；
    /// 所以这里直接走 Il2CppInterop 的托管镜像：静态字段 instance → 实例方法 GetItemAttrById。
    /// 拿不到实例或查不到物品时返回 -1，调用方按「倍率 1.0」处理，绝不影响原版行为。
    /// </summary>
    public static class ItemLookup
    {
        private static readonly Dictionary<int, int> TypeCache = new Dictionary<int, int>();
        private static bool _warned;

        public static int GetItemType(int itemId)
        {
            if (itemId <= 0) return -1;
            if (TypeCache.TryGetValue(itemId, out int cached)) return cached;

            int t = Query(itemId);
            if (t >= 0) TypeCache[itemId] = t;
            return t;
        }

        /// <summary>取物品显示名（用于诊断）。</summary>
        public static string GetItemName(int itemId)
        {
            try
            {
                ItemManager mgr = ItemManager.instance;
                if (mgr == null) return "";
                ItemAttr attr = mgr.GetItemAttrById(itemId);
                if (attr == null) return "";
                return attr.itemName ?? "";
            }
            catch { return ""; }
        }

        public static void ClearCache()
        {
            TypeCache.Clear();
        }

        /// <summary>诊断用：打印一次物品表的可用性与条目数。</summary>
        public static void DumpDiagnostics()
        {
            try
            {
                ItemManager mgr = ItemManager.instance;
                if (mgr == null)
                {
                    DurabilityConfig.Log("诊断：ItemManager.instance = null");
                    return;
                }
                var dic = mgr.itemAttrDic;
                int count = dic == null ? -1 : dic.Count;
                DurabilityConfig.Log("诊断：ItemManager 可用，物品表条目 " + count + " 个");
            }
            catch (Exception e)
            {
                DurabilityConfig.Log("诊断失败：" + e.Message);
            }
        }

        private static int Query(int itemId)
        {
            try
            {
                ItemManager mgr = ItemManager.instance;
                if (mgr == null)
                {
                    if (!_warned)
                    {
                        _warned = true;
                        DurabilityConfig.Log("ItemManager.instance 为空，暂按原版倍率处理（进游戏后会自动恢复）。");
                    }
                    return -1;
                }

                ItemAttr attr = mgr.GetItemAttrById(itemId);
                if (attr == null) return -1;
                int typeVal = (int)attr.itemType;
                // 诊断：验证类型判断是否正确
                if (itemId >= 10000 && itemId < 11000)
                {
                    DurabilityConfig.Log($"[类型诊断] itemId={itemId} name='{attr.itemName}' " +
                                         $"itemType={(int)attr.itemType}({attr.itemType}) " +
                                         $"实际类型={attr.GetType().Name}");
                }
                return typeVal;
            }
            catch (Exception e)
            {
                if (!_warned)
                {
                    _warned = true;
                    DurabilityConfig.Log("查询物品类别异常（已回退原版倍率）：" + e.Message);
                }
                return -1;
            }
        }
    }
}