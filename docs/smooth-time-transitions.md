# 平滑时段切换

当前采用固定相对窗口：清晨渐变 60 分钟，傍晚渐变 20 分钟、完整黄昏 20 分钟、渐变入夜 30 分钟，仍随 API 日出日落移动；不按季节额外调整，也不新增黎明预设。文末早期实测记录对应当时的统一 10 分钟窗口，不能当作新时长的验收记录。实机验证按用户要求以加速检查为主。

设置面板的“平滑时段切换”对应 `TimeSync.EnableSmoothTransitions`，默认关闭。自动过渡还需要启用“时间同步”；手动短过渡不依赖时间同步开关。雨雪粒子、雷电及环境音沿用现有同步。

平滑模式采用统一软阴影，夜晚稳定阶段也保持软阴影以避免端点跳变；因此夜晚阴影边缘会比原版柔和。释放控制时恢复原来的阴影模式，不修改源资源。后处理端点基于 Unity 默认参数、当前下层 Volume 和时段覆盖计算，未勾选的参数不使用其无效的序列化值。

Beautify 横向光晕关闭端点的细节层也按时段权重渐变，避免强度归零时整层突隐。仅在接管中的混合阶段增加一次低分辨率临时纹理合成，同一命令缓冲内归还纹理；稳定端点仍走原生效果，不关闭光晕，也不改写源材质或游戏 Shader 资源。适配代码依赖当前游戏 Beautify 的内部字段及渲染 Pass。

自动光照过渡使用真实时刻，曲线为 SmoothStep。设日出为 R、日落为 S：

| 视觉阶段 | 时间范围 | 时长 | R=06:30、S=18:30 的示例 |
|---|---|---|---|
| Night → Day | R−40 分钟 ～ R＋20 分钟 | 60 分钟 | 05:50–06:50 |
| Day → Sunset | S−35 分钟 ～ S−15 分钟 | 20 分钟 | 17:55–18:15 |
| 完整 Sunset | S−15 分钟 ～ S＋5 分钟 | 20 分钟 | 18:15–18:35 |
| Sunset → Night | S＋5 分钟 ～ S＋35 分钟 | 30 分钟 | 18:35–19:05 |

每个渐变窗口中点的混合比例为 50%，不等同于日出日落或游戏逻辑切换时刻。游戏按钮和原有离散决策仍以 R、S−30 分钟、S＋30 分钟切换，关闭平滑后的行为不变。阴雨天沿用现有基础环境映射，两端相同时跳过渐变；不会为雨雪强加晴天黄昏。跨午夜窗口按绝对日期计算；无效或互相重叠的日程沿用离散回退。

启动、开启选项、时钟跳变或日出日落更新时，用 2 秒对齐当前时刻的画面。手动选择也使用 2 秒，并保留到下一次有效自动过渡窗口；日出日落更新时重新计算尚未到期的保持期限。没有有效窗口时不建立长期保持，手动短过渡结束后恢复自动对齐。关闭平滑或时间同步会收敛到逻辑时段并释放控制。剧情和鲸鱼覆盖期间交回游戏，恢复时重新对齐尚未到期的手动选择或当前自动时段。COZY 或 MLS 暂未就绪时每秒最多重试一次，原有启动同步继续工作；进入可接管状态后再进行对齐。

时间计算和控制状态位于 `Pure/Core/TimeTransition.cs`；`GameEnvironmentRuntime` 持有渲染适配器。逻辑时段、UI 和原有持久化仍由游戏入口更新。渲染混合独立于 30 秒同步轮询，不增加天气请求。原生光照贴图混合只有两个输入：短过渡被第三个时段打断时，在同一个 2 秒窗口内连续经过端点预设，避免纹理槽位突换。

兼容性检查在修改场景前执行。存在有效 `lightingScenario` 时使用 MLS 两端副本；未绑定时只处理场景实际使用的灯光和对象。支持同拓扑、兼容材质表面的参数混合、独有透明表面、单一主方向光、对应的全局后处理 Volume，以及 COZY 大气参数。游戏桌灯的发光贴图通过发光贡献渐变处理。不同天空纹理、无法插值的后处理开关、不兼容表面或光照数据会使当前场景回退到原切换方式，并记录一次具体原因。运行时材质和 Volume 副本在结束时释放，源配置保持原样。

