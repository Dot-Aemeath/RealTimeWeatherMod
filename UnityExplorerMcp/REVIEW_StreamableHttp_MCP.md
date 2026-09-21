> 历史审阅快照：下文关于“仅支持 2026-07-28 / 无 initialize”的结论已过时。当前支持版本、握手与传输行为以 [README.md](README.md) 和源码为准；本文保留原始审阅记录。

# UnityExplorerMcp Streamable HTTP MCP 迁移审阅报告

> 审阅对象：`UnityExplorerMcp/`（C# 7.3 / .NET Framework 4.7.2，BepInEx 子插件）
> 基线提交：`6d88544`（HEAD），重点对照迁移提交 `34f0b93` 与 UTF-8 修复提交 `6d88544`
> 范围：架构与数据流、协议合规性、工具清单、测试质量、代码级问题、落地优化建议

---

## 1. 架构与数据流

### 1.1 调用链与线程模型总览

```
UnityExplorerMcpPlugin.Awake() [Unity 主线程]
  └─ MainThreadDispatcher.InitializeFromMainThread()        // 缓存主线程 SynchronizationContext
  └─ new McpServer(port, logger)
  └─ RegisterTool(...) × 14                                  // 注册全部 IMcpTool
  └─ _mcpServer.Start()                                      // 起 accept 线程

McpServer.Start()                                            // 调用线程(Awake)，非阻塞返回
  └─ TcpListener(IPAddress.Loopback, port).Start()
  └─ new Thread(AcceptClients){ IsBackground=true }          // "MCP-Streamable-HTTP" accept 线程

AcceptClients()                                              // 独立后台线程 [线程 A]
  └─ AcceptTcpClient() 循环
  └─ ThreadPool.QueueUserWorkItem(HandleClient, client)      // 每连接一个线程池工作项

HandleClient()                                               // 线程池线程 [线程 B/C/...，并发]
  ├─ McpHttpWire.ReadRequest(stream, MaxRequestBytes=1MiB)   // 同步读完整请求
  ├─ StreamableHttpProtocol.CreateResponse(request)          // 同步校验+分发
  │    ├─ McpStreamableHttpRequestValidator.Validate(...)    // 逐请求元数据校验
  │    └─ McpJsonRpcHandler.Process(message)                 // JSON-RPC 分发
  │         └─ _executeTool(tool, args)                      // => McpServer.ExecuteTool
  │              ├─ MainThreadTool? ──> MainThreadDispatcher.Execute( 10s 超时 )
  │              │      └─ _mainContext.Post(...) + mre.Wait(10000)   // marshal 回 Unity 主线程
  │              └─ 否则直接 tool.Execute(args)               // 非 UI 工具（如 get_console_logs）
  └─ WriteResponse(...)                                       // 同步写回，随即关闭连接
```

### 1.2 各组件职责

| 文件 | 职责 | 关键方法/成员 |
|---|---|---|
| `Plugin.cs` | BepInEx 入口，装配全部工具，生命周期 | `Awake()` 注册 14 工具并 `Start()`；`OnDestroy()` 置 `_shutdownRequested` 但**不停止服务器**；`OnApplicationQuit()` 才 `Stop()` |
| `MCP/McpServer.cs` | 本地 TCP 宿主（改造后职责收窄为「注册 + 生命周期」） | `Start()`/`Stop()`/`AcceptClients()`/`HandleClient()`/`ExecuteTool()`；持有 `ToolRegistry`、`StreamableHttpProtocol` |
| `MCP/StreamableHttpProtocol.cs` | HTTP/1.1 编解码（`McpHttpWire`）+ 传输层策略（`CreateResponse`） | `CreateResponse()` 状态码协商；`McpHttpWire.ReadRequest/WRITE/ParseHeaders/TryDecodeChunkedBody` |
| `MCP/McpStreamableHttpRequestValidator.cs` | 逐请求头/正文一致性校验 | `Validate()`；`TryGetCurrentRequestMetadata`；`TryDecodeMirroredHeader` |
| `MCP/McpJsonRpcHandler.cs` | 纯 JSON-RPC 方法分发，无 Unity 依赖 | `Process()`；`server/discover`、`tools/list`、`tools/call`；自定义错误码 |
| `MCP/JsonHelper.cs` | 手写 JSON 序列化/解析（`JsonParser`） | `Serialize/DeserializeSimple/SerializeJsonRpc*` |
| `Core/MainThreadDispatcher.cs` | 主线程 marshal 桥接 | `InitializeFromMainThread/Execute(timeout)/ExecuteAsync` |
| `Core/ConsoleCaptureService.cs` | Unity 日志环形缓冲捕获 | `logMessageReceivedThreaded` 监听 |

