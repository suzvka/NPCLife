## 1. 核心架构与模式
该中间件采用**纯代码 POCO（Plain Old CLR Object）**作为配置载体，摒弃了传统的 `appsettings.json` 或环境变量自动绑定机制。配置系统基于**手动序列化/反序列化**逻辑，通过自定义的 `JsonWriter` 和 `JsonParser` 实现轻量级的 JSON 交互。

主要设计模式包括：
- **分层配置结构**：顶层 `FrameworkConfig` 聚合了驱动参数 (`DriverConfig`)、诊断开关 (`DiagnosticSection`) 和功能特性 (`FeatureToggleSection`)。
- **冻结机制 (Freeze Pattern)**：`FrameworkConfig` 支持 `Freeze()` 操作，一旦调用，任何修改尝试都会抛出 `InvalidOperationException`，确保运行时配置的不可变性。
- **缓存即真相 (Cache-as-Truth)**：针对提示词 (`PromptConfig`)，采用静态懒加载从 EmbeddedResource 读取默认值，运行时修改仅影响内存中的实例字段。
- **凭证隔离**：LLM 访问凭证 (`LlmCredential`) 与通用配置解耦，通过 `CredentialRegistry` 进行独立管理和持久化。

## 2. 关键配置文件与类
- **`src/NPCLife/Framework/FrameworkConfig.cs`**：全局配置入口，包含校验逻辑 (`Validate`) 和合并优先级说明（默认值 < JSON 文件 < 代码覆盖）。
- **`src/NPCLife/Driver/DriverConfig.cs`**：定义 Agent 的行为阈值（如事件触发数量、重要度）和定时器脉冲间隔。
- **`src/NPCLife/Driver/PromptConfig.cs`**：管理 LLM 的系统提示词（System Prompt）和采样参数（Temperature），支持从资源文件恢复默认值。
- **`src/NPCLife/Framework/Llm/LlmConfig.cs`**：定义 LLM API 的基础连接信息（BaseUrl, ModelName, ProviderType）。
- **`src/NPCLife/Infrastructure/Llm/CredentialRegistry.cs`**：负责多模型凭证的注册、激活顺序管理及通过 `IStorage` 接口进行的持久化。

## 3. 配置加载与持久化流程
1. **初始化**：通过 `CreateDefault()` 生成包含硬编码默认值的配置实例。
2. **加载**：宿主程序通过 `FromJson(string)` 方法将存储的 JSON 字符串解析并覆盖默认值。若解析失败，则静默回退到默认配置。
3. **持久化**：配置对象通过 `ToJson()` 序列化为字符串，由宿主程序决定存储位置（如本地文件或游戏存档）。
4. **凭证管理**：`CredentialRegistry` 在构造时接收初始 JSON，并在每次别名变更时通过回调委托自动触发持久化。

## 4. 开发者规范
- **禁止直接修改冻结配置**：在框架初始化完成后，严禁尝试修改 `FrameworkConfig` 的属性，必须先检查 `IsFrozen` 状态。
- **使用 CreateDefault 工厂方法**：创建新配置实例时，应始终使用各配置类的 `CreateDefault()` 方法，以确保获得最新的硬编码默认值。
- **敏感信息管理**：API Key 等敏感信息不应硬编码在 `LlmConfig` 中，而应通过 `CredentialRegistry` 进行管理，利用其隔离特性提高安全性。
- **扩展配置项**：新增配置字段时，必须同步更新 `ToJson` 和 `FromJson` 中的序列化/反序列化逻辑，并在 `Validate` 方法中添加合法性校验。