`envsync_debug` 快照新增 `smoothTransitionsEnabled`、`transitionMode`、`transitionFrom`、`transitionTo`、`transitionProgress`、`transitionManualUntil`、`transitionUnavailableReason`。原有 `matchesExpected` 仍只比较离散游戏状态，不证明视觉过渡正确。

验证分为两层：定向纯逻辑测试与主插件编译；后续显式授权后的实机检查。实机需覆盖三个晴天边界及夜晚↔阴天、启动和手动打断、剧情退出和场景重载。完成冷检查不能替代这些视觉验收。

## 实机测试方案

### 运行中控制（推荐）

安装本仓库构建的主插件和 UnityExplorer MCP Bridge 后，直接调用 `envsync_debug`，或在仓库根目录运行 PowerShell 助手：

桥接默认不监听端口。调试前按 `UnityExplorerMcp/README.md` 开启本地服务器；天气回放本身不需要 `AllowExecuteCode`。包含资源枚举的周期验收脚本需要临时开启该高权限入口，结束后应关闭并重启或退出游戏。

```powershell
# 晴天 Day → Sunset，3 秒预备对齐后按 20 倍速播放，约 65 秒结束并停在终点
./scripts/Invoke-WeatherDebug.ps1 -Action test_start -Boundary sunset -WeatherCode 1 -Speed 20
./scripts/Invoke-WeatherDebug.ps1 -Action test_pause
./scripts/Invoke-WeatherDebug.ps1 -Action test_seek -Progress 0.5
./scripts/Invoke-WeatherDebug.ps1 -Action test_resume
./scripts/Invoke-WeatherDebug.ps1 -Action test_speed -Speed 1
./scripts/Invoke-WeatherDebug.ps1 -Action snapshot
./scripts/Invoke-WeatherDebug.ps1 -Action test_stop
```

对应 MCP 请求例如 `envsync_debug({"action":"test_start","boundary":"night","weatherCode":11,"speed":20})`。
`boundary` 支持 `dawn`、`sunset`、`night`；统一日出 06:30、日落 18:30，各自完整窗口见上表，播放前后各包含 15 秒模拟时间余量。
`test_seek` 的 `progress` 为所选窗口的线性时间位置；0.5 分别对应 06:20、18:05、18:50。先暂停再 seek 可定格。开始、定位和进度计算共用正式调度的窗口定义。
`speed` 为模拟秒/真实秒，0 暂停、1 原速，最大 600。调速不重启短对齐；seek 会从当前画面进行 2 秒对齐。

连续跨日可调用 `test_cycle`：`days` 为 1–7（默认 2），从当天零点连续播放到指定天数后的零点，仅开始会话时初始化一次控制器。600 倍速播放两天约需 5 分钟，覆盖 6 个时间窗口。此模式下 `testTimelineProgress` 表示整个日程进度，`testWindowProgress=-1`（不适用），`test_seek` 的进度也针对整个日程。

```powershell
./scripts/Invoke-WeatherDebug.ps1 -Action test_cycle -Days 2 -Speed 600
# 自动检查跨日边界、单调进度、终点、每日记录不变，并采集首尾资源数量。
./scripts/Test-TimeTransitionCycle.ps1 -Days 2 -Speed 600
```

600 倍速用于跨日与资源生命周期检查；20–60 倍速用于观察窗口内视觉变化。自动脚本在计时播放之外读取资源数量，避免反射诊断干扰帧时间。

`Test-TimeTransitions.ps1` 默认要求窗口内出现连续混合。对于两端相同的场景，显式使用 `-ExpectSteady` 验证整个窗口保持同一预期环境，例如 `-WeatherCode 11 -Boundary sunset -ExpectSteady`；不能用“没有混合帧”自动判定通过。

测试会话只存在于内存，临时接管时间、天气和平滑开关，不写 cfg，也不改系统时间。
播放期间暂停彩蛋抽取、每日重置和环境音自动调度，避免模拟日期影响用户的每日记录；已有剧情和鲸鱼覆盖仍有效。
`test_stop` 退出会话，清除该会话中的手动时段保持并恢复原配置控制；没有活动会话时不会清除正常游玩中的手动保持。普通 `apply`/`disable` 也会退出会话并清除模拟日历上的手动保持，同时保留它们原有的配置写入语义；切换普通调试场景时也会重置过渡状态。
原速播放可用于细致观察；当前验收优先使用加速窗口和连续跨日播放。加速测试的模拟时长不能当成真实进程运行时长。

