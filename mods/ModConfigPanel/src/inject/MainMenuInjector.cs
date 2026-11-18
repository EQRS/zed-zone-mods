using System;
using HarmonyLib;
using ModConfigPanel;

namespace ModConfigPanel.inject
{
    /// <summary>主菜单「模组配置」按钮注入（M0 已验证可行）。</summary>
    public static class MainMenuInjector
    {
        private const string ButtonLabel = "模组配置";

        [HarmonyPatch]
        public static class Patch
        {
            static System.Reflection.MethodBase TargetMethod() =>
                AccessTools.Method(typeof(ModSceneController), "BuildButtonTree", Type.EmptyTypes);

            [HarmonyPostfix]
            public static void Postfix(ref ModSceneController.ButtonNode __result)
            {
                try
                {
                    if (__result == null) return;

                    // 防重复：重进场景时会重新构建
                    if (__result.children != null)
                    {
                        for (int i = 0; i < __result.children.Count; i++)
                        {
                            var c = __result.children[i];
                            if (c != null && c.label == ButtonLabel) return;
                        }
                    }

                    var node = new ModSceneController.ButtonNode();
                    node.label = ButtonLabel;
                    node.action = Il2CppInterop.Runtime.DelegateSupport
                        .ConvertDelegate<Il2CppSystem.Action>((System.Action)(() => OnClick()));
                    __result.children.Add(node);

                    Logx.Info($"[注入] 已添加「{ButtonLabel}」按钮，同级节点 {__result.children.Count} 个");
                }
                catch (Exception e)
                {
                    Logx.Error("[注入] 失败: " + e);
                }
            }
        }

        private static void OnClick()
        {
            Logx.Info("[点击] 打开模组配置面板");
            try { UI.ConfigPanel.Toggle(); }
            catch (Exception e) { Logx.Error("[点击] 打开面板失败: " + e); }
        }
    }
}
