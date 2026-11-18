using System;
using System.Collections.Generic;
using System.Text;
using BepInEx.Configuration;

namespace ZedZone.ZedDurability
{
    /// <summary>
    /// 所有倍率的唯一来源。
    /// 倍率含义（新语义 · 消耗系数）：
    ///   1.0 = 原版消耗；0.5 = 消耗减半（耐用一倍）；0 = 完全不消耗；2.0 = 消耗翻倍。
    /// 内部统一换算成「消耗系数 CostFactor」，各处补丁一律写 cost * CostFactor。
    /// 配置文件：&lt;游戏目录&gt;\BepInEx\config\zedzone.durabilitymultiplier.cfg
    /// </summary>
    public static class DurabilityConfig
    {
        /// <summary>
        /// 只包含**确认有耐久**的物品类型（经数据包与实机双重验证）。
        /// v1.0.3 恢复 RangedWeapon（官方文档确认 hp = 武器耐久）。
        /// 已移除：Ammo(弹药)、Arrow(箭矢)、Throwable(投掷物)、Backpack(背包)、Cloak(斗篷)。
        /// 顺序不可乱改；新增项只能追加到末尾。
        /// </summary>
        /// <summary>
        /// 游戏 ItemType 枚举的完整定义（源码实证）：
        ///   Material=0 Food=1 FaceMask=2 MeleeWeapon=3 RangedWeapon=4 Clothing=5
        ///   Ammo=6 Backpack=7 Throwable=8 Magazine=9 Arrow=10 GunPart=11
        ///   Deployable=12 Container=13 Liqiud=14 Cloak=15
        ///
        /// 关键：数组下标必须严格等于枚举值！删档位时填 null，绝不能压缩数组，
        ///       否则后面的档位全部错位（v1.0.2 曾因此让 Clothing/GunPart/Container/Liquid 全部失效）。
        /// </summary>
        private const int NumItemTypes = 16;

        /// <summary>下标 = ItemType 枚举值；null 表示该类型没有耐久（配置项不创建）。</summary>
        private static readonly string[] KeyByEnum =
        {
            /* 0  Material      */ "Material_材料",
            /* 1  Food          */ "Food_食物",
            /* 2  FaceMask      */ "FaceMask_面罩",
            /* 3  MeleeWeapon   */ "MeleeWeapon_近战武器",
            /* 4  RangedWeapon  */ "RangedWeapon_枪械",
            /* 5  Clothing      */ "Clothing_衣物护甲",
            /* 6  Ammo          */ null,   // 弹药无耐久
            /* 7  Backpack      */ null,   // 背包无耐久
            /* 8  Throwable     */ null,   // 投掷物无耐久
            /* 9  Magazine      */ "Magazine_弹匣",
            /* 10 Arrow         */ null,   // 箭矢无耐久
            /* 11 GunPart       */ "GunPart_枪械零件",
            /* 12 Deployable    */ "Deployable_可放置物",
            /* 13 Container     */ "Container_容器",
            /* 14 Liqiud        */ "Liquid_液体",   // 游戏内拼写为 Liqiud
            /* 15 Cloak         */ null,   // 斗篷无耐久
        };

        private static readonly string[] CommentByEnum =
        {
            /* 0  */ "材料耐用度倍率",
            /* 1  */ "食物保质相关倍率",
            /* 2  */ "面罩耐用度倍率",
            /* 3  */ "近战武器每次挥砍的耐久消耗倍率",
            /* 4  */ "枪械本体耐久（hp 字段）倍率",
            /* 5  */ "衣物/护甲受击磨损倍率",
            /* 6  */ null,
            /* 7  */ null,
            /* 8  */ null,
            /* 9  */ "弹匣每次射击的耐久消耗倍率",
            /* 10 */ null,
            /* 11 */ "枪械零件（枪管等）射击磨损倍率",
            /* 12 */ "可放置物耐用度倍率",
            /* 13 */ "容器（水瓶等）每次取用的耐久消耗倍率",
            /* 14 */ "液体耐用度倍率",
            /* 15 */ null,
        };

        /// <summary>特征级独立档位（不是 ItemType），索引从 16 开始。</summary>
        private static readonly string[] ExtraKeys =
        {
            "LightingTool_照明工具",   // 16
            "FishBait_鱼饵",           // 17
            "LiquidUse_液体消耗",      // 18
        };

        private static readonly string[] ExtraComments =
        {
            "照明工具（手电等）每秒耐久消耗倍率——独立档位，不受材料档影响",
            "鱼饵每次使用的耐久消耗倍率——独立档位，不受食物档影响",
            "每次取用液体（喝水/灌装/加油）的液体消耗倍率——只影响液体存量，不影响容器耐久；2.0 = 每次消耗减半",
        };

        /// <summary>ItemType 档位数（16）+ 特征档位数（3）= 19</summary>
        public const int TotalSlots = NumItemTypes + 3;

        public const int IdxLightingTool = 16;
        public const int IdxFishBait = 17;
        public const int IdxLiquidUse = 18;

        private static readonly ConfigEntry<float>[] Entries = new ConfigEntry<float>[TotalSlots];
        private static readonly float[] Multipliers = new float[TotalSlots];
        private static readonly ConfigEntry<float>[] MaxEntries = new ConfigEntry<float>[TotalSlots];
        private static readonly float[] MaxMultipliers = new float[TotalSlots];

