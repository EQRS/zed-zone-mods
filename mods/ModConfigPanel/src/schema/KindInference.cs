using System;
using System.Collections.Generic;
using System.Globalization;

namespace ModConfigPanel.Schema
{
    /// <summary>
    /// 从 cfg 解析出的类型信息推算控件类型与编辑能力。
    /// M6 覆盖：Boolean / Single / Int32 / String / 枚举类 / 未知类型。
    /// </summary>
    public static class KindInference
    {
        /// <summary>基础类型 → 控件类型。</summary>
        public static ItemKind InferKind(ParsedKey k)
        {
            string t = k.Type ?? "";
            switch (t)
            {
                case "Boolean":
                    return ItemKind.Toggle;

                case "Single":
                case "Double":
                    return ItemKind.Number;

                case "Int32":
                case "Int64":
                case "UInt32":
                case "Byte":
                    return ItemKind.Number;

                case "String":
                    return ItemKind.Input;     // 字符串可编辑

                case "KeyCode":
                    // 按键绑定：可点击改键
                    return ItemKind.Keybind;

                default:
                    // 其他枚举类（LogLevel / DetourProvider 等）→ 只读
                    if (!string.IsNullOrEmpty(t) && t != "Unknown")
                        return ItemKind.Text;
                    return ItemKind.Number;
            }
        }

        public static bool IsInteger(ParsedKey k)
        {
            switch (k.Type)
            {
                case "Int32": case "Int64": case "UInt32": case "Byte": return true;
                default: return false;
            }
        }

        /// <summary>是否为无法安全编辑的类型（枚举、自定义结构等）。</summary>
        public static bool IsReadOnly(ParsedKey k)
        {
            if (k.Type == null) return false;
            switch (k.Type)
            {
                case "Boolean": case "Single": case "Double":
                case "Int32": case "Int64": case "UInt32": case "Byte":
                case "KeyCode":            // 按键可编辑
                    return false;
                case "String":             // 文本可编辑
                    return false;
                default: return true;
            }
        }

        /// <summary>从字符串值推断数值型初值，失败返回 fallback。</summary>
        public static float ParseFloat(string s, float fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            if (float.TryParse(s.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float f))
                return f;
            return fallback;
        }

        public static bool ParseBool(string s, bool fallback)
        {
            if (string.IsNullOrWhiteSpace(s)) return fallback;
            string v = s.Trim();
            if (string.Equals(v, "true", StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(v, "false", StringComparison.OrdinalIgnoreCase)) return false;
            return fallback;
        }
    }
}
