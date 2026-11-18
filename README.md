# ZED ZONE 代码模组集合

本仓库收录为游戏 **ZED ZONE（隔离区 - 丧尸末日生存）** 开发的 BepInEx 代码模组。

所有模组开源，欢迎审查代码、提交 Issue 与 Pull Request。

---

## 模组列表

| 模组 | 版本 | 说明 | 工坊 |
|---|---|---|---|
| [**自定义耐久**](./mods/ZedDurability/) | 1.0.4 | 按物品类别自定义耐久消耗速度 | [链接](https://steamcommunity.com/sharedfiles/filedetails/?id=3837657256) |
| [**模组配置中心**](./mods/ModConfigPanel/) | 1.0.2 | 给所有代码模组提供统一的游戏内配置面板 | [链接](https://steamcommunity.com/sharedfiles/filedetails/?id=3837749722) |

---

## 前置要求

两个模组都需要先安装 **ZED ZONE 代码模组加载器**：

- 创意工坊 ID：**3834614258**
- 安装后在 `steamapps\workshop\content\1211600\3834614258\` 里双击 `install.cmd`
- **必须从 Steam 启动游戏**

---

## 目录结构

```
zed-zone-mods/
├── mods/
│   ├── ZedDurability/        # 自定义耐久
│   │   ├── README.md         # 使用说明
│   │   ├── src/              # 源码
│   │   └── docs/             # 额外文档
│   └── ModConfigPanel/       # 模组配置中心
│       ├── README.md
│       ├── src/
│       └── docs/
└── docs/                     # 跨模组的通用文档
```

新增模组时按 `mods/<模组名>/` 平级放置。

---

## 安全说明

**所有模组均为纯本地运行，不联网、不收集任何数据、不上传任何信息。**

### 文件读写范围

| 模组 | 读取 | 写入 |
|---|---|---|
| 自定义耐久 | `BepInEx\config\zedzone.durabilitymultiplier.cfg` | 无（只读配置） |
| 模组配置中心 | `BepInEx\config\*.cfg`、`Mods\*\modconfig.json` | 对应 cfg 文件（保存时，先备份 `.bak`） |

**不读写游戏存档**，不修改游戏本体文件。

### 行为说明

- 使用 Harmony 在内存中给游戏方法挂前后钩子
- 自定义耐久：只缩放「耐久消耗量」，不改耐久上限、不改存档字段
- 模组配置中心：在游戏中创建一个独立的 UI Canvas，与游戏原有 UI 隔离

### 已知限制

- **枪械本体耐久**：实际存储在 `ItemData.properties[0]`（不是 `durability` 字段），已在 1.0.4 修复
- **耐久上限功能**：暂时关闭。游戏会让模板耐久与实例耐久脱钩，当前实现无法可靠生效
- **食物变质速度**：通过 `ItemData.TryGetFoodFreshRatio` 的新鲜度衰减实现（1.0.5 起）

---

## 构建

需要 .NET 6 SDK 与游戏本体（提供 `BepInEx\core` 和 `BepInEx\interop` 引用）。

```bash
cd mods/ZedDurability
dotnet build -c Release
```

**注意**：`csproj` 里的 `BepInExCore` / `InteropDir` 指向本机游戏路径，克隆后需要改成自己的路径。

---

## 许可证

[MIT](./LICENSE)

---

## 免责声明

本项目与 ZED ZONE 开发者 **Leven Liu** 及发行商 **INDIECN** 无隶属关系。

使用模组导致的存档问题或游戏异常，作者不承担责任。建议使用前备份存档。