快照增加 `testActive`、`testComplete`、`testSpeed`、`testWindowProgress`、`testWindowDurationSeconds`，以及
`testFrameCount`、`testAverageFrameMs`、`testMaxFrameMs`、`testP95FrameMs`、`testFramesOver50ms`、`testGen0Collections`。
帧时间取自 Unity 主线程 `unscaledDeltaTime`，排除最初 3 秒预备和暂停/完成后的帧；P95 用 1ms 桶统计，上限桶 1000ms。
`testWindowDurationSeconds` 在单窗口测试中分别为 3600、1200、1800；无测试或连续跨日时为 0。测试脚本据此计算超时，不再假定统一窗口长度。帧时间数值包含整个游戏及其他插件开销，不是本插件的独占 GPU/CPU 耗时。截图采样也会扰动帧时间，应与无截图的性能运行分开。
可在 `test_start` 中传 `smoothTransitions:false`（PowerShell 用 `-Discrete`）做原生切换对照。
测试仍遵守剧情和鲸鱼覆盖保护；遇到覆盖时应记录，而不是强行推进画面。

COZY 光晕支持相同资源的强度、大小和遮挡半径插值；未配置光晕的一端按零强度淡入淡出。
不同活动光晕资源或离散遮挡规则仍会触发兼容性回退。

### 测试准备

- 使用 `test_start` 时无需改配置，会话临时启用时间、天气及平滑控制。下述旧 `apply` 流程才需要开启 `TimeSync.EnableTimeSync`、`TimeSync.EnableSmoothTransitions` 和天气同步。
- 播放会话会暂停彩蛋自动调度；开始前仍需避开已生效的鲸鱼覆盖和剧情。旧 `apply` 流程需要自行关闭季节彩蛋。
- 先调用 `envsync_debug({ "action": "snapshot" })`，确认 `runtimeReady=true`、`transitionUnavailableReason` 为空。
- 每次记录画面、日志和快照。`matchesExpected` 只验证离散逻辑状态；视觉验收以画面和下列 transition 字段为准。

`envsync_debug` 的 `currentDateTime` 传空字符串时，天气保持模拟值，但时钟继续跟随系统时间。这适合观察完整的自动过渡；传固定时间则适合快速检查某个混合位置。

### 第一轮：快速冒烟

使用日出 `06:30`、日落 `18:30`，每次应用后等待至少 2 秒再读取快照：

| 天气代码 | 固定时间 | 期望视觉状态 |
|---|---:|---|
| `1` 晴 | `06:20` | Night → Day，`transitionProgress` 约 `0.5` |
| `1` 晴 | `18:05` | Day → Sunset，进度约 `0.5` |
| `1` 晴 | `18:50` | Sunset → Night，进度约 `0.5` |
| `9` 阴 | `06:20` | Night → Cloudy，进度约 `0.5` |
| `9` 阴 | `18:05` | Cloudy 单一状态，不应出现无意义过渡 |
| `9` 阴 | `18:50` | Cloudy → Night，进度约 `0.5` |

示例：

```text
envsync_debug({
  "action": "apply",
  "weatherCode": 1,
  "temperature": 20,
  "weatherText": "SmoothTest",
  "currentDateTime": "2026-09-20 18:05:00",
  "sunrise": "06:30:00",
  "sunset": "18:30:00"
})
```

固定时间会触发 2 秒对齐，所以它验证的是目标混合画面、资源兼容性和短过渡，不代表完整窗口已经流畅。

### 第二轮：完整自动过渡与流畅性

优先使用 `test_start` 播放上表完整窗口。需要跟随真实时钟观察时，令窗口起点 `T` 为当前系统时间 15 秒后，并传 `currentDateTime: ""`：

- 日出：`sunrise = T + 40min`，观察 60 分钟。
- 白天进入黄昏：`sunset = T + 35min`，观察 20 分钟。
- 黄昏进入夜晚：`sunset = T - 5min`，观察 30 分钟。

