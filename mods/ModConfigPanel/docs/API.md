# ModConfig Panel · SDK API 参考

> 面向需要用代码注册配置的作者。
> **推荐用 `ModConfigBridge.cs` 软调用，不需要引用 ModConfigPanel.dll。**

---

## 命名空间

```csharp
ModConfigPanel.Api
```

---

## ModConfigApi

平台入口。

| 成员 | 签名 | 说明 |
|---|---|---|
| `ApiVersion` | `const int` | 当前 API 版本（1） |
| `IsPlatformAvailable` | `bool` | 平台是否可用 |
| `Register` | `void Register(ModConfigDeclaration decl)` | 注册/覆盖一个模组的配置声明 |
| `Unregister` | `void Unregister(string modGuid)` | 注销一个模组 |

**注册行为**：同一个 `ModGuid` 重复注册会覆盖旧的。
注册时会保留自动解析得到的当前值（不会因为声明里的 `default` 覆盖玩家的实际配置）。

---

## ModConfigDeclaration

| 字段 | 类型 | 说明 |
|---|---|---|
| `ModGuid` | `string` | **必填**，模组唯一标识 |
| `DisplayName` | `string` | 面板显示名 |
| `Description` | `string` | 简介 |
| `ConfigFile` | `string` | 配置相对路径，如 `BepInEx/config/xxx.cfg`；无配置文件可留 null |
| `RestartRequired` | `bool` | 是否需重启，默认 true |
| `Groups` | `List<GroupDecl>` | 配置分组 |
| `AddGroup(section, title)` | `GroupDecl` | 便捷方法：新增分组并返回 |

---

## GroupDecl

| 字段/方法 | 说明 |
|---|---|
| `Section` | cfg 节名 |
| `Title` | 界面显示标题 |
| `Items` | 配置项列表 |
| `Add(ItemDecl)` | 追加一个配置项 |

---

## ItemDecl 工厂方法

| 方法 | 签名 |
|---|---|
| `Toggle` | `Toggle(string key, string label, bool def = false, string description = null)` |
| `Slider` | `Slider(string key, string label, float min, float max, float step = 0.05f, float def = 1f, string unit = null, string description = null)` |
| `Number` | `Number(string key, string label, float min, float max, bool integer = false, float def = 0f, string description = null)` |
| `Input` | `Input(string key, string label, string description = null)` |
| `Keybind` | `Keybind(string key, string label, string description = null)` |
| `Text` | `Text(string key, string content)` |
| `Button` | `Button(string key, string label, string action, string confirm = null, Dictionary<string,string> prm = null)` |
| `Callback` | `Callback(string key, string label, string typeAndMethod, string confirm = null)` |

`Callback` 等价于 `Button(key, label, "callback:" + typeAndMethod)`。

---

## BuiltinAction 常量

避免手写字符串：

```csharp
BuiltinAction.RestartGame   // "builtin:restart_game"
BuiltinAction.OpenFolder    // "builtin:open_folder"
BuiltinAction.OpenUrl       // "builtin:open_url"
BuiltinAction.ResetSection  // "builtin:reset_section"
BuiltinAction.ShowLog       // "builtin:show_log"
```

---

## 完整示例

```csharp
var decl = new ModConfigDeclaration
{
    ModGuid = "yourname.yourmod",
    DisplayName = "你的模组",
    ConfigFile = "BepInEx/config/yourname.yourmod.cfg",
};

var general = decl.AddGroup("General", "常规");
general.Add(ItemDecl.Toggle("Enabled", "启用", true, "关闭后不生效"));
general.Add(ItemDecl.Slider("Scale", "倍率", 0f, 10f, 0.1f, 1f, "x"));

var ops = decl.AddGroup("Ops", "操作");
ops.Add(ItemDecl.Button("restart", "重启游戏", BuiltinAction.RestartGame, "确定重启吗？"));
ops.Add(ItemDecl.Callback("clear", "清空缓存", "YourMod.Helper.ClearCache"));

ModConfigApi.Register(decl);
```

---

## 安全性

| 约束 | 说明 |
|---|---|
| 回调方法必须无参静态 void | 不接受参数与返回值 |
| 回调必须来自模组程序集 | 禁止调用游戏本体 / BepInEx / UnityEngine / 平台自身 |
| 调用异常被隔离 | 只记日志，不影响游戏与其他模组 |

---

## 兼容性

- 平台升级会保持 `ApiVersion` 向后兼容
- 若未来出现不兼容变更，会提升 `ApiVersion` 并在接入指南中说明
- 软调用方应在调用前检查 `IsPlatformAvailable`