### 1.3 线程模型细节与死锁风险分析

**accept → 每连接一线程池工作项**：`McpServer.cs:78-79`，`AcceptClients` 是唯一 accept 线程；每个 `TcpClient` 交给 `ThreadPool.QueueUserWorkItem`。优势：天然并发支持多客户端；劣势：线程池线程数无上限，连接洪泛会撑爆线程池（见 §5 高优先级问题）。

**每连接处理**：`HandleClient`（`McpServer.cs:92-135`），同步读取完整请求 → 校验 → 分发 → 写回 → `using` 关闭。`ReceiveTimeout=SendTimeout=15000`（`97-98`），一次连接只处理一个请求（`Connection: close`，无 keep-alive）。

**Unity 主线程交互（重点）**：`ExecuteTool`（`McpServer.cs:137-157`）：凡 `tool is MainThreadTool` 就经 `MainThreadDispatcher.Execute(on 10s 超时)` marshal 到 `_mainContext.Post` 回调，工作线程 `mre.Wait(10000)` 阻塞等待。

**死锁风险（真实存在）**：
1. **Unity 主线程正忙 → 10s 超时抛 `TimeoutException` → 工具返回错误**。主线程忙于加载、特写、脚本编译时，工具调用会超时。不是死锁但可感知延迟。
2. **主线程回调内再调工具 = 死锁**：若某 `MainThreadTool.ExecuteOnMain` 内部又调用另一个需要 marshal 的入口（如反射触发了 `MainThreadDispatcher.Execute`），`mre.Wait` 会让主线程等自己 → 必然 10s 超时。当前工具实现未发现此类嵌套，但 `execute_code` 的 `MonoCSharpEvaluator` 是「跑到一起」的风险点——它直接在 marshal 后的主线程上下文执行任意 C#（`ExecuteCodeTool.cs:38` → `MainThreadDispatcher` 内），若用户输入代码里再调用 MCP 工具则死锁。
3. **ConsoleCaptureService 无主线程依赖**：`logMessageReceivedThreaded` 是线程安全回调，`_lock` 保护，非 `MainThreadTool`，直接在 `ExecuteTool` 的非 marshal 分支执行（`McpServer.cs:148`），不在主线程——正确。

**并发安全**：`ToolRegistry`（`Dictionary`）只在 `Awake` 注册（单线程）后只读，工具列表排序稳定（`ToolRegistry.cs:25` `OrderBy(Name)`）→ 并发读安全。`_toolRegistry.GetToolDescriptions()` 返回新 List，无共享可变状态问题。

### 1.4 明显可改进的连接模型

- 无 keep-alive：每请求新建 TCP 连接。MCP 客户端（opencode）通常复用连接并逐个发请求，这里每个工具调用都重建连接 + 握手，开销可见但可接受（local）。
- **无连接数上限 / 无请求排队 / 无每 IP 限速**：loopback 上仍可被本机进程 SYN 洪泛拖垮（见 §5、§6 安全）。
- 缺少请求体读取的硬中断：虽然有 `ReceiveTimeout`，但 `ReadRequest` 的循环只在 `stream.Read` 超时才退出，配合 1MiB 上限可接受，但慢速分块读取可占住线程池线程 15s。

---

## 2. 协议合规性（对照 MCP Streamable HTTP 规范）