        public static ConfigEntry<bool> Enabled;

        public static int TypeCount => TotalSlots;

        public static void Init(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true,
                "总开关。false = 完全不生效，等同原版。");

            const string section = "Multipliers 倍率";
            const string maxSection = "MaxDurability 耐久上限";

            // ItemType 档位（下标 = 枚举值，null 跳过）
            for (int i = 0; i < NumItemTypes; i++)
            {
                string key = KeyByEnum[i];
                if (key == null) continue;
                Entries[i] = cfg.Bind(section, key, 1f,
                    new ConfigDescription(
                        CommentByEnum[i] + "（1.0=原版消耗；0.5=消耗减半；0=不消耗；2.0=消耗翻倍）",
                        new AcceptableValueRange<float>(0f, 100f)));
                // 耐久上限功能暂时关闭（实现路径未验证），不再生成配置项
            }

            // 特征档位
            for (int i = 0; i < ExtraKeys.Length; i++)
            {
                int slot = NumItemTypes + i;
                Entries[slot] = cfg.Bind(section, ExtraKeys[i], 1f,
                    new ConfigDescription(
                        ExtraComments[i],
                        new AcceptableValueRange<float>(0f, 100f)));
                // 耐久上限功能暂时关闭
            }

            Reload();
        }

        /// <summary>把 ConfigEntry 当前值刷新进缓存数组，并打印生效中的倍率。</summary>
        public static void Reload()
        {
            for (int i = 0; i < Entries.Length; i++)
                Multipliers[i] = Entries[i] == null ? 1f : Clamp(Entries[i].Value);
            for (int i = 0; i < MaxEntries.Length; i++)
                MaxMultipliers[i] = MaxEntries[i] == null ? 1f : ClampPositive(MaxEntries[i].Value);

            Log(Enabled != null && !Enabled.Value
                ? "已禁用（Enabled=false），全部按原版处理。"
                : "生效倍率：" + DescribeActive());
        }

        /// <summary>按 ItemType 整数取「消耗系数」（= 配置值本身）。越界或异常返回 1.0。</summary>
        public static float Get(int itemType) => CostFactor(itemType);

        public static float GetByItemId(int itemId)
        {
            int t = ItemLookup.GetItemType(itemId);
            return Get(t);
        }

        public static float Clamp(float v)
        {
            // 新语义下 0 是合法值（=完全不消耗），负数与 NaN 一律按 0 处理
            if (float.IsNaN(v) || v < 0f) return 0f;
            if (v > 100f) return 100f;
            return v;
        }

        /// <summary>
        /// 统一换算：配置值 → 消耗系数。
        /// 配置 0.5 表示消耗减半，因此系数直接等于配置值；0 表示不消耗。
        /// 所有补丁都应写成 cost * CostFactor(type)，不要再出现除算法。
        /// </summary>
        public static float CostFactor(int itemType)
        {
            if (Enabled != null && !Enabled.Value) return 1f;
            if (itemType < 0 || itemType >= Multipliers.Length) return 1f;
            return Multipliers[itemType];
        }

        /// <summary>是否完全不消耗（配置为 0）。</summary>
        public static bool IsZero(int itemType) => CostFactor(itemType) <= 0f;

        /// <summary>
        /// 耐久上限倍数（独立于消耗配置）。
        /// 2.0 = 新生成物品的初始耐久上限翻倍；1.0 = 原版；0.5 = 上限减半。
        /// 0 视为 1.0（上限为 0 没有意义，且会让物品一生成就损坏）。
        /// </summary>
        public static float MaxFactor(int itemType)
        {
            if (Enabled != null && !Enabled.Value) return 1f;
            if (itemType < 0 || itemType >= MaxMultipliers.Length) return 1f;
            return MaxMultipliers[itemType];
        }

        public static bool MaxHasEffect(int itemType)
        {
            float f = MaxFactor(itemType);
            return f > 0f && Math.Abs(f - 1f) >= 0.0001f;
        }

        /// <summary>上限倍数的取值约束：0 与负数一律按 1.0 处理，避免把物品生成成损坏状态。</summary>
        public static float ClampPositive(float v)
        {
            if (float.IsNaN(v) || v <= 0f) return 1f;
            if (v > 100f) return 100f;
            return v;
        }

        /// <summary>是否需要处理（既不是原版 1.0，也不是 0 的特殊分支）。</summary>
        public static bool HasEffect(int itemType)
        {
            float f = CostFactor(itemType);
            return f > 0f && Math.Abs(f - 1f) >= 0.0001f;
        }

        public static string DescribeActive()
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Multipliers.Length; i++)
            {
                if (Math.Abs(Multipliers[i] - 1f) < 0.0001f) continue;
                if (sb.Length > 0) sb.Append(", ");
                string key = i < NumItemTypes
                    ? (KeyByEnum[i] ?? ("slot" + i))
                    : ExtraKeys[i - NumItemTypes];
                sb.Append(key).Append('=')
                  .Append(Multipliers[i] <= 0f ? "不消耗" : Multipliers[i].ToString("0.##") + "x");
            }
            return sb.Length == 0 ? "全部 1.0（原版）" : sb.ToString();
        }

        public static void Log(string msg) => Plugin.Instance?.Log.LogInfo("[自定义耐久] " + msg);
    }
}