# UnityExplorer MCP Bridge

将 UnityExplorer 的运行时调试能力暴露为 **MCP (Model Context Protocol)**，让 AI 助手能直接「看到」游戏运行时的 UI 布局、组件属性和场景结构。

## 解决的问题

设计 Mod 前端 UI 时，AI 无法看到游戏运行时的实际 UI 布局。反编译只能看到代码逻辑，看不到运行时坐标、锚点、层级关系。本插件填补了这个缺口。

## 架构

```
AI 助手 (opencode)
  │  MCP Streamable HTTP
  ▼
UnityExplorerMcp Plugin (BepInEx)
  │
  ├── TcpListener (localhost:8972)
  ├── Unity API (Resources.FindObjectsOfTypeAll)
  └── Mono.CSharp (UnityExplorer 内置)
```

## 安装

### 前置要求

- **BepInEx 5.4.x** — Unity Mod 加载器
- **UnityExplorer 4.9.0** (BepInEx 5 Mono 版) — 运行时调试器

### 部署

1. 编译项目：`dotnet build UnityExplorerMcp/UnityExplorerMcp.csproj`
2. DLL 自动复制到 `BepInEx/plugins/UnityExplorerMcp/`
3. 启动游戏

传输层单元测试不依赖游戏安装：

```powershell
dotnet test UnityExplorerMcp.Tests/UnityExplorerMcp.Tests.csproj
```

### AI 助手配置

如需在 opencode 中使用，在 `opencode.json` 中添加：

```json
"unity-explorer": {
  "type": "remote",
  "url": "http://localhost:8972/mcp",
  "enabled": true
}
```

## MCP 工具

| Tool | 功能 | 典型用途 |
|------|------|---------|
| `get_ui_hierarchy` | 获取限深 Canvas → UI 树 | 查看局部布局；默认只返回活动对象和 5 层，避免大响应 |
| `inspect_element` | 按路径或 `instanceId` 查看组件详情 | 精确区分同名对象，查看 Button、Text(TMP)、Image 属性 |
| `search_elements` | 按名称、路径、可见文本和组件类型搜索 | 优先使用；默认只返回 25 个活动元素及屏幕像素范围 |
| `get_mouse_element` | 获取鼠标下方的 UI 命中栈 | 跨所有 Canvas 返回最上层开始的多个元素 |
| `inspect_screen_point` | 按截图坐标获取 UI 命中栈 | 默认使用左上角原点；事件射线无结果时自动按可见图形矩形兜底 |
| `compare_ui_elements` | 比较两个 UI 元素 | 返回中心、尺寸、边缘差值和重叠范围，适合检查对齐与间距 |
| `preview_ui_layout` | 临时预览位置和尺寸调整 | 可相对另一控件放置、匹配宽高；返回可回滚的 `previewId` |
| `rollback_ui_preview` | 回滚临时 UI 调整 | 指定 `previewId` 回滚一组预览；省略则回滚全部 |
| `search_ui_sprites` | 搜索已加载的 Sprite | 查看尺寸、纹理和最多 5 个使用位置，复用游戏原生视觉样式 |
| `execute_code` | 在游戏内执行 C# 代码 | 运行任意 C# 查询（返回 8000 字符以内结果） |
| `envsync_debug` | 控制 RealTimeWeatherMod 确定性调试场景 | 一次写入天气/时钟/日出日落并返回期望状态与游戏实际状态 |
| `get_console_logs` | 获取最近 N 条 Unity 日志 | 排查运行时错误 |
| `take_screenshot` | 游戏截图（base64 PNG） | 可指定输出尺寸来降低传输量，让 AI 查看当前画面 |
| `list_scenes` | 列出所有场景及根对象 | 了解游戏场景结构 |

### 典型用法示例

```csharp
// 查找所有 Button
execute_code("GameObject.FindObjectsOfType<Button>().Length")

// 分析一个 GameObject 的所有组件
inspect_element("Canvas/SomePanel/Button")

// 按用户在屏幕上看到的文字查找；关闭的设置面板需要 activeOnly=false
search_elements({ "query": "天气", "activeOnly": false, "maxResults": 25 })

// 搜索结果会返回 instanceId；同名对象较多时用它精确检查
inspect_element({ "instanceId": 12345 })

// 只在确实需要结构上下文时读取限深 UI 树
get_ui_hierarchy({ "canvasName": "SettingsCanvas", "maxDepth": 5 })

// 从 1200×720 截图上点击的位置定位 UI（默认左上角为 0,0）
inspect_screen_point({ "x": 438, "y": 266, "maxResults": 8 })

// 比较导出按钮与原生新建按钮
compare_ui_elements({ "instanceIdA": 12345, "instanceIdB": 67890 })

// 把导出按钮临时贴到新建按钮下方并匹配尺寸
preview_ui_layout({
  "instanceId": 12345,
  "referenceInstanceId": 67890,
  "placement": "below",
  "gap": 4,
  "matchWidth": true,
  "matchHeight": true
})

// 截图确认后回滚；将上一调用返回的实际 previewId 传入
rollback_ui_preview({ "previewId": "<previewId>" })

// 查找游戏内现成的圆圈/对勾素材及其使用位置
search_ui_sprites({ "query": "todo_circle", "maxResults": 20 })

// 模拟 17:45 的雷雨场景，并立即验证 Mod 是否真的应用成功
envsync_debug({
  "action": "apply",
  "weatherCode": 11,
  "temperature": 18,
  "weatherText": "DebugThunder",
  "currentDateTime": "2026-08-03 17:45",
  "sunrise": "06:00",
  "sunset": "18:00"
})

// 不改状态，只读取有效输入、期望环境、实际活动环境和 matchesExpected
envsync_debug({ "action": "snapshot" })

// 关闭调试场景并恢复真实天气/时钟输入
envsync_debug({ "action": "disable" })
```