> 对照 2025-03-26 / 2025-06-18 draft。注意：本项目采用的是**非标准自定义协议版本 `2026-07-28`**，且用请求内 `_meta` 携带版本/能力信息，这是对 MCP「initialize 握手」机制的简化替代，本身就不是标准 MCP 客户端可直连的形态。

### 2.1 逐项对照表

| 规范点 | 实现 | 状态 |
|---|---|---|
| 端点 | `POST /mcp`（`EndpointPath="/mcp"`，`StreamableHttpProtocol.cs:70`） | ✅ 符合 |
| 协议版本协商 | 仅支持 `2026-07-28`；无 `initialize`，改为每请求 `_meta + MCP-Protocol-Version/Mcp-Method/Mcp-Name` 头 | ⚠️ 非标准（自造版本串 + 头镜像机制） |
| `Accept` 协商 | POST 要求同时含 `application/json` 与 `text/event-stream`（`124-133`） | ✅ 符合；但**禁止**缺任一 |
| `Content-Type` | 要求 `application/json`（`124-127`） | ✅ |
| `GET /mcp` | 返回 `405 Method Not Allowed`（`105-110`），不做 SSE 长连接 | ⚠️ 简化——规范允许 GET 建 SSE 流，本实现选择不支持（README 声明） |
| `DELETE` | 未实现 → 落入 405（`112-117`） | ⚠️ 简化——规范用于终止 session，本服务无 session |
| `Mcp-Session-Id` | 完全不产生、不校验、无服务端会话状态（README 明确「无状态」） | ⚠️ 核心简化；多客户端 stateless 调用靠逐请求头 |
| 响应类型 | JSON-RPC 直接 200 `application/json`；notification 回 202 空体 | ✅ 合理，因为本服务不产生请求级通知 |
| JSON-RPC 批处理 | **不支持**（`JsonParser.ParseDocument` 只解析单个对象，数组 → Parse error `-32700`） | ⚠️ 简化，见 §5 |
| 通知 vs 响应 | 无 `id` ⇒ 判为 notification 回 202（`McpJsonRpcHandler.cs:133-136`） | ✅ |
| 客户端响应消息 | 请求带 `result` 无 `method` ⇒ `Invalid(-32600)`（`103-106`） | ✅ 防御 |
| 错误码映射 | 自定义 `-32020`（HeaderMismatch）/`-32022`（不支持版本）；沿用 `-32700/-32600/-32601/-32602/-32603` | ⚠️ `-32020/-32022` 是**未注册保留码段内的自定义码**，可能与客户端实现冲突 |
| 方法未找到 | HTTP `404` + JSON-RPC `-32601`（`167-172`） | ⚠️ 有争议：`404` 用于「MCP 方法不存在」而非 HTTP 资源不存在，见 §5 |
| SSE 流 | 完全不实现 | 有意简化（README「未来计划」列出 `subscriptions/listen`） |
| `server/discover` | 非标准方法，返回 `resultType/ttlMs/cacheScope`（`McpJsonRpcHandler.cs:191-208`） | ⚠️ 非标准——这是该实现的「能力发现」替代 initialize |

### 2.2 主要偏差与影响

1. **无 `initialize` / 无 session**：标准 MCP SDK 客户端（TypeScript/Python reference client）握手会失败。该实现只适配「自定义 + 头镜像」的客户端（opencode remote adapter）。ROI：简化多连接并发、免 session 生命周期管理。
2. **`Accept` 强制同时含两个类型**：合法 MCP 客户端可能只声明 `application/json`，会被 406 拒绝。规范建议 POST 响应可 JSON 或 SSE；这里要求两个都声明是「客户端必须明确要 JSON」的变通。可接受但会挡掉只带单 Accept 的客户端。
3. **无 SSE**：无 `subscriptions/listen` 能力，无法获得「列表变更」「资源变化」推送。当前工具集（查询型 + `envsync_debug` apply）不需要推送，符合现状。
4. **`GET` 405**：规范客户端若先发 GET 探测 SSE 流，会得到 405 而不是流，客户端应回退到 POST——多数实现支持，属可接受的简化。

---

## 3. 工具清单与质量

### 3.1 注册的工具（`Plugin.cs:31-44`，共 14 个）

