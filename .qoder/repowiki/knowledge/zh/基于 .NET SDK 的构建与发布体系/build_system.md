该项目采用标准的 **.NET SDK (MSBuild)** 作为核心构建系统，定位为**嵌入式游戏中间件**，因此构建流程高度依赖 `dotnet` CLI 工具链，未引入复杂的 CI/CD 流水线或容器化部署方案。

### 1. 核心构建架构
- **解决方案管理**：使用 `NPCLife.slnx`（XML 格式）组织项目结构，包含主库 `src/NPCLife` 和测试项目 `tests/NPCLife.Tests`。
- **多目标框架支持**：
  - **主库**：同时 targeting `net48` (.NET Framework 4.8) 和 `netstandard2.0`。前者适配传统 Unity 等游戏引擎，后者确保在现代 .NET Core/.NET 5+ 环境下的跨平台兼容性。
  - **测试库**：仅 targeting `net48`，使用 `xunit` (v2.9.3) 运行单元测试。
- **语言标准**：强制使用 `LangVersion 12.0`，以利用最新的 C# 特性。

### 2. 打包与版本策略
- **自动化打包**：在 `Release` 配置下，`GeneratePackageOnBuild` 设为 `true`。执行 `dotnet build -c Release` 时会自动生成 `.nupkg` NuGet 包。
- **版本管理**：当前版本硬编码为 `1.1.0` (`<Version>1.1.0</Version>`)，尚未实现基于 Git Tag 或 CI 环境变量的动态版本注入。
- **资源嵌入**：`Prompts/*.txt` 提示词文件被标记为 `EmbeddedResource`，直接编译进程序集，简化了中间件在宿主游戏中的部署复杂度。

### 3. 开发者构建约定
- **常用命令**：
  - 构建：`dotnet build src/NPCLife/NPCLife.csproj`
  - 测试：`dotnet test tests/NPCLife.Tests/NPCLife.Tests.csproj`
  - 打包：`dotnet pack src/NPCLife/NPCLife.csproj -c Release`
- **内部可见性**：通过 `InternalsVisibleTo` 向测试项目开放内部成员，支持白盒测试。

### 4. 缺失的自动化设施
- **无 CI/CD 配置**：仓库中未发现 `.github/workflows`、`.gitlab-ci.yml` 等配置文件，缺乏自动化的持续集成流水线。
- **无容器化支持**：缺少 `Dockerfile`，符合其非独立微服务、而是作为库集成的定位。
- **无脚本化构建**：缺少 `Makefile` 或 `build.sh`，所有操作需直接调用 `dotnet` 命令。