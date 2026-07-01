# NPCLife v1.1 迁移指南

## 1. 概述

本次更新旨在简化技能体系、统一事件路由机制、消除角色专属工具的冗余定义。主要变更包括：

- **工具命名规范化**：导演工具从 `workspace_*` 重命名为 `storyline_*`
- **事件路由统一**：`route_events` 从角色专属技能迁移到系统级技能
- **技能体系精简**：技能总数从 9 个减少到 6 个
- **即兴编剧工具合并**：`ImproviserMcpTools.cs` 删除，即兴编剧改用统一的写作工具集

**影响范围**：
- 提示词中的工具名引用
- 宿主项目中的技能注册代码
- MCP 工具实现方的 HookId 配置

---

## 2. 工具层 Breaking Changes（LLM 调用侧）

### 2.1 工具名重命名

以下导演工具已重命名，提示词中的所有引用需同步更新：

| 旧名 | 新名 |
|------|------|
| `create_workspace` | `create_storyline` |
| `list_workspaces` | `list_storyline` |
| `get_workspace` | `get_storyline` |
| `branch_workspace` | `branch_storyline` |
| `merge_workspaces` | `merge_storylines` |

**迁移示例**：

```diff
- 请使用 create_workspace 创建新的剧情线
+ 请使用 create_storyline 创建新的剧情线
```

### 2.2 工具移除

以下工具已禁用，需从提示词中移除相关指引：

| 工具名 | 原用途 | 替代方案 |
|--------|--------|----------|
| `suspend_workspace` | 挂起剧情线 | 由宿主自行实现（如需） |
| `resume_workspace` | 恢复剧情线 | 由宿主自行实现（如需） |
| `get_current_time` | 获取游戏时间 | 时间信息随事件注入，无需主动查询 |

**迁移示例**：

```diff
- 如需暂停剧情线，调用 suspend_workspace
+ （删除此指引）
```

### 2.3 route_events 迁移到 system skill

**变更说明**：

`route_events` 从三个角色专属技能（`workspace_direction`、`workspace_writing`、`workspace_improviser`）统一迁移到 `system` 技能。所有角色自动可用，无需单独注册。

**签名变更**：

| 参数 | 旧版（导演专属） | 新版（统一） |
|------|-----------------|-------------|
| `sourceWorkspaceId` | 必填 | **移除**（默认从当前工作空间取事件） |
| `targetWorkspaceId` | 必填 | 必填 |
| `eventIds` | 必填 | 必填 |
| `message` | 可选 | 可选 |
| `focusCharacterIds` | 可选 | 可选 |
| `knowledgeTags` | 可选 | 可选 |

**迁移示例**：

```diff
- route_events(sourceWorkspaceId="dir_001", targetWorkspaceId="ws_123", eventIds="evt_1,evt_2")
+ route_events(targetWorkspaceId="ws_123", eventIds="evt_1,evt_2")
```

**提示词调整**：
- 移除对 `sourceWorkspaceId` 参数的说明
- 移除"只有导演能跨空间路由事件"的限制说明
- 所有角色（编剧、即兴编剧）均可使用 `route_events` 将事件推回导演或其他剧情线

### 2.4 finish_round 参数精简

**移除的参数**：

| 参数 | 原因 |
|------|------|
| `outcome` | 本由 `recap` 涵盖，无需单独字段 |
| `triggerEventIds` | 纯溯源字段，LLM 无需关心 |

**变更的参数**：

| 参数 | 旧版 | 新版 |
|------|------|------|
| `directorNote` | 必填 | **可选**（即兴编剧可留空） |

**迁移示例**：

```diff
  finish_round(
    recap="本轮叙事总结",
-   outcome="剧情发展结果",
-   directorNote="给导演的留言",
-   triggerEventIds="evt_1,evt_2"
+   directorNote="给导演的留言"  // 可选，即兴编剧可省略
  )
```

---

## 3. 技能配置 Breaking Changes（宿主注册侧）

### 3.1 技能 ID 重命名

| 旧 ID | 新 ID |
|-------|-------|
| `workspace_direction` | `storyline_direction` |
| `workspace_writing` | `storyline_writing` |

**迁移方式**：

宿主代码中所有引用旧 ID 的地方需更新：

```diff
- skillSlot.Activate("workspace_writing");
+ skillSlot.Activate("storyline_writing");

- if (skillId == "workspace_direction") { ... }
+ if (skillId == "storyline_direction") { ... }
```

### 3.2 技能合并

**`environment_query` 并入 `character_query`**