| Tool | 线程 | 参数 | 结果上限 | 备注 |
|---|---|---|---|---|
| `get_ui_hierarchy` | 主线程 | canvasName,maxDepth(≤10) | 深度钳制 1-10 | 限深遍历 |
| `inspect_element` | 主线程 | path/instanceId | — | 精确组件属性 |
| `search_elements` | 主线程 | query,activeOnly,maxResults(1-100) | clamp 1-100（默认25） | 结果截断+`totalMatches` |
| `execute_code` | 主线程 | code(必填) | 单结果 8000 字符截断（`MonoCSharpEvaluator.cs:28`） | **最高风险**，任意 C# 执行 |
| `envsync_debug` | 主线程 | action(必填),weatherCode,temperature,weatherText,currentDateTime,sunrise,sunset | — | 反射桥接 WeatherDebugControl |
| `get_console_logs` | 非主线程 | count(默认50) | 上限由 `MaxLogs=500`（环形缓冲） | 唯一非 UI 工具 |
| `take_screenshot` | 主线程 | width,height(钳制16-4096) | 输出 PNG base64，**无字节上限**（见 §5） | 4096² base64 可达数 MB |
| `get_mouse_element` | 主线程 | — | — | 命中栈 |
| `inspect_screen_point` | 主线程 | x,y,maxResults(钳制1-20) | clamp 20 | 截图坐标→UI |
| `compare_ui_elements` | 主线程 | instanceIdA/B | — | 对齐/间距计算 |
| `preview_ui_layout` | 主线程 | instanceId,referenceInstanceId,placement,gap,等 | previewId 可回滚 | 内存级临时修改 |
| `rollback_ui_preview` | 主线程 | previewId(可空) | — | 回滚 |
| `search_ui_sprites` | 主线程 | query,activeOnly,maxResults(1-100) | clamp 1-100（默认25） | Sprite 复用 |
| `list_scenes` | 主线程 | — | — | 场景清单 |

### 3.2 参数定义质量

- `envsync_debug.InputSchema` 定义最完整：`action` 带 `enum`、`required:["action"]`、其余字段带 description（`EnvSyncDebugTool.cs:24-68`）。但 schema **未声明 `weatherCode/temperature` 为 apply 必填**，实际 `ExecuteOnMain` 里 apply 缺参才 `TextError` 兜底——schema 与运行时契约不一致，客户端可能提前省略导致仅在运行时失败。
- `execute_code` 的 SQL 是任意 C# 无沙箱（描述声明支持 `System.Linq/UnityEngine/Assembly-CSharp`）。
- `get_console_logs`/`take_screenshot` 缺 description 的 range 说明与运行时钳制一致（`take_screenshot` 有 16-4096 钳制但 description 只写「max: 4096」）。

### 3.3 错误处理质量

- 工具内部抛异常：`ExecuteTool` 不 catch，`McpJsonRpcHandler.ProcessToolCall` 的 try-catch（`244-264`）把 `Exception` 转成 `isError=true` 的 text content，HTTP 仍 200 —— 这是 MCP 语义，合理。
- `envsync_debug` 把业务失败包装成 `{"success":false,"error":...}` text，`isError=true`（`250-267`），且反射方法不存在报中文错误——可用但**错误信息暴露反射内部结构**（方法名）。
- **异常泄露到 handler**：`DecodeBody` 现在统一转 `InvalidDataException` → 400（`StreamableHttpProtocol.cs:394-404`）。但 `ExecuteTool` 里 `MainThreadDispatcher.Execute` 抛 `TimeoutException` 会进 `ProcessToolCall` catch → 作为 isError 文本返回——可接受。

### 3.4 安全性

**已做（防线）**：
- **仅监听 loopback**：`TcpListener(IPAddress.Loopback, _port)`（`McpServer.cs:45`）。
- **Host 校验**：白名单 `localhost:port` / `127.0.0.1:port`（`StreamableHttpProtocol.cs:177-192`）；`Host` 缺失 → 400（`88-93`）。
- **Origin 拒绝**：任何 `Origin` 头 → 403（`95`），阻断浏览器 CORS 盗窃。
- **Body 大小上限**：`MaxRequestBytes = 1MiB`（`McpServer.cs:19`），读取循环扩容到上限即抛 `InvalidDataException`（`250-253`）。
- **UTF-8 严格校验**：`StrictUtf8 = new UTF8Encoding(false, true)`（`226`），非法正文 → 400（修复提交核心）。

