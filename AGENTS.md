# RealTimeWeatherMod — Agent 参考

## 项目总览

BepInEx 插件（.NET Framework 4.7.2），为游戏《Chill with You: Lo-Fi Story》同步真实天气。
目录结构：`RealTimeWeatherMod/`（主插件）+ `RealTimeWeatherMod.Tests/`（纯逻辑 xUnit 测试，net9.0）+ `UnityExplorerMcp/`（MCP 桥接子项目）+ `UnityExplorerMcp.Tests/`（传输与协议 xUnit 测试，net9.0）。

## Build & Test

仓库包含 `RealTimeWeatherMod.slnx`。日常修改优先验证受影响的项目：

```
dotnet build RealTimeWeatherMod/RealTimeWeather.csproj
dotnet build UnityExplorerMcp/UnityExplorerMcp.csproj     # 编译后自动复制 DLL 到游戏 BepInEx/plugins/
dotnet test RealTimeWeatherMod.Tests/RealTimeWeatherMod.Tests.csproj
dotnet test UnityExplorerMcp.Tests/UnityExplorerMcp.Tests.csproj
```

主插件和 MCP 都通过 `$(GameDir)` 引用本地游戏程序集；默认路径不可用时传入
`-p:GameDir="<游戏目录>"`。测试项目不依赖 Unity 或游戏安装。

## Git 提交规范

- **中文提交信息**，格式 `<类型>: <描述>`
- 类型：`feat` `fix` `refactor` `docs` `test` `chore`

## 入口与架构

- `ChillEnvPlugin.Awake()`  插件入口：Harmony PatchAll → InitConfig → 创建隐藏 GO (`ChillEnvSyncRunner`) 挂载 3 个 MonoBehaviour
- Harmony 自动扫描补丁 + 手动 `UnlockConditionGodMode.ApplyPatches()`
- 三种 MonoBehaviour：`AutoEnvRunner`（天气轮询+环境切换）、`SceneryAutomationSystem`（彩蛋规则）、`ModSettingsIntegration`（设置面板集成）
- `GameEnvironmentRuntime` 集中管理环境控制器、EnvironmentUI 查找、状态读取及启动期/常规切换 fallback；调用方不直接反射这些游戏类型
- `WeatherAcquisitionModule` 通过 Provider Adapter 统一双数据源、缓存与 fallback；`WeatherSyncModule` 统一启动期/定时同步编排，Unity 副作用经 `IWeatherSyncEffects` 注入
- `SceneryAutomationOwnershipState` 维护用户接管/自动托管不变量；`SceneryDailyTriggerState` 维护五个概率彩蛋（含蓝蝶）的抽取、跨日重置与鲸鱼覆盖状态，持久化按 `All` 遍历
- **主线程约定**：当前彩蛋状态、所有权及其持久化都由 Unity 主线程调用。新增跨线程入口时先派发回主线程；若以后确需并发访问，应重新设计同步边界，不要继续依赖当前前提
- 天气源 Adapter 按 `Name` 精确匹配，匹配不到落到默认源；共享的 HTTP→解析协议在 `ProviderJsonRequest`
- `EnvironmentDecision` 是基础环境/降水景色的唯一决策入口（常规同步、启动同步、`envsync_debug` 期望值共用）；`ModEnvironmentMap` 负责与游戏 `EnvironmentType` 互转
- `ModEnvironmentContext.Capture()` 每轮只调一次，`EnvironmentInputSnapshot` 沿调用链传递；彩蛋规则的日历/天气判定在 `SceneryConditions`
- 主版本源是 `ChillEnvPlugin.PluginVersion`；不要在 AGENTS 中记录易过期的当前版本号
- API：心知天气 Seniverse + OpenMeteo；设置面板可切换数据源
- **`RealTimeWeatherMod/Pure/` 是 Unity-free 逻辑区**：两个 csproj 均以通配符 `Pure\**\*.cs` 引入，测试项目不引用 Unity/BepInEx/游戏程序集，因此编译器会阻止反向依赖；这只保证编译边界，不等于行为测试覆盖率。反射、文件 IO、Unity 与 Harmony 适配器留在 `Core/`、`Services/`、`Patches/` 并由主 csproj 显式列出。发布产物仍是单个 `RealTimeWeatherMod.dll`
- 模块边界和依赖方向见 `docs/architecture.md`

## 反射桥接的游戏类型

| 游戏类型 | Mod 访问方式 | 用途 |
|---------|------------|------|
| `UnlockItemService` | Harmony Postfix `Setup` | 获取解锁服务实例 |
| `Bulbul.EnvironmentController` | Harmony Postfix `Setup` → `GameEnvironmentRuntime` | 状态读取、直连或模拟点击 |
| `Bulbul.EnvironmentUI` | `GameEnvironmentRuntime` 反射 `ChangeTime` / `OnClickButtonChangeTime` | 启动期及常规时段切换 |
| `Bulbul.WindowViewService` | 经 EnvironmentUI 获取，反射 `ActivateWindow` / `DeactivateWindow` | 启动期降水窗景同步 |
| `Bulbul.UnlockConditionService` | Harmony Prefix `IsUnlocked` | 上帝模式（拦截解锁判断） |
| `Bulbul.CurrentDateAndTimeUI` | Harmony Postfix `UpdateDateAndTime` | 日期栏追加天气文本 |
| `Bulbul.SaveDataManager` | `GameEnvironmentRuntime` 直接引用并兼容旧字段 | 读取窗景状态 |
| `RoomGameManager` | 反射 `CurrentMainState` | 过场剧情检测（state≠14 时禁止同步） |

## 天气 Code → 游戏环境映射

| 游戏效果 | 心知天气 Code |
|---------|--------------|
| Snow | 20-25 |
| ThunderRain | 11, 12, 16-18 |
| HeavyRain | 10, 14, 15 |
| LightRain | 13, 19 |
| OFF | 0-9, 26-39（阴天/多云强制关雨雪） |

## 关键约束

- **反射脆弱性**：`_environmentDic`、`_isLocked`、`_windowViewService` 等字段名依赖游戏版本
- **无存档写入**：解锁仅在当前会话生效
- **用户交互追踪**：`UserInteractionPatch` 只调用 `RecordUserInteraction`；该入口原子化完成用户接管、托管释放及持久化，自动化不再接管
- **过场保护**：`IsInCutscene()` 检查 `RoomGameManager.CurrentMainState != 14`
- **控制台快捷键**：F7=强制刷新API, F8=日志状态, F9=缓存优先重同步（缓存失效时可能联网）
- **缓存**：60 分钟 TTL，30 分钟慢钟轮询刷新

## UnityExplorer MCP Bridge

子项目 `UnityExplorerMcp/` 使用 C# 7.3、硬依赖 UnityExplorer。标准 Streamable HTTP 入口为
`http://localhost:8972/mcp`；配置见 `.mcp.json`，工具与开发限制见 `UnityExplorerMcp/README.md`。

## 发布新版本

发布或同步版本号时必须使用项目 skill `$release-realtime-weather`。开始修改前必须先让用户明确确认目标版本号。
