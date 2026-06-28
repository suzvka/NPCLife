## 1. 核心设计：接口抽象与宿主注入
该项目采用**零外部依赖**的日志设计模式。核心是一个极简的 `ILogger` 接口，定义在 `src/NPCLife/Framework/ILogger.cs` 中。

- **抽象层**：框架内部不直接依赖任何具体的日志库（如 Serilog, NLog 等），而是通过 `ILogger` 接口进行解耦。
- **注入方式**：日志实例由宿主层（游戏引擎或测试框架）在初始化时注入。框架内部的静态管理类（如 `AgentPipeline`, `ErrorHandler`, `EventBus`）通过公开静态属性 `public static ILogger Logger` 接收实例。
- **静默失败**：如果未注入 `Logger`，框架内部的日志调用将静默忽略（通过 `?.` 操作符），确保框架在无日志配置下仍能正常运行。

## 2. 日志级别与语义
`ILogger` 定义了三个核心方法，对应不同的严重等级：

| 方法 | 语义 | 使用场景 |
| :--- | :--- | :--- |
| `Message(string msg)` | 信息 (Info) | 正常流程状态变更，如生命周期初始化、配置就绪、Trace 开始/结束。 |
| `Warning(string msg)` | 警告 (Warn) | 非致命异常、拦截器错误、处理器异常、诊断模式下的详细上下文。 |
| `Error(string msg)` | 错误 (Error) | 致命错误或线程调度异常（如 `MainThreadDispatcher` 中的未捕获异常）。 |

**注意**：该接口未提供 `Debug` 或 `Trace` 级别。调试信息通常通过 `ErrorHandler.DiagnosticMode` 开关控制，或在测试基类 `LogTestBase` 中通过 `ITestOutputHelper` 直接输出。

## 3. 关键组件与日志实践

### 3.1 静态基础设施日志
多个核心静态类持有 `ILogger` 引用，用于记录框架级事件：
- **`ErrorHandler`**：负责全局错误报告。支持 `DiagnosticMode`，启用后会输出包含 `TraceId` 的详细警告信息。它还将错误发布到 `EventBus` (`llm.error`)。
- **`AgentPipeline`**：在 Agent 循环的拦截点（如 `OnBeforePrompt`）发生异常时，记录 `Warning` 并继续执行，防止单个拦截器崩溃导致整个 Agent 循环终止。
- **`LifecycleManager`**：记录组件的初始化、销毁状态。在 `Shutdown` 阶段逆序 Dispose 组件时，若某组件抛出异常，会记录 `Warning` 但继续销毁其余组件。
- **`EventBus`**：在事件处理器抛出异常时记录 `Warning`，实现错误隔离。

### 3.2 依赖注入式日志
在非静态的基础设施类中，`ILogger` 通常通过构造函数注入：
- **LLM 适配器**：`AnthropicAdapter`, `OpenAiAdapter`, `LlmAccessor` 均接收 `ILogger` 用于记录 API 请求/响应细节或网络错误。
- **知识模块**：`BuiltInKnowledgeBase` 和 `KnowledgeMcpProvider` 注入 logger 以跟踪知识检索过程。
- **交互存储**：`InteractionHistoryStore` 使用 logger 记录历史记录的持久化状态。

### 3.3 测试日志支持
在 `tests/NPCLife.Tests/Helpers/LogTestBase.cs` 中，提供了专门用于单元测试的结构化日志基类。
- 它不实现 `ILogger`，而是直接利用 xUnit 的 `ITestOutputHelper`。
- 提供 `Section`, `Log`, `Step`, `Dump` 等方法，用于在复杂的多智能体协作测试中输出“人在回路”可读的结构化日志。

## 4. 开发者规范

1. **禁止直接使用 Console**：框架代码中严禁使用 `Console.WriteLine` 或 `Debug.Log`。必须通过注入的 `ILogger` 或静态类的 `Logger` 属性输出。
2. **异常安全**：在记录日志时，应假设日志实现本身可能抛出异常（尽管极少见）。框架内部通常在 `catch` 块中记录错误，并确保日志调用不会干扰主业务流程。
3. **Trace 关联**：在处理跨模块的异步或长链路任务时，应利用 `ErrorHandler.BeginTrace()` 和 `EndTrace()` 生成 `TraceId`，并在日志消息中手动包含该 ID，以便在诊断模式下追踪完整请求链路。
4. **诊断开关**：对于高频或详细的调试日志，应受 `ErrorHandler.DiagnosticMode` 或类似配置控制，避免在生产环境中产生过多日志噪音。