**未做（风险）**：
- **无鉴权**：README 明确「服务没有鉴权」；Host/Origin 校验只挡浏览器借 localhost，不挡本机恶意进程直接 `127.0.0.1:8972` 发请求。`execute_code` 是**任意代码执行 + envsync_debug 可改游戏状态**，任何能读 loopback 的本机进程 = 完全控制游戏进程。README 也警示「不要端口转发/反向代理」（`.mcp.json` 仅 localhost）。
- **端口固定**：默认 8972，端口冲突或恶意进程抢占同端口可 DoS 服务（loopback 上无 SO_REUSEADDR 冲突检测）。

---

## 4. 测试质量（`UnityExplorerMcp.Tests/StreamableHttpProtocolTests.cs`）

文件实际 694 行（提交 `34f0b93` 时 527 行）。覆盖清单：

### 已覆盖
1. **协议发现**：`server/discover` 仅声明当前版本 + serverInfo（`18-40`）；老版本被拒（`200-216`）。
2. **tools/list**：字符串 id 保留 + 排序稳定（`42-62`）。
3. **tools/call**：Name 头校验（`64-81`）、base64 中文 Name（`83-95`）、失败 isError（`97-115`）。
4. **通知 vs 响应**：notification→202 空体（`117-131`）；客户端响应被拒（`133-146`）。
5. **initialize 非当前协议**：404 -32601（`148-158`）；旧初始化无当前元数据→400 -32020（`160-173`）。
6. **HTTP 层**：非法 JSON→400 -32700（`175-187`）；Accept 协商（`189-197`）；404 未知方法（`281-290`）；Origin 403（`293-300`）；GET 405（`302-313`）；根路径 404（`315-323`）。
7. **头镜像**：缺版本头（`218-229`）、缺/错 Mcp-Method/Name（`231-250`）、版本头-正文不一致（`252-263`）、缺 capabilities（`265-278`）。
8. **Wire 解析**：重复 Accept 合并 + UTF-8 正文（`325-347`）；chunked 解码（`349-373`）；CL+TE 混用拒绝（`375-387`）；非法 UTF-8→InvalidData（`389-412`）；411 无 body framing（`414-421`）；深嵌套 JSON（`423-438`）；超大指数（`440-453`）；null id（`455-470`）；缺 Host 400（`499-507`）；超大 Content-Length 防溢出（`509-525`）；小读取分块解码（`527-548`）；202 防御头（`550-562`）。

### 缺口（按重要度）
1. **无并发/多连接测试**：不测两个 TcpClient 同时请求、`McpServer.HandleClient` 的线程安全、主线程阻塞期间第二个请求行为。
2. **无 IPv6 测试**：不测 `[::1]:8972` 之类。虽然只绑 Loopback(v4)，但规范里 loopback 可能含 v6。
3. **无 malformed JSON-RPC 结构性测试**：不测 `jsonrpc` 缺省/错值、`method` 非 string、`params` 非 object、数组（batch）输入。`Process` 的 `jsonrpc!="2.0"`→-32600 分支无直接测试。
4. **无主线程 marshal 行为测试**：`MainThreadDispatcher` 依赖 Unity `SynchronizationContext`，测试项目不引用 Unity，故 `MainThreadTool` 执行路径（ExecuteTool 的 marshal 分支）**完全未覆盖**。`envsync_debug` 反射行为在测试项目不可测。
5. **无工具层测试**：`ToolRegistry.GetTool` 未知名、`ToolArgumentReader` 的 int/float 转换边界（如 double 非整、超 range）。
6. **无缓冲区/DoS 边界测试**：不测恰好 1MiB、超大 chunk size（`TryDecodeChunkedBody` 的 `int.TryParse` 溢出→非法已测，但接近上限的 alloc 未测）。
7. **无宿主线程生命周期测试**：不测 `Stop()` 后 accept 线程退出、重复 `Start()`。
8. `execute_code`/`take_screenshot` 的响应大小无断言（screenshot base64 可能爆量）。