同时保证清晨窗口终点不晚于黄昏窗口起点、入夜窗口终点不晚于次日清晨窗口起点，且原有离散日落边界不跨日（日落早于 `23:30`）。建议至少完整录制以下四条基础边：

1. 晴天 Night → Day。
2. 晴天 Day → Sunset。
3. 晴天 Sunset → Night。
4. 恶劣天气 Cloudy ↔ Night。

验收时检查：

- `transitionProgress` 单调从接近 `0` 到 `1`，窗口中点为 `0.5`；稳定混合时 `transitionMode=scheduled`。
- 天空、大气、主方向光和阴影方向、烘焙光照、后处理、桌灯发光与时段独有透明物体没有单帧闪烁或突然跳变。
- 窗景按钮和逻辑状态只显示目标逻辑时段，没有同时亮起多个互斥时段。
- 对比“关闭平滑”“开启但处于稳定时段”“正在混合”三种情况；记录平均帧率、帧时间尖峰和肉眼卡顿。重点观察第一次建立材质/光照贴图混合对，以及进入和离开混合窗口的瞬间。

### 第三轮：天气与降水组合

自动流畅性不必为每种降水重复原速完整窗口；可用固定窗口中点快速覆盖效果组合，每次等待 2 秒对齐完成：

| 代码 | 场景 | 应观察的额外效果 |
|---:|---|---|
| `13` | 小雨 | 基础时段仍走 Day/Sunset/Night；LightRain 在过渡中保持稳定 |
| `14` | 大雨 | 白天基础为 Cloudy；HeavyRain 不闪断 |
| `11` | 雷雨 | 白天基础为 Cloudy；ThunderRain 与雷电不被时段混合重置 |
| `20` | 雪 | 白天基础为 Cloudy；Snow 不闪断 |

每种天气至少检查一个白天/阴天边界和一个进入夜晚边界。切换天气代码时允许基础环境按既有规则立即变化，但降水对象不应因每帧光照混合反复启停。

### 第四轮：打断、释放和回退

- 手动 Day → Night，并在 1 秒内改点 Cloudy：第二次过渡应从当前画面继续，不应先跳回端点。
- 手动选择后等待超过一次 30 秒轮询：手动状态应保持到下一个自动窗口。
- 在自动混合中关闭平滑、关闭时间同步：应在 2 秒内收敛到逻辑时段并释放控制。
- 在自动混合中进入剧情或触发鲸鱼覆盖，再退出：进入时游戏接管，退出后 2 秒重新对齐。
- 混合中切换场景或重载存档：旧场景材质、Volume 和临时光照场景应释放，新场景重新准备。
- 若 `transitionMode=unavailable`，记录 `transitionUnavailableReason`；此时原有离散切换必须仍可用，不能停在半混合状态。

使用测试会话时，结束后调用 `envsync_debug({ "action": "test_stop" })`，恢复原配置控制且不写配置。使用旧 `apply` 流程时才调用 `disable` 关闭持久化调试配置。

## 2026-09-20 首轮实测记录（修复前）

已备份并替换游戏目录中的主插件和 MCP DLL，源文件与部署文件的 SHA-256 一致；重启后通过运行中的新接口验证。此前 Release 编译成功，定向纯逻辑测试 33 项通过。

### 测试接口与天气状态

- 开始、暂停冻结时间、定位到窗口中心、恢复、调速、播放到终点停止和退出会话均通过实机检查。
- 比较测试前后的时间同步、天气同步、平滑开关和模拟输入等非敏感配置项，内容未变。
- 6 种天气 × 3 个边界共 18 个窗口中心检查，`matchesExpected` 全部为 `true`。这只证明基础环境与降水逻辑状态，不证明粒子连续性或视觉混合。

| 天气代码 | 日出中心 | 黄昏中心 | 夜晚中心 | 降水 |
|---|---|---|---|---|
| 1 | Day | Sunset | Night | 无 |
| 9 | Cloudy | Cloudy | Night | 无 |
| 13 | Day | Sunset | Night | LightRain |
| 14 | Cloudy | Cloudy | Night | HeavyRain |
| 11 | Cloudy | Cloudy | Night | ThunderRain |
| 20 | Cloudy | Cloudy | Night | Snow |

其中 14 项应发生混合，4 项为阴天黄昏不变的对照。18 项均报告 `transitionMode=unavailable`，回退后的离散映射符合预期。

