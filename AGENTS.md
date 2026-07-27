# RealTimeWeatherMod — Agent 参考

## 项目总览

BepInEx 插件（.NET Framework 4.7.2），为游戏《Chill with You: Lo-Fi Story》同步真实天气。
目录结构：`RealTimeWeatherMod/`（主插件）+ `RealTimeWeatherMod.Tests/`（纯逻辑 xUnit 测试，net9.0）+ `UnityExplorerMcp/`（MCP 桥接子项目）。

## Build & Test

仓库包含 `RealTimeWeatherMod.slnx`。日常修改优先验证受影响的项目：

```
dotnet build RealTimeWeatherMod/RealTimeWeather.csproj
dotnet build UnityExplorerMcp/UnityExplorerMcp.csproj     # 编译后自动复制 DLL 到游戏 BepInEx/plugins/
dotnet test RealTimeWeatherMod.Tests/RealTimeWeatherMod.Tests.csproj
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
- `WeatherService` 统一调度双数据源（`Seniverse`/`OpenMeteo`），`OpenMeteoWeatherMapper` 将 OpenMeteo weather_code 映射为心知 code
- 主版本源是 `ChillEnvPlugin.PluginVersion`；不要在 AGENTS 中记录易过期的当前版本号
- API：心知天气 Seniverse + OpenMeteo；设置面板可切换数据源

## 反射桥接的游戏类型

| 游戏类型 | Mod 访问方式 | 用途 |
|---------|------------|------|
| `UnlockItemService` | Harmony Postfix `Setup` | 获取解锁服务实例 |
| `Bulbul.EnvironmentController` | EnvRegistry 注册 | 模拟点击切换环境 |
| `Bulbul.EnvironmentUI` | AccessTools `OnClickButtonChangeTime` | 切换时段 |
| `Bulbul.WindowViewService` | 反射 `ChangeWeatherAndTime` | 切换天气+时段 |
| `Bulbul.UnlockConditionService` | Harmony Prefix `IsUnlocked` | 上帝模式（拦截解锁判断） |
| `Bulbul.CurrentDateAndTimeUI` | Harmony Postfix `UpdateDateAndTime` | 日期栏追加天气文本 |
| `Bulbul.SaveDataManager` | 直接引用 | 读取窗景状态 |
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
- **用户交互追踪**：`UserInteractionPatch` 将用户手动开关的环境加入 `UserInteractedMods` 脏标记列表，自动化不再接管
- **过场保护**：`IsInCutscene()` 检查 `RoomGameManager.CurrentMainState != 14`
- **控制台快捷键**：F7=强制刷新API, F8=日志状态, F9=强制重同步（无网络）
- **缓存**：60 分钟 TTL，30 分钟慢钟轮询刷新

## UnityExplorer MCP Bridge

子项目 `UnityExplorerMcp/` 使用 C# 7.3、硬依赖 UnityExplorer。MCP 配置见
`.mcp.json`（loopback 端口 8972）；工具与开发限制见 `UnityExplorerMcp/README.md`。

## 发布新版本

发布或同步版本号时必须使用项目 skill `$release-realtime-weather`。开始修改前必须先让用户明确确认目标版本号。