---

## 5. 代码级问题清单

### 高

| # | 文件:行 | 问题 | 建议 |
|---|---|---|---|
| H1 | `McpServer.cs:78-79` | **线程池无上限 + 无并发连接数限制**。每连接 `QueueUserWorkItem`，攻击者可开上千连接占满线程池，配合 `ReadRequest` 阻塞读（`ReceiveTimeout=15s`）让线程全部挂起 → 服务不可用。Loopback 下这是本机 DoS / 意外 overload。 | 用 `SemaphoreSlim` 限制并发处理数（如 16）；`AcceptTcpClient` 后若信号量满则直接 `client.Close()`。 |
| H2 | `UNITYExplorerMcp/Core/MonoCSharpEvaluator.cs`（经 `ExecuteCodeTool.cs:38`） | **任意 C# 无沙箱 + 可访问文件/网络**。`execute_code` 是 RCE 入口。README 警示 Host/Origin 仅挡浏览器，但**任何本机进程可直连发 `execute_code`**。无鉴权时这是本机权限等效漏洞。 | 至少加 token 鉴权（server 生成、`.mcp.json` 配置），并把 `execute_code` 标记为高危工具建议客户端禁用；或按需裁剪。 |
| H3 | `StreamableHttpProtocol.cs:105-117` | `GET`/未知方法返回 `405` 但**不支持 `initialize`/`DELETE`/`HEAD/OPTIONS`**。当前客户端只 POST，但 POST 到未知方法（如 `DELETE`）也回 405 而非 404，语义略歧义。 | 可接受，但建议对 `Options`/`HEAD` 明确，避免标准客户端探测混淆。 |
| H4 | `McpJsonRpcHandler.cs:167-172` | 未知方法回 HTTP **404** + JSON-RPC `-32601`。HTTP 404 通常表示「资源不存在」，但 `-32601` 是「方法不存在」——两者语义矛盾；且测试 `17-40`/`160-173` 把「方法路由失败」与「HTTP 路径错误」都归一为 404，客户端难区分 `GET /bad`（路径错）和合法 POST 但方法未知（RPC 错）。 | 方法未知应统一回 HTTP 200 + `-32601` error（JSON-RPC Over HTTP 惯例），仅路径错才 404。 |

### 中

| # | 文件:行 | 问题 | 建议 |
|---|---|---|---|
| M1 | `JsonHelper.cs` `ParseNumber`（`291-319`） | 数字词法用字符扫描 `-0123456789.eE+`，**允许 `1..2`、`+-`、`1e+` 等非规范形式**后交给 `TryParse` 判定。`e`/`E`/`.`/`+` 混入虽最终 TryParse 失败会报 Parse error，但对畸形输入仍消耗 O(n) 扫描；且 `ParseNumber` 对 `Infinity/NaN`（`0x/…`）不处理。 | 严格按 JSON 数字文法推进（可复用大小写），或用 `JsonRequiredToken` 手写状态机。当前 `ParseError` 兜底保证不崩溃，风险为中。 |
| M2 | `JsonHelper.cs` `ParseString`（`200-248`） | 未处理 **代理对**：`\uD83D\uDE00` 两个 `\u` 各自 `Append((char)codePoint)`，若孤立高位/低位代理会产出非法 UTF-16 而序列化时又直接原样输出。功能上少见但属于解析健壮性缺口。 | 不进主流程；至少对 `\u` 代理对合并；或改用 `System.Text.Json`（net9 测试可用，但运行时 .NET 4.7.2 需 NuGet）。 |
| M3 | `McpStreamableHttpRequestValidator.cs:75-88` | 支持版本集只有当前版本；`-32022` 返回值的数据体是 `new{}` 匿名对象经 `JsonHelper.Serialize` 反射序列化。**匿名对象反射序列化**走 `SerializeObject`（`JsonHelper.cs:63-101`），只在运行时反射公共属性，性能与结构稳定性一般。 | 序列化路径对已知结构改为显式字典/手写拼接，减少反射。 |
| M4 | `StreamableHttpProtocol.cs:154-157` | 错误响应 `ReasonPhrase` 覆盖不全（`400/403/404/405/406/411/415/200/202`），若未来新状态码漏加走 `default:"Error"`（`173`），HTTP 头会出现 `413 Error` 这种奇怪短语。 | 加默认值并统一枚举→短语映射。 |
| M5 | `McpJsonRpcHandler.cs` 错误码段 | `-32020`/`-32022` 落在 JSON-RPC `-32000..-32099` 保留「server error」段，可能与标准客户端保留码冲突；且自定义码未文档化。 | 文档化这些码，或改用 `< -40000` 私有段避免冲突。 |
| M6 | `TakeScreenshotTool.cs`（`70-83`） | **4096×4096 PNG 的 base64 可到数 MB**，整包写入 `McpHttpResponse.Body`（`string`）再 `Encoding.UTF8.GetBytes`，无响应上限。一次大截图可让 `WriteResponse` 分配大内存字节数组并在 loopback 慢速写时占线程。 | 对截图像素/字节上限设更保守钳制（如 1024²），或对响应 JSON 序列化前设总字节上限。 |

