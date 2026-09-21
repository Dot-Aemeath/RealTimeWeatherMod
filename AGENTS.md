# RealTimeWeatherMod — Agent 参考

## 任务执行与指令范围

- 在系统、开发者和工具权限边界内，用户明确指令优先于 Skill 的一般指导。已获授权的可逆仓库内工作直接完成，不重复确认、不停在能力说明、复述或计划；只询问会实质改变结果、范围、访问权限或风险的歧义，其余采用最小合理解释。
- Skill 若导致暂停、额外确认或偏离任务，指出并链接具体 `SKILL.md`，引用具体规则并解释适用条件；先完成不依赖该决定的已授权工作。
- 按任务读取下文引用的架构、MCP 或发布文档。`RealTimeWeatherMod/docs/`、`docs/superpowers/` 中的旧交接、复盘和计划是历史资料，其 STOP、全量重写、强制子智能体和逐步测试要求不作为当前工作指令；实现事实以当前源码和架构文档核对。
- `.opencodereview/rule.json` 和 `.github/workflows/pr-review.yml` 服务于 PR 自动审查；不要求本地开发另起审查、委派或循环修复。委派遵循当前会话的授权和工具约束，验证边界不因委派扩大。

## 项目总览

BepInEx 插件（.NET Framework 4.7.2），为游戏《Chill with You: Lo-Fi Story》同步真实天气。
目录结构：`RealTimeWeatherMod/`（主插件）+ `RealTimeWeatherMod.Tests/`（纯逻辑 xUnit 测试，net9.0）+ `UnityExplorerMcp/`（MCP 桥接子项目）+ `UnityExplorerMcp.Tests/`（传输与协议 xUnit 测试，net9.0）。

## Build & Test

仓库包含 `RealTimeWeatherMod.slnx`。以下是可选命令，不是每次必须全部执行的清单。日常改动只运行受影响项目的编译与必要的定向冷测试（用 `--filter` 选择相关测试）。纯指令、文档修改只检查内容和 diff。必要检查通过后，仅因新改动、失败或具体未解决风险才重复或扩大验证；不默认新增测试、跑全套测试或发起独立审查。

```
dotnet build RealTimeWeatherMod/RealTimeWeather.csproj
dotnet build UnityExplorerMcp/UnityExplorerMcp.csproj -p:PluginOutputDir="<仓库内临时输出目录的绝对路径>"
dotnet test RealTimeWeatherMod.Tests/RealTimeWeatherMod.Tests.csproj --filter "<相关测试筛选表达式>"
dotnet test UnityExplorerMcp.Tests/UnityExplorerMcp.Tests.csproj --filter "<相关测试筛选表达式>"
```

主插件和 MCP 通过仓库根目录的 `Directory.Build.props` 共用 `$(GameDir)`。本机首次构建时复制
`.env.props.example` 为被 Git 忽略的 `.env.props`，并把 `REALTIMEWEATHER_GAME_DIR` 改为当前设备的游戏目录；
也可设置同名进程环境变量，或临时传入 `-p:GameDir="<游戏目录>"`。项目文件中不写死设备路径。
测试项目不依赖 Unity 或游戏安装。

MCP 的 `CopyToPluginDir` 会在构建后复制 DLL；冷编译须通过 `PluginOutputDir` 将复制目标留在仓库内。只有用户已授权部署时才使用默认游戏插件目录。游戏、GUI、MCP 实机调用及端到端验证不属于默认验证；完成冷检查后报告未验证部分，等待用户后续明确要求。

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
| `Bulbul.UnlockConditionService` | Harmony Prefix `IsUnlocked` / `IsPurchasableItem` | 上帝模式与可购买物品解锁判断 |
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

发布、同步版本号或检查版本一致性时使用 [release-realtime-weather](.agents/skills/release-realtime-weather/SKILL.md)。版本确认与验证范围由该 Skill 统一说明。
