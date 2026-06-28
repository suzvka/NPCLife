## 1. 使用的系统与工具
该项目基于 **.NET SDK** (C#) 构建，采用标准的 **NuGet** 包管理器进行第三方库的依赖管理。依赖声明直接嵌入在项目文件 (`.csproj`) 中，使用 `<PackageReference>` 元素。

## 2. 关键文件与包
- **`src/NPCLife/NPCLife.csproj`**: 核心库的依赖清单。
  - `System.Net.Http` (v4.3.4): 用于处理 HTTP 请求，支撑 LLM API 调用（如 OpenAI, Anthropic 适配器）。
- **`tests/NPCLife.Tests/NPCLife.Tests.csproj`**: 测试项目的依赖清单。
  - `xunit` (v2.9.3): 单元测试框架。
  - `xunit.runner.visualstudio` (v3.0.2): 测试运行器。
  - `Microsoft.NET.Test.Sdk` (v17.13.0): .NET 测试 SDK。
- **`NPCLife.slnx`**: 解决方案文件，定义了项目间的引用关系（测试项目引用核心库）。

## 3. 架构与约定
- **极简依赖策略**: 核心库 (`NPCLife`) 极力避免引入重型第三方框架，仅依赖 .NET 基础库 (`System.Net.Http`)。LLM 交互、MCP 协议实现、JSON 处理等核心逻辑均为自研或基于原生 `System.Text.Json` (隐含在 SDK 中) 实现，以确保在游戏引擎（如 Unity/Unreal）中的兼容性和轻量化。
- **多目标框架支持**: 核心库同时 targeting `net48` (.NET Framework 4.8) 和 `netstandard2.0`，以兼容旧版游戏引擎环境和现代 .NET 环境。
- **内部可见性**: 通过 `InternalsVisibleTo` 属性将内部成员暴露给测试项目，避免了为了测试而公开内部 API 的设计妥协。

## 4. 开发者应遵循的规则
- **依赖添加**: 新增第三方库时，需评估其对 `netstandard2.0` 和 `net48` 的兼容性。
- **版本管理**: 目前在 `.csproj` 中硬编码版本号。若项目规模扩大，建议引入 `Directory.Packages.props` 进行集中式版本管理 (Central Package Management)。
- **私有源**: 当前未发现 `nuget.config`，默认使用 nuget.org 公共源。若需引入私有包，需在解决方案根目录配置 `nuget.config`。