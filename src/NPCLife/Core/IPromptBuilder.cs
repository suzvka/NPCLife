using NPCLife.Cards;
using NPCLife.Framework.PromptBlocks;
using NPCLife.Workspace;
using System.Collections.Generic;

namespace NPCLife.Core
{
    /// <summary>
    /// 系统提示词构建器。根据工作空间的激活技能集，动态组装 Agent 的完整 LLM 请求上下文。
    /// 
    /// 框架提供默认实现（DefaultPromptBuilder），通过 PromptBlockRegistry 聚合所有 PromptBlock，
    /// 产出 System Prompt、注入消息和工具定义。
    /// 
    /// 宿主可提供自定义实现以完全控制 prompt 组装策略。
    /// </summary>
    public interface IPromptBuilder
    {
        /// <summary>
        /// 根据工作空间和本轮事件构建完整的 LLM 请求上下文。
        /// 每次 Agent 激活时调用，反映技能激活/反激活后的最新状态。
        /// </summary>
        /// <param name="workspace">当前工作空间。</param>
        /// <param name="events">本轮待处理事件列表。</param>
        PromptBuildResult Build(IWorkspace workspace, IReadOnlyList<IGameEvent> events);
    }
}
