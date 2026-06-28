NPCLife 采用了一套轻量级、无外部依赖的配置管理系统，核心设计理念是**纯 POCO（Plain Old CLR Object）数据类**配合**自定义 JSON 序列化/反序列化**。该系统不依赖 .NET 标准的 `Microsoft.Extensions.Configuration` 或 `appsettings.json`，而是通过代码定义的默认值、JSON 字符串加载以及运行时对象修改来实现配置的层级管理。

### 1. 核心架构与组件

配置系统主要由以下几个核心部分组成：

*   **框架全局配置 (`FrameworkConfig`)**：
    *   作为配置的根节点，包含 `DriverConfig`（驱动参数）和 `DiagnosticSection`（诊断开关）。
    *   **不可变性保护**：提供 `Freeze()` 方法，一旦调用，任何修改尝试都会抛出 `InvalidOperationException`，确保运行时配置的安全性和一致性。
    *   **校验机制**：内置 `Validate()` 方法，检查阈值、容量等参数的合法性，防止非法配置导致运行时错误。

*   **驱动配置 (`DriverConfig`)**：
    *   控制 Agent 的行为参数，如事件触发阈值（按角色区分：Director, Screenwriter, Improviser）、定时器脉冲间隔、历史缓冲区容量等。
    *   提供 `GetEffective...` 方法，根据当前工作空间的角色动态获取对应的阈值配置。

*   **LLM 配置与凭证 (`LlmConfig` / `LlmCredential`)**：
    *   `LlmConfig`：定义 LLM API 的基础访问参数（BaseUrl, ApiKey, ModelName, ProviderType）。
    *   `LlmCredential`：纯数据传递对象，用于运行时凭证管理，支持多凭证激活顺序（Fallback 链路）。
    *   **持久化注册表 (`CredentialRegistry`)**：实现 `ICredentialManager` 接口，管理多个 LLM 凭证的 CRUD 操作及激活状态。它通过宿主提供的委托（`persistAction`）将配置序列化为 JSON 并持久化到存储后端（如文件系统或数据库）。

*   **提示词配置 (`PromptConfig`)**：
    *   通过嵌入式资源（Embedded Resource）加载默认的系统提示词（`.txt` 文件）。
    *   采用静态只读属性暴露，确保基座提示词不可被外部随意覆盖，仅允许在此基础上追加指令。

### 2. 配置加载与分层策略

配置的分层与合并遵循以下优先级（从低到高）：
1.  **代码默认值**：各配置类中的字段初始值或 `CreateDefault()` 工厂方法。
2.  **JSON 配置文件**：通过 `FrameworkConfig.FromJson(string json)` 解析外部传入的 JSON 字符串，覆盖默认值。
3.  **运行时代码覆盖**：在配置冻结前，通过代码直接修改属性值。

**序列化实现**：
*   项目使用了自定义的 `JsonWriter` 和 `JsonParser` 进行轻量级 JSON 处理，避免引入庞大的第三方 JSON 库。
*   `FrameworkConfig.ToJson()` 和 `CredentialRegistry.SerializeState()` 负责将配置对象转换为 JSON 字符串。
*   `FrameworkConfig.FromJson()` 和 `CredentialRegistry.DeserializeState()` 负责从 JSON 字符串恢复配置状态，并在解析失败时优雅地回退到默认值。

### 3. 开发者规范

*   **配置不可变性**：在初始化完成后，务必调用 `FrameworkConfig.Freeze()` 以锁定配置，防止运行时意外修改。
*   **凭证管理**：不要硬编码 API Key。应使用 `CredentialRegistry` 管理凭证，并通过宿主环境提供的持久化委托保存配置。
*   **扩展配置**：若需新增配置项，应在相应的 POCO 类中添加字段，并同步更新 `ToJson` 和 `FromJson` 中的序列化/反序列化逻辑。
*   **提示词定制**：禁止直接修改 `PromptConfig` 中的嵌入式资源。应在获取默认提示词后，通过字符串拼接或模板引擎追加自定义指令。