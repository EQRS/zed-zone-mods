using System;
using System.Reflection;
using HarmonyLib;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// 全部 Harmony 补丁。
    /// 原则：只放大/缩小「扣耐久」这一步传入的消耗量，不改耐久上限、不碰存档字段；
    /// 任何异常都吞掉并按原版数值继续，绝不让模组把游戏搞崩。
    /// </summary>
    public static class DurabilityPatches
    {
        public static int Apply(Harmony harmony)
        {
            int n = 0;
            n += TryPatch(harmony, typeof(HumanCostItemDurability));
            n += TryPatch(harmony, typeof(HumanCostItemDurabilityExecute));
            n += TryPatch(harmony, typeof(InventoryCostItemDurabilityByIdOut));
            n += TryPatch(harmony, typeof(VehicleCostStorageItemDurability));
            n += TryPatch(harmony, typeof(MeleeWeaponDurabilityPerHit));
            n += TryPatch(harmony, typeof(GunPartPatches.AfterRangedWeaponFire));


            n += TryPatch(harmony, typeof(FeaturePatches.ContainerCostPerML));
            n += TryPatch(harmony, typeof(FeaturePatches.LightingToolCostPerSecond));
            n += TryPatch(harmony, typeof(FeaturePatches.SprayPaintCost));
            n += TryPatch(harmony, typeof(FeaturePatches.FishBaitCost));
            n += TryPatch(harmony, typeof(FeaturePatches.MeleeAttachmentCost));
            n += TryPatch(harmony, typeof(LiquidPatches.DrinkFromContainer));
            n += TryPatch(harmony, typeof(LiquidPatches.DrinkLiquidDirect));
            n += TryPatch(harmony, typeof(FoodShelfCost.FreshRatioPatch));

            // 物品表初始化探针：ItemManager 就绪后立即应用一次
            try
            {
                harmony.CreateClassProcessor(typeof(FrameProbe.ItemManagerInitProbe)).Patch();
                harmony.CreateClassProcessor(typeof(FrameProbe.LootTableProbe)).Patch();
            }
            catch (Exception e) { DurabilityConfig.Log("物品表探针挂载失败：" + e.Message); }

            // 周期任务探针：物品表就绪后应用弹匣/食物倍率
            try
            {
                harmony.CreateClassProcessor(typeof(FrameProbe.CharacterInputProbe)).Patch();
                harmony.CreateClassProcessor(typeof(FrameProbe.MapControllerProbe)).Patch();
                DurabilityConfig.Log("已挂载周期探针：CharacterInput + MapController");
            }
            catch (Exception e) { DurabilityConfig.Log("周期探针挂载失败：" + e.Message); }



            return n;
        }

        private static int TryPatch(Harmony harmony, Type type)
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                DurabilityConfig.Log("已挂载补丁：" + type.Name);
                return 1;
            }
            catch (Exception e)
            {
                DurabilityConfig.Log("补丁挂载失败（游戏版本可能已变）：" + type.Name + " -> " + e.Message);
                return 0;
            }
        }

        /// <summary>ItemData -> ItemType 整数；拿不到返回 -1（调用方按 1.0 处理）。</summary>
        internal static int TypeOfItem(ItemData item)
        {
            try
            {
                if (item == null) return -1;
                return ItemLookup.GetItemType(item.itemId);
            }
            catch { return -1; }
        }

        /// <summary>
        /// 新语义：配置值即消耗系数。
        /// 0.5 = 消耗减半；0 = 完全不消耗；1.0 = 原版；2.0 = 消耗翻倍。
        /// </summary>
        internal static float Scale(int itemType, float cost) => Scale(itemType, cost, "Scale");

        internal static float Scale(int itemType, float cost, string patch)
        {
            try
            {

                float f = DurabilityConfig.CostFactor(itemType);
                if (f <= 0f) return 0f;
                if (Math.Abs(f - 1f) < 0.0001f) return cost;
                return cost * f;
            }
            catch { return cost; }
        }

        // ---------- 玩家身上的物品 ----------

        [HarmonyPatch]
        public static class HumanCostItemDurability
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(HumanCharacterController), "CostItemDurability",
                new[] { typeof(ItemData), typeof(float) });

            [HarmonyPrefix]
            public static void Prefix(ItemData itemData, ref float durabilityCost)
            {
                try { durabilityCost = Scale(TypeOfItem(itemData), durabilityCost, "HumanCostItemDurability"); } catch { }
            }
        }

        [HarmonyPatch]
        public static class HumanCostItemDurabilityExecute
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(HumanCharacterController), "CostItemDurabilityExecute",
                new[] { typeof(ItemData), typeof(float) });

            [HarmonyPrefix]
            public static void Prefix(ItemData itemData, ref float durabilityCost)
            {
                try { durabilityCost = Scale(TypeOfItem(itemData), durabilityCost, "HumanCostItemDurabilityExecute"); } catch { }
            }
        }

        // ---------- 背包里的物品 ----------





        [HarmonyPatch]
        public static class InventoryCostItemDurabilityByIdOut
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(InventoryData), "CostItemDurability",
                new[]
                {
                    typeof(int), typeof(float),
                    typeof(Il2CppSystem.Collections.Generic.List<InventoryData>),
                    typeof(float).MakeByRefType(),
                });

            [HarmonyPrefix]
            public static void Prefix(int m_itemId, ref float m_costDurability)
            {
                try { m_costDurability = Scale(ItemLookup.GetItemType(m_itemId), m_costDurability, "InventoryByIdOut"); } catch { }
            }
        }

        // ---------- 衣物 / 护甲受损 ----------



        // ---------- 载具货箱里的物品 ----------

        [HarmonyPatch]
        public static class VehicleCostStorageItemDurability
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(VehicleData), "CostStorageItemDurability",
                new[] { typeof(ItemData), typeof(float) });

            [HarmonyPrefix]
            public static void Prefix(ItemData m_itemData, ref float durabilityCost)
            {
                try { durabilityCost = Scale(TypeOfItem(m_itemData), durabilityCost, "VehicleCostStorage"); } catch { }
            }
        }

        // ---------- 近战武器挥砍 ----------

        /// <summary>
        /// BasicMeleeWeapon.CostWeaponDurabilityPerHit() 没有任何参数，入参改不了。
        /// 观察：近战耐久消耗取自武器属性 durabilityCostPerHit，每次挥砍按它扣。
        /// 做法：挥砍前把该值按倍率缩放，挥砍后还原——只影响这一次扣耐久，
        /// 不写回物品数据、不改存档；异常时一律还原，保证原版行为。
        /// </summary>
        [HarmonyPatch]
        public static class MeleeWeaponDurabilityPerHit
        {
            static MethodBase TargetMethod() => AccessTools.Method(
                typeof(BasicMeleeWeapon), "CostWeaponDurabilityPerHit", Type.EmptyTypes);

            [HarmonyPrefix]
            public static void Prefix(BasicMeleeWeapon __instance, out MeleeState __state)
            {
                __state = null;
                try
                {
                    ItemAttr_MeleeWeapon attr = __instance.weaponAttr;
                    if (attr == null) return;

                    float f = DurabilityConfig.CostFactor((int)ItemType.MeleeWeapon);
                    if (Math.Abs(f - 1f) < 0.0001f) return;

                    float original = attr.durabilityCostPerHit;
                    attr.durabilityCostPerHit = original * f;
                    __state = new MeleeState { Attr = attr, Original = original };
                }
                catch { __state = null; }
            }

            [HarmonyPostfix]
            public static void Postfix(MeleeState __state)
            {
                try
                {
                    if (__state?.Attr != null) __state.Attr.durabilityCostPerHit = __state.Original;
                }
                catch { }
            }

            public sealed class MeleeState
            {
                public ItemAttr_MeleeWeapon Attr;
                public float Original;
            }
        }
    }
}