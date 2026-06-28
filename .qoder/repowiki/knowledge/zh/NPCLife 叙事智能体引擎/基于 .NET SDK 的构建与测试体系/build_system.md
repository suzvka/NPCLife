该项目采用标准的 .NET SDK (MSBuild) 作为核心构建系统，依赖 `dotnet` CLI 工具链进行编译、打包和测试。目前未发现独立的 CI/CD 配置文件（如 GitHub Actions, GitLab CI）或容器化构建脚本（Dockerfile），构建流程主要依赖本地开发环境或宿主游戏的集成构建过程。

### 1. 构建系统与工具
- **核心工具**：`.NET SDK` / `MSBuild`。
- **解决方案文件**：使用 `NPCLife.slnx`（XML 格式的 Solution 文件）管理项目结构，包含主库 `src/NPCLife/NPCLife.csproj` 和测试项目 `tests/NPCLife.Tests/NPCLife.Tests.csproj`。
- **语言标准**：强制使用 `LangVersion 12.0`，确保使用较新的 C# 特性。

### 2. 多目标框架与兼容性
- **目标框架**：主库同时 targeting `net48` (.NET Framework 4.8) 和 `netstandard2.0`。
    - `net48`：适配传统 Unity 引擎或其他基于 .NET Framework 的游戏宿主环境。
    - `netstandard2.0`：提供广泛的跨平台兼容性，适配现代 .NET Core/.NET 5+ 环境。
- **测试框架**：测试项目仅 targeting `net48`，使用 `xunit` (v2.9.3) 作为测试运行器。

### 3. 打包与发布策略
- **自动打包**：在 `Release` 配置下，`GeneratePackageOnBuild` 设置为 `true`，意味着执行 `dotnet build -c Release` 时会自动生成 `.nupkg` NuGet 包。
- **版本管理**：当前硬编码版本为 `1.0.0` (`<Version>1.0.0</Version>`)，未观察到基于 Git Tag 或 CI 环境变量动态注入版本的机制。
- **优化策略**：Release 模式下启用代码优化 (`Optimize=true`) 并禁用调试符号 (`DebugSymbols=false`)，以减小包体积并提升运行时性能。

### 4. 资源与依赖管理
- **嵌入式资源**：`Prompts/*.txt` 文件被标记为 `EmbeddedResource`，这意味着提示词模板直接编译进程序集，无需外部文件部署，简化了中间件的集成难度。
- **依赖项**：
    - 主库仅依赖 `System.Net.Http` (v4.3.4)，保持极简依赖树，避免与宿主游戏产生依赖冲突。
    - 测试库依赖 `xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`。

### 5. 开发者构建约定
- **编译命令**：
    - 构建主库：`dotnet build src/NPCLife/NPCLife.csproj`
    - 运行测试：`dotnet test tests/NPCLife.Tests/NPCLife.Tests.csproj`
    - 生成 NuGet 包：`dotnet pack src/NPCLife/NPCLife.csproj -c Release`
- **内部可见性**：主库通过 `InternalsVisibleTo` 向 `NPCLife.Tests` 开放内部成员，便于进行白盒测试。

### 6. 缺失的自动化设施
- **无 CI/CD 配置**：仓库根目录缺少 `.github/workflows` 或类似 CI 配置文件，表明目前可能缺乏自动化的持续集成流水线。
- **无容器化支持**：缺少 `Dockerfile` 或 `docker-compose.yml`，不适合独立微服务部署，符合其“嵌入式中间件”的定位。
- **无脚本化构建**：缺少 `Makefile` 或 `build.sh`，所有构建操作需直接调用 `dotnet` 命令。