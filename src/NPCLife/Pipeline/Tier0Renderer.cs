using NPCLife.Framework.Script;
using System.Collections.Generic;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// Tier 0 渲染器接口：0 次 LLM 的模板槽位填充，用作降级兜底与环境语。
    /// 设计底线：任何单点缺失都不得导致"无台词产出"——Tier 0 恒可用。
    /// </summary>
    public interface ITier0Renderer
    {
        IReadOnlyList<ScriptLine> Render(MaterialBag bag);
    }

    /// <summary>
    /// 模板槽位填充的 Tier 0 实现。按时刻类型选一组通用模板，
    /// 以 (说话者, tick) 的确定性哈希挑选变体（无随机、可测、避免同刻穿帮重复）。
    /// 模板均为不含具体事实的泛化语（问候/环境/确认），天然满足"袋外事实禁止"。
    /// </summary>
    public sealed class TemplateTier0Renderer : ITier0Renderer
    {
        // 每类时刻的模板池（{SpeakerName}/{ListenerName} 为槽位）。
        private static readonly Dictionary<MomentKind, string[]> Templates = new Dictionary<MomentKind, string[]>
        {
            [MomentKind.Interaction] = new[]
            {
                "是你啊，最近一切都好吗？",
                "难得见你一面，有话直说吧。",
                "正好，我也有事想跟你聊聊。",
            },
            [MomentKind.Bark] = new[]
            {
                "小心！","坚持住！","别愣着，动手！","有情况！"
            },
            [MomentKind.Letter] = new[]
            {
                "此信亲启，内中有要事相商。","见字如面，望你读罢即复。"
            },
            [MomentKind.AmbientPulse] = new[]
            {
                "今天天气倒是不错。","这里一切安静。","但愿这样的平静能久一点。","闲来无事，挺好。"
            },
        };

        private static readonly string[] Fallback = { "……", "嗯。", "就这样吧。" };

        public IReadOnlyList<ScriptLine> Render(MaterialBag bag)
        {
            string speakerId = bag?.Moment != null ? bag.Moment.SpeakerId : null;
            var kind = bag?.Moment != null ? bag.Moment.Kind : MomentKind.AmbientPulse;
            long tick = bag?.Moment != null ? bag.Moment.GameTick : 0;

            var pool = Templates.TryGetValue(kind, out var p) && p != null && p.Length > 0 ? p : Fallback;
            string text = pool[StableIndex(speakerId, tick, pool.Length)];

            var line = new ScriptLine
            {
                SpeakerId = string.IsNullOrEmpty(speakerId) ? null : speakerId,
                Text = text,
                Type = string.IsNullOrEmpty(speakerId) ? ScriptLineType.Narration : ScriptLineType.Dialogue,
                RelativeTime = 0f
            };
            return new List<ScriptLine> { line };
        }

        /// <summary>确定性哈希（同输入同输出），避免运行间随机导致测试不稳。</summary>
        private static int StableIndex(string speakerId, long tick, int length)
        {
            unchecked
            {
                int h = 17;
                if (!string.IsNullOrEmpty(speakerId))
                    for (int i = 0; i < speakerId.Length; i++) h = h * 31 + speakerId[i];
                h = h * 31 + (int)(tick ^ (tick >> 32));
                int idx = h % length;
                return idx < 0 ? idx + length : idx;
            }
        }
    }
}