### 平滑验收阻塞

1. 当前游戏的夜晚主方向光为 `Hard` 阴影，其余三个时段为 `Soft`；遮罩均为 -1，Cookie 均为空。`TimeTransitionRendering.InspectRoots` 一次检查全部四个时段，因此也阻止不涉及夜晚的 Day → Sunset 混合，报“时段主光的阴影、遮罩或 Cookie 不一致”。
2. 仅在当前进程临时将夜晚阴影改为 `Soft` 后，新的晴天黄昏测试继续回退，报“时段后处理覆盖项不同: Beautify”。这是 `InspectVolumes` 的参数类型或 `overrideState` 一致性检查；尚未定位具体参数，也未修改后处理配置。

隔离实验结束后已将夜晚阴影恢复为 `Hard` 并读回确认，调用 `test_stop` 后 `testActive=false`、`debugEnabled=false`。实验未修改源资源、系统时间或配置文件。

结论：测试控制接口和离散回退验证通过，平滑功能验收未通过。当前帧时间来自回退路径，不能据此判断真实混合的流畅性；原速连续过渡、粒子连续性、手动打断、剧情/鲸鱼恢复和场景重载仍未完成。后续需处理方向光与 Beautify 的实际资源差异，再进行视觉及帧时间验收，不能直接删除兼容性检查来算作通过。

本机原始快照和接口检查结果保存在忽略目录 `artifacts/playback-live-results.json`，可使用 `artifacts/verify-playback.ps1` 重跑同一矩阵。

## 2026-09-20 第二轮实测记录

修复内容：统一平滑期间的方向光阴影过滤方式；解析后处理有效端点；仅在 Beautify 自动优化开启时归一化两个已证实不生效的剥离开关；按 COZY 自身隐式转换规则展开 `Overridable<float>` 雾参数，修复混合执行时的类型转换异常。原始时段配置未改写。

主插件 Release 编译通过；定向逻辑测试 33 项通过（NuGet 漏洞数据查询不可达产生 NU1900 警告，不影响测试执行）。游戏已正常重启并部署修复版。

| 完整窗口，20 倍速 | scheduled 混合采样数 | 平均帧时间 | P95 | 最大帧时间 | >50ms |
|---|---:|---:|---:|---:|---:|
| Night → Day | 56 | 16.668ms | 17ms | 16.921ms | 0 |
| Day → Sunset | 57 | 16.668ms | 17ms | 17.002ms | 0 |
| Sunset → Night | 57 | 16.667ms | 17ms | 16.902ms | 0 |

三次进度均单调，结束时为 `settled`，逻辑环境符合预期，未触发回退；每次记录到 2 次 Gen0 回收。这些为整个游戏的 Unity 主线程帧时间，不能当作插件独占开销。画面检查确认白天→黄昏、黄昏→夜晚的 50% 中点存在正确的天空与室内混合，而非原版离散端点；静态截图不能证明没有单帧闪烁。

原始记录：`artifacts/time-transition-runs.json`。可重复执行：

```powershell
./scripts/Test-TimeTransitions.ps1 -Speed 20
# 当前三个窗口按原速顺序播放，约 112 分钟；结束后恢复原配置控制。
./scripts/Test-TimeTransitions.ps1 -Speed 1 -OutputPath ./artifacts/time-transition-realtime.json
```

完整逐帧视觉验收、剧情恢复和场景重载不由下述加速检查代替。多云不是当前三个时间场景目标的验收条件。

### 持续运行补充检查

`LongRunningTimeTransitionTests` 的两个定向用例已通过：晴天和小雨各在同一个控制状态实例上连续模拟三天，跨越年界，每天三个窗口共 9 个窗口，并以 30 天进程运行时间为起点。每 10 秒检查一次独立计算的混合权重、逻辑时段及窗口外稳定状态；没有使用 `test_start` 重建控制器。它验证跨日调度，不替代真实帧级渲染和长时间资源检查。

实机读取确认三个时间预设指向同一个 COZY `Clear` 天气资源实例，时段结束时切换天气配置不会在三者之间改用另一种云量预设。

