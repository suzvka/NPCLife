using System.Collections.Generic;

namespace NPCLife.Framework.Llm
{
    /// <summary>
    /// Transcript 结构验证器。
    /// 在每轮 LLM 调用前运行，确保消息历史符合 API 约定：
    /// <list type="bullet">
    ///   <item>system 消息只出现在位置 0</item>
    ///   <item>assistant 消息不得连续出现（同一 turn 只允许一条 assistant）</item>
    ///   <item>assistant 声明的 tool_calls 必须有完整对应的 tool 结果消息</item>
    ///   <item>tool 消息的 ToolCallId 必须来自紧邻的前一条 assistant</item>
    ///   <item>transcript 不得以未完成的 tool 结果结尾</item>
    /// </list>
    /// </summary>
    public static class TranscriptValidator
    {
        /// <summary>验证结果。</summary>
        public readonly struct ValidationResult
        {
            public bool IsValid { get; }
            public string Reason { get; }

            private ValidationResult(bool isValid, string reason)
            {
                IsValid = isValid;
                Reason = reason;
            }

            public static ValidationResult Ok() => new ValidationResult(true, null);
            public static ValidationResult Fail(string reason) => new ValidationResult(false, reason);
        }

        /// <summary>
        /// 验证消息列表的结构完整性。
        /// </summary>
        public static ValidationResult Validate(IReadOnlyList<LlmMessage> messages)
        {
            if (messages == null || messages.Count == 0)
                return ValidationResult.Fail("Transcript is empty");

            // Rule 1: system 消息只能出现在 index 0
            for (int i = 1; i < messages.Count; i++)
            {
                if (messages[i].Role == "system")
                    return ValidationResult.Fail($"Unexpected system message at index {i}");
            }

            // 扫描 assistant 消息并校验 tool_calls 配对
            for (int i = 0; i < messages.Count; i++)
            {
                var msg = messages[i];

                if (msg.Role == "assistant")
                {
                    // Rule 2: assistant 不得连续出现
                    if (i + 1 < messages.Count && messages[i + 1].Role == "assistant")
                        return ValidationResult.Fail(
                            $"Consecutive assistant messages at index {i} and {i + 1}");

                    if (msg.ToolCalls != null && msg.ToolCalls.Count > 0)
                    {
                        // Rule 3: 收集紧随其后的 tool 结果，校验 ID 一一对应
                        var expectedIds = new HashSet<string>();
                        foreach (var tc in msg.ToolCalls)
                            expectedIds.Add(tc.Id);

                        var foundIds = new HashSet<string>();
                        int j = i + 1;
                        while (j < messages.Count && messages[j].Role == "tool")
                        {
                            var toolId = messages[j].ToolCallId;
                            if (!expectedIds.Contains(toolId))
                                return ValidationResult.Fail(
                                    $"Orphan tool result '{toolId}' at index {j}");
                            foundIds.Add(toolId);
                            j++;
                        }

                        foreach (var id in expectedIds)
                        {
                            if (!foundIds.Contains(id))
                                return ValidationResult.Fail(
                                    $"Missing tool result for tool_call_id '{id}'");
                        }
                    }
                }
                else if (msg.Role == "tool")
                {
                    // Rule 4: tool 消息必须出现在 assistant (带 tool_calls) 之后
                    // 允许多个连续的 tool 消息（同属一个 assistant turn）
                    if (i == 0)
                        return ValidationResult.Fail(
                            $"Tool message at index {i} not preceded by assistant");
                    
                    // 向前查找最近的非 tool 消息
                    int prevIdx = i - 1;
                    while (prevIdx >= 0 && messages[prevIdx].Role == "tool")
                        prevIdx--;
                    
                    if (prevIdx < 0 || messages[prevIdx].Role != "assistant")
                        return ValidationResult.Fail(
                            $"Tool message at index {i} not preceded by assistant");
                }
            }

            // Rule 5: （已废弃）循环中间状态下 transcript 以 tool result 结尾是正常的，
            // 因为 LLM 尚未回复。仅在最终 transcript 检查时才有意义。
            // if (messages[messages.Count - 1].Role == "tool")
            //     return ValidationResult.Fail("Transcript ends with tool result — assistant response pending");

            return ValidationResult.Ok();
        }
    }
}