| 项目 | 旧版 | 新版 |
|------|------|------|
| 技能 ID | `environment_query` | （移除，合并到 `character_query`） |
| 技能名称 | "环境感知" | "角色与环境查询" |
| HookId | `environment_query` | `character_query` |

**迁移方式**：

1. 宿主中的 `EnvironmentMcpProvider` 需合并到 `CharacterMcpProvider`（或共用一个 provider）
2. 统一使用 HookId `character_query`
3. 删除独立的 `environment_query` 技能注册

```diff
- McpSkillRegistry.RegisterFromProvider(new EnvironmentMcpProvider(...));
+ // 环境查询工具合并到 CharacterMcpProvider
+ McpSkillRegistry.RegisterFromProvider(new CharacterMcpProvider(...));
```

### 3.3 技能移除

| 技能 ID | 原用途 | 替代方案 |
|---------|--------|----------|
| `event_query` | 多维事件历史查询 | 从 SkillCatalog 移除 |
| `workspace_improviser` | 即兴编剧专属工具 | 改用 `storyline_writing` |

**迁移方式**：

1. 移除宿主中对 `event_query` 和 `workspace_improviser` 的注册
2. 即兴编剧改用 `storyline_writing` 技能（与编剧共用）

```diff
- McpSkillRegistry.RegisterTool("event_query", ...);
- McpSkillRegistry.RegisterTool("workspace_improviser", ...);
+ // 即兴编剧使用 storyline_writing
+ skillSlot.Activate("storyline_writing");
```

### 3.4 技能总数变更

**9 → 6**

最终技能列表：

| 技能 ID | 名称 | 可用角色 |
|---------|------|----------|
| `colony_overview` | 游戏全局状态 | Director |
| `character_query` | 角色与环境查询 | Director, Screenwriter, Improviser |
| `relationship_query` | 关系网络 | Screenwriter, Improviser |
| `knowledge_management` | 知识管理 | Director, Screenwriter, Improviser |
| `storyline_direction` | 剧情分支管理 | Director |
| `storyline_writing` | 写作工具集 | Screenwriter, Improviser |

**注意**：如有测试断言技能数量，需更新为 6。

---

## 4. 角色权限变更

以下技能新增了对即兴编剧的支持：

| 技能 | 新增角色 |
|------|----------|
| `relationship_query` | +Improviser |
| `knowledge_management` | +Screenwriter, +Improviser |

**迁移方式**：

确认宿主的 MCP 工具实现允许即兴编剧调用关系查询和知识管理：

```diff
  // CharacterMcpProvider / RelationshipMcpProvider / KnowledgeMcpProvider
  // 确保即兴编剧（WorkspaceRole.Improviser）可调用这些工具
```

---

## 5. 迁移检查清单

### 提示词层
- [ ] 工具名引用已更新（`create_workspace` → `create_storyline` 等）
- [ ] `finish_round` 参数说明已更新（移除 `outcome`、`triggerEventIds`；`directorNote` 改为可选）
- [ ] `route_events` 参数说明已更新（移除 `sourceWorkspaceId`）
- [ ] 移除对 `suspend_workspace` / `resume_workspace` / `get_current_time` 的指引

### 技能注册层
- [ ] 技能 ID 引用已更新（`workspace_direction` → `storyline_direction`、`workspace_writing` → `storyline_writing`）
- [ ] `EnvironmentMcpProvider` 已合并到 `CharacterMcpProvider`，HookId 统一为 `character_query`
- [ ] `event_query` 技能注册已移除
- [ ] `workspace_improviser` 技能注册已移除
- [ ] 即兴编剧的工具集已切换到 `storyline_writing`

### 角色权限层
- [ ] `relationship_query` 工具实现允许 Improviser 调用
- [ ] `knowledge_management` 工具实现允许 Screenwriter 和 Improviser 调用

### 测试层
- [ ] 技能数量断言已更新（9 → 6）
- [ ] 技能 ID 断言已更新（移除 `event_query`、`environment_query`、`workspace_improviser`）

---

## 附录：变更文件清单

| 文件 | 变更类型 |
|------|----------|
| `src/NPCLife/Workspace/DirectionMcpTools.cs` | 工具重命名、`route_events` 移除 |
| `src/NPCLife/Workspace/WritingMcpTools.cs` | `route_events` 移除、`finish_round` 参数精简 |
| `src/NPCLife/Workspace/ImproviserMcpTools.cs` | **已删除** |
| `src/NPCLife/Infrastructure/Mcp/SystemMcpProvider.cs` | 新增 `route_events`、禁用 `get_current_time` |
| `src/NPCLife/Workspace/SkillCatalog.cs` | 技能 ID 重命名、技能合并/移除 |