原速测试按用户要求提前停止，未完成三个完整窗口。已保留的局部快照中 P95 为 17ms，但发生过一次约 370ms 长帧；当时同时进行了反射资源诊断和本地测试编译，原因尚未归属，不能宣称全程无卡顿。局部快照见 `artifacts/time-transition-realtime-partial.json`，资源采样见 `artifacts/time-transition-resources.json`。采样期间临时材质为 51、VolumeProfile 为 11，未观察到增长，但这只是局部运行证据。

### 600 倍速连续跨日检查

首轮两天连续播放覆盖 6 个窗口，均单调混合并完成，结束为 Night / `settled`。17277 帧的平均帧时间为 16.669ms、P95 为 17ms、最大 37.569ms，超过 50ms 为 0。首尾材质数量均为 620、临时材质均为 47、VolumeProfile 均为 10；托管内存的两个采样受 GC 阶段影响，不能据此判断泄漏。记录在 `artifacts/time-transition-cycle.json`。

该轮暴露出测试时钟会触发彩蛋每日重置的副作用：结束后日期恢复为真实日期，但未保存测试前抽取记录，不能证明原抽取结果未变。现已在播放期间隔离彩蛋和环境音自动调度，复测脚本将每日记录文件的首尾哈希一致作为通过条件。

另已实机复现并修复测试手动保持残留：夜晚测试中手动选择 Day 后，停止测试能恢复真实 Night / `settled`，且 `transitionManualUntil` 为空。记录在 `artifacts/playback-manual-release.json`。

隔离修复后的两天 600 倍速复测通过：6 个窗口均单调混合并完成，17279 帧平均 16.667ms、P95 17ms、最大 17.238ms，超过 50ms 为 0。首尾材质均为 621、临时材质均为 47、VolumeProfile 均为 10；每日记录文件哈希保持一致。记录在 `artifacts/time-transition-cycle-isolated.json`。这约 5 分钟的实机播放验证了模拟跨日，不等同于真实运行两天，也不能证明每个渲染帧都不存在视觉跳变。

### 小雨组合加速检查

天气代码 13 下，三个完整窗口均以 20 倍速通过，每条记录到 57 个混合采样，进度单调、终点正确且没有回退。平均帧时间均约 16.67ms、P95 均为 17ms；日出/黄昏/入夜最大帧时间分别为 17.438ms、16.995ms、17.037ms，超过 50ms 均为 0。记录在 `artifacts/time-transition-rain-runs.json`。这是运行状态与帧时间检查，没有逐帧录像证明雨粒子的视觉连续性；大雨、雷雨和雪所涉及的 Cloudy 路径仍不计入当前三个时间场景的验收范围。

### 端点单帧天空闪烁

宣传录制时，用户观察到切换后闪烁。Unity `WaitForEndOfFrame` 逐帧记录确认：白天→黄昏首个 `settled` 帧的天空短暂变回蓝天，下一帧恢复黄昏；黄昏→夜晚也有同类单帧跳变。之前的调度、帧时间和中点截图检查没有覆盖该问题，不能用它们声称端点视觉验收通过。

反编译确认 COZY 在 `Update` 的 `RaisePropogateVariables` 计算天空参数，在大气模块 `LateUpdate` 上传 Shader。Mod 在两者之间收敛端点时，`ChangeCozyWeather` 只替换时间模块和大气配置引用，本帧参数仍可能来自旧预设。端点切换后现调用原生 `atmosphereModule.PropogateVariables()` 立即刷新参数，之后仍由原生渲染流程上传；不通过剪掉坏帧掩盖问题。

普通 GDI 窗口捕获在本机返回了陈旧画面，不能作为此次证据或宣传素材。有效录制来自 Unity 渲染结束后，录制开销也不能用于性能验收。修复前原始帧分别位于 `artifacts/flicker-sunset/`、`artifacts/flicker-night/`。

修复版 Release 编译通过（0 警告、0 错误），已部署并核对 DLL 哈希。三个窗口重新进行 180 倍速逐渲染帧录制：未再出现上述单帧旧天空。黄昏端点采样区域相邻帧 RGB 平均绝对差从修复前约 72.7 降至 1.52，夜晚端点从约 64.5 降至 0.046；数值仅辅助定位，仍结合端点前后画面检查，不等同于所有视觉变化都为零。修复后的帧和状态位于 `artifacts/fixed-sunset/`、`artifacts/fixed-night/`、`artifacts/fixed-dawn/`。