### 推荐定位流程

1. 用户把鼠标移到需要调整的控件上，调用 `get_mouse_element`。
2. 已知按钮文案时，先调用 `search_elements`；它会同时匹配 GameObject 名称、完整路径和 Text/TMP 文本。
3. 使用结果中的 `instanceId` 调用 `inspect_element`，避免同名预制体或隐藏副本造成误判。
4. 依据 `screenRect` 的屏幕像素范围和 `rectTransform` 的锚点/局部坐标修改 Mod。
5. 只有需要父子结构时再调用 `get_ui_hierarchy`，并尽量指定 `canvasName` 和较小 `maxDepth`。

调整布局时推荐使用安全闭环：`search_elements` / `inspect_screen_point` 定位 →
`compare_ui_elements` 量化差异 → `preview_ui_layout` 临时预览 → 截图确认 →
`rollback_ui_preview` 恢复运行时 UI → 修改并构建 Mod 源码。预览只修改当前游戏内存，
不会写入资源或源码；同一个 `previewId` 可包含多个元素并一次性回滚。

`search_elements` 默认 `activeOnly=true`、`maxResults=25`。若要检查尚未打开的设置页，显式传入
`activeOnly=false`。空查询会枚举指定组件，但仍受 `maxResults` 限制，并通过 `totalMatches` /
`truncated` 告知是否还有更多结果。

## 协议

- 端点：`http://localhost:8972/mcp`
- 传输：标准 MCP Streamable HTTP（JSON-RPC 2.0），仅支持当前协议版本 `2026-07-28`
- 不提供 `initialize` 或旧版协议兼容层；客户端通过逐请求 `_meta` 与 `server/discover` 工作
- `POST /mcp` 要求 `Content-Type: application/json`，且 `Accept` 同时声明 `application/json` 与 `text/event-stream`
- 当前请求必须让 `MCP-Protocol-Version`、`Mcp-Method`、必要时的 `Mcp-Name` 与正文逐项一致；不一致会在工具执行前返回结构化 `400`
- JSON-RPC 请求直接返回 `200 application/json`；可接受的 notification 返回无正文的 `202 Accepted`
- `GET /mcp` 按当前规范返回 `405 Method Not Allowed`，不再建立旧式 endpoint-event SSE 长连接
- 服务无会话状态且不发送 `Mcp-Session-Id`，多个客户端可并发、独立调用同一端点
- 仅监听 loopback；只接受发往本机 Host、且不带浏览器 `Origin` 的请求
- 服务没有鉴权；这里的 Host/Origin 校验用于阻止浏览器借 localhost 调用 `execute_code`，不用于防御已能在本机运行程序的攻击者。不要端口转发或反向代理到其他设备

## 项目结构

```
UnityExplorerMcp/
├── Plugin.cs                    — BepInEx 主入口
├── MCP/
│   ├── McpServer.cs             — loopback TCP 宿主与并发连接调度
│   ├── StreamableHttpProtocol.cs— Streamable HTTP 与 HTTP/1.1 编解码
│   ├── McpStreamableHttpRequestValidator.cs — 逐请求元数据与镜像请求头校验
│   ├── McpJsonRpcHandler.cs     — 当前 MCP 协议发现与工具分发
│   ├── JsonHelper.cs            — 轻量 JSON 序列化
│   └── Tools/                   — MCP 工具实现
├── Core/
│   ├── UiHierarchyTraverser.cs  — UI 树遍历引擎
│   ├── ConsoleCaptureService.cs — Unity 日志捕获
│   ├── MainThreadDispatcher.cs  — 主线程调度
│   └── MonoCSharpEvaluator.cs   — C# 代码执行引擎
└── Models/
    └── UiElementInfo.cs         — UI 元素数据模型

UnityExplorerMcp.Tests/          — 不依赖 Unity 的传输与协议契约测试
```

## 开发注意

1. **Mono.CSharp** — 集成在 UnityExplorer.BIE5.Mono.dll 中，运行时通过 `Reflection.Emit` 创建 public 基类解决 accessibility 问题
2. **C# 7.3** — 目标 .NET Framework 4.7.2
3. **主线程** — Unity API 需在主线程调用，通过 `SynchronizationContext.Post` 调度
4. **BepInEx OnDestroy** — 插件在 chainloader 启动后被销毁（未知根因），当前方案是 OnDestroy 不停止后台线程服务器

## 已知限制

- `execute_code` 依赖 UnityExplorer 内置的 Mono.CSharp，卸载 UnityExplorer 后不可用
- UI 树默认限深；需要更多层级时可将 `maxDepth` 提高到 10
- `search_ui_sprites` 只能搜索当前已加载的 Sprite；未打开过的界面资源可能尚未加载
- `preview_ui_layout` 可能被 Unity 的 LayoutGroup 或界面刷新逻辑在下一帧覆盖；这通常说明应修改布局控制器而非子元素坐标
- 重载场景或销毁预览目标后，该目标无法再回滚；建议预览确认后立即调用 `rollback_ui_preview`

## 未来计划

- [ ] 拆分为独立开源 MCP 项目发布
- [ ] 标准 MCP 安装包
- [ ] 支持更多运行时查询（材质、动画、粒子系统）
- [ ] Mouse-Inspect 视觉反馈
- [ ] 需要列表变更通知时，为 `subscriptions/listen` 增加请求作用域 SSE 响应