### 低

| # | 文件:行 | 问题 | 建议 |
|---|---|---|---|
| L1 | `McpJsonRpcHandler.cs:133-136` 与 `validator` | 通知（无 `id`）在 validator 里**也会要求 `Mcp-Method` 头镜像**，且无方法时如何走——`Process` 里无 `id` 且 `params` 合法 → 明确 202。但若客户端发**带方法负载的通知**且 headered 一致，validator 的 `RequiresNameHeader` 对通知方法（如 `notifications/…`）不会要求 Name 头，行为可。风险低但仍值得注释。 | 补充注释说明通知的 headered 约定。 |
| L2 | `McpServer.cs:50-55` `OnDestroy` | 注释「server keeps running」：插件实例销毁后后台线程和服务器**继续存活**（未订阅 BepInEx `OnDestroy` 停止）。若 BepInEx 卸载插件，端口仍被占、线程泄漏。这是有意为之（README 已知限制）但**生命周期不干净**。 | 参考：提供显式停止路径，或在 `OnUnloader`/插件卸载事件停止。 |
| L3 | `StreamableHttpProtocol.cs:563-594` `ParseHeaders` | 用 `Split` + `IndexOf(':')`，未处理 **obs-fold（续行头）**、`Host` 重复（合并成逗号分隔但 Host 应该单值）、头行以空格开头等 HTTP 边界；对畸形头抛 `InvalidDataException` → 400，安全但偏宽松。 | 可接受（本机场景）；如要严格可限制头行数/长度。 |
| L4 | `GetConsoleLogsTool.cs:29-37` 与 `ToolArgumentReader` | `count` 解析：`cv is long` 直接 `(int)l` 可能溢出为负 → `GetRecent` 里 `count<=0` 会返回空（`ConsoleCaptureService.cs:48`）。行为可但健壮性一般。 | 钳制 count 到 [1,500]。 |
| L5 | `MainThreadDispatcher.cs:45-46` | `mre.Wait(timeoutMs)` 超时抛 `TimeoutException`；若 `_mainContext==null`（`Initialize` 未跑）则 `_mainContext.Post` 抛 `NullReferenceException` 逃逸。`Execute` 未防御 `_mainContext==null`。 | `Execute` 判空回退到直接执行或报错。 |

---

## 6. 落地优化建议清单（结合真实场景：AI 连上游戏内 MCP 调试天气/彩蛋）

> 结合目标使用场景：AI 客户端在本地连 `http://localhost:8972/mcp`，用 `envsync_debug` apply 模拟天气/时钟场景、比对期望与实际，配合 `take_screenshot`/`search_elements` 验证 UI，`execute_code` 跑查询。