宣传 GIF 使用这三段修复后真实录制，按白天→黄昏→夜晚→白天串接，省略窗口之间的稳定时段，保留所有过渡端点，再按固定帧率采样和调色板压缩。小体积版本为 `artifacts/smooth-transitions-promo-compact.gif`；更清晰的 GIF 和 MP4 同名存于 `artifacts/`。录制结束已退出内存测试会话并恢复游戏界面。

### 手动切换末尾的暗部跳变

在“新绿”背景上，经真实 `EnvironmentUI.OnClickButtonChangeTime` 入口录制六个 Day / Sunset / Night 有向切换，确认 Day → Sunset、Night → Sunset 末尾仍有树影和窗边暗部对比度突变。主方向光参数连续，原因不是 2 秒窗口不足。

反编译 Beautify 渲染器及本机 D3D Shader 确认：`AnamorphicFlaresResample`（Pass 11）只对低频光晕乘强度，高频 `_BloomTex` 直接相加；黄昏有效强度为 0，`intensity > 0` 分支退出时残留层突然消失。修复仅让这一残留层按启用光晕端点的混合权重淡入淡出，复用原生 Pass 4 的纹理乘法；原本已随强度变化的低频层不重复衰减。两个端点都启用时保持原生路径。

修复版 Release 编译通过（0 警告、0 错误），已部署并核对 DLL 哈希。六个方向共录制 620 帧，均达到手动目标，结合首尾逐帧对比与中间画面检查，未再观察到上述端点突跳。Day → Sunset 桌面区域相邻帧 RGB 平均绝对差由 0.626 降至 0.031，树木区域由 4.481 降至 0.695；动态树叶使正常相邻帧仍有差异，数值不是通用视觉评分。原始帧及状态在 `artifacts/shadow-corrected-*/`，总览为 `artifacts/shadow-corrected-overview.png`。

此次验证针对当前房间的三个晴天时段，不能外推到所有背景、雨雪粒子、剧情或其他游戏版本。截图采集扰动帧时间，因此性能另用无截图的加速播放测量。

三个完整自动窗口在无截图的 60 倍速复测中均通过：平均帧时间约 16.67ms、P95 均为 17ms，最大帧时间分别为 17.008 / 17.089 / 16.740ms，超过 50ms 均为 0。结果在 `artifacts/time-transition-flares-performance.json`，这仍是整个游戏帧时间而非插件独占成本。测试结束已恢复真实时间控制和游戏界面。补充宣传剪辑 `artifacts/manual-smooth-transitions-promo.gif` 使用修复后真实手动切换的白天→黄昏→夜晚→白天三段，保留端点，省略段间测试准备时间。

### 固定非对称窗口验收（2026-09-20）

自动窗口调整为清晨 60 分钟、傍晚 20 分钟渐变 + 20 分钟完整黄昏 + 30 分钟入夜，范围见文首。手动切换仍为 2 秒；不增加季节规则、黎明预设或天气请求。`TimeTransitionSchedule.Windows` 是正式调度与调试播放共同的窗口源，定位、进度、首尾余量和脚本超时已同步更新。

主插件及 MCP Bridge 的 Release 编译均通过（0 警告、0 错误），已正常重启部署并核对两份 DLL 哈希。41 项定向测试通过，覆盖非对称起止/中点、20 分钟完整黄昏、重叠回退、跨午夜、三天连续调度及原有 2 秒短切换。测试输出包含既有 NU1900 漏洞审计源连接警告，不影响用例执行。

三个完整窗口以 60 倍速、无截图方式通过实机检查：清晨/黄昏/入夜分别记录 114 / 38 / 57 个混合采样，进度单调，结束为正确时段且未回退。平均帧时间约 16.67ms，P95 均为 17ms，最大分别为 17.387 / 17.615 / 16.973ms，超过 50ms 均为 0。结果在 `artifacts/time-transition-new-windows.json`。

暂停并定位到 50% 的实机读回也通过：06:20 / 18:05 / 18:50，各窗口长度为 3600 / 1200 / 1800 秒，实际混合进度均为 0.5；记录在 `artifacts/new-window-seek-results.json`。结束后已退出测试会话并恢复真实输入。本轮针对新时序与调试接口，不替代全天气、全背景或原速逐帧视觉验收。
