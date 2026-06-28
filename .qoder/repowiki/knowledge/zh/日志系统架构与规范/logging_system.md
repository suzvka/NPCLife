## 1. 核心设计理念
NPCLife 采用**零外部依赖的抽象日志接口**设计。框架内部不绑定任何具体的日志实现（如 Serilog、NLog 或 Unity Debug），而是通过 `ILogger` 接口进行解耦。这种设计确保了库的通用性，允许宿主环境（如 Unity 游戏引擎、ASP.NET Core 服务或控制台应用）注入符合其生态习惯的日志适配器。

## 2. 关键组件与文件
- **`src/NPCLife/Framework/ILogger.cs`**: 定义了统一的日志契约，包含 `Message` (Info), `Warning`, `Error` 三个基础级别。
- **`tests/NPCLife.Tests/Helpers/FakeLogger.cs`**: 测试专用的内存日志记录器，用于在单元测试中捕获并断言日志输出。
- **静态基础设施类**: `AgentPipeline`, `ErrorHandler`, `EventBus`, `LifecycleManager`, `MainThreadDispatcher` 等均包含一个公共静态属性 `public static ILogger Logger`，作为全局日志出口。
- **实例化组件**: `AnthropicAdapter`, `InteractionHistoryStore`, `BuiltInKnowledgeBase` 等通过构造函数注入 `ILogger` 实例。

## 3. 架构模式与约定
### 3.1 混合注入策略
- **静态注入**: 对于纯静态的工具类或管理器（如 `EventBus`），日志器通过静态属性注入。宿主层需在初始化阶段赋值，例如：`EventBus.Logger = new UnityLogger();`。
- **构造注入**: 对于有生命周期的服务类（如 LLM 适配器），日志器通过构造函数参数传入，遵循依赖注入（DI）的最佳实践。

### 3.2 容错与静默失败
所有日志调用均采用空值合并操作符（`Logger?.Warning(...)`）。如果宿主层未注入日志器，框架将静默忽略日志输出，而不会抛出 `NullReferenceException`。这保证了框架在未配置日志时的稳定性。

### 3.3 异常隔离
在 `EventBus` 和 `AgentPipeline` 等涉及回调链的场景中，日志系统被用于记录回调执行过程中的异常。框架会捕获异常并通过 `Logger.Warning` 输出错误信息，确保单个处理器的崩溃不会中断整个事件链或 Agent 循环。

### 3.4 诊断追踪集成
`ErrorHandler` 将日志系统与请求链路追踪（TraceId）结合。在诊断模式（`DiagnosticMode`）开启时，`BeginTrace` 和 `EndTrace` 会通过 `Logger.Message` 输出链路 ID，帮助开发者在复杂的多智能体协作中定位问题。

## 4. 开发者规范
1. **禁止直接使用 Console/Debug**: 框架内部代码严禁直接使用 `Console.WriteLine` 或特定平台的 Debug API，必须通过 `ILogger` 输出。
2. **日志级别选择**:
   - `Message`: 用于正常的生命周期流转（如初始化完成、存档加载）。
   - `Warning`: 用于非致命的异常情况（如工具调用失败、JSON 解析错误、回调异常）。
   - `Error`: 用于导致功能完全失效的严重错误（通常在 `MainThreadDispatcher` 等关键路径中使用）。
3. **上下文标记**: 建议在日志消息前添加模块标签，例如 `[AgentPipeline]` 或 `[Lifecycle]`，以便于在海量日志中进行过滤和检索。
4. **测试验证**: 在编写单元测试时，应使用 `FakeLogger` 注入被测对象，并检查其 `Messages`, `Warnings`, `Errors` 列表以验证逻辑分支的正确性。