### 协议层
| 建议 | 收益 | 成本 |
|---|---|---|
| **A1. 增加并发连接信号量上限（H1）** | 防本机进程/异常客户端把 MCP 线程池打满，工具延迟可预测 | 低，`SemaphoreSlim` 数行 |
| **A2. 方法未知改回 HTTP 200 + `-32601`（H4）** | 客户端能从 RPC 错误码正确区分「方法不存在」而非「HTTP 资源不存在」 | 低，改 2 处返回 + 测试 |
| **A3. 补 `Mcp-Session-Id` 或明确 stateless 契约** | 若未来接入标准 MCP SDK 客户端，握手才能通；或至少文档化 stateless 让客户端明确无 session | 中，若只是文档化则低 |
| **A4. 增加请求级总响应字节上限** | 防 `take_screenshot` 大响应造成内存尖峰 | 低，`WriteResponse` 前校验 body 长度 |

### 工具层
| 建议 | 收益 | 成本 |
|---|---|---|
| **B1. `envsync_debug` schema 补全 apply 必填**（`required` 语义化） | AI 客户端在调用前就能校验参数，减少运行时失败 | 低，改 schema + 描述 |
| **B2. `execute_code` 加显式「高危」标识 + 可选禁用开关** | 减少误用/风险；README/config 提供 `EnableExecuteCode=false` | 低，Plugin 加 bool 配置 |
| **B3. `take_screenshot` 默认钳制到较小尺寸（如 1024²）** | 响应体量可控，AI 传输更快 | 低 |
| **B4. 为查询类工具返回**`totalMatches`统一约定**（如 `search_elements` 已有） | AI 能判断结果是否被截断，避免误判 | 低 |

### 安全性
| 建议 | 收益 | 成本 |
|---|---|---|
| **C1.（最高优先）加鉴权 token**：启动生成随机 token 写入 `MCPConfig`/`.mcp.json`，客户端请求头带 `Authorization` | 把「任何本机进程可 RCE」降为「持有 token 者才可 RCE」；配合 Host/Origin 校验形成纵深 | 中；需改 `.mcp.json` + `README` + 校验逻辑 |
| **C2. 将 `execute_code` 移到独立可选子端点或可禁用** | 默认最小权限，仅按需开启 | 低-中 |
| **C3. 记录每次工具调用的审计日志（含来源 ip/Host/source）** | 安全事故可追溯 | 低，`_logger` 已有 |

### 可观测性
| 建议 | 收益 | 成本 |
|---|---|---|
| **D1. 增加每个请求的耗时/状态码/工具名统计日志** | 主线程 marshal 超时瓶颈可量化（当前只有工具错误日志） | 低 |
| **D2. 对 `MainThreadDispatcher.Execute` 超时打带工具名的 warning** | 定位「主线程忙导致工具超时」的振荡 | 低 |
| **D3. 暴露 `/healthz` 或 `server/info` 简单存活端点** | 运维/调试快速确认服务在跑 | 低 |

### 建议优先级组合
- **首期（最小改动、最多安全+稳定性收益）**：H1（并发上限）+ H4（错误码语义）+ C1（token 鉴权）+ B3（截图钳制）+ D1/D2（耗时日志）。
- **二期**：A3（session/标准兼容）、B2（execute_code 开关）、A4（响应上限）。
- **评估后可选**：M2（JSON 代理对）、严格 HTTP 头解析。

---

## 附：迁移与修复提交要点回顾

- **`34f0b93`**：把原「自定义 HTTP + 单连接 SSE」缩身为标准 Streamable HTTP；拆出三个可测类；`McpServer` 收窄到 TCP 宿主；新增 14 工具与 `envsync_debug` 反射桥；工具列表按名排序稳定。
- **`6d88544`**：修复 `StrictUtf8` 的 `DecoderFallbackException`（继承 `ArgumentException`）从 `catch(InvalidDataException)` 漏出导致连接被断、无 400 返回的问题——在 `McpHttpWire` 边界统一转 `InvalidDataException`，并改测试契约断言「异常类型=400 承诺」。改动小而精准，修复到位。
