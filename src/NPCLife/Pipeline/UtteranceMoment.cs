namespace NPCLife.Pipeline
{
    /// <summary>
    /// 话语时刻类型。管线唯一生成触发源——没有时刻就没有生成。
    /// </summary>
    public enum MomentKind
    {
        /// <summary>玩家与 NPC 打开对话（Tier 2，可预取）。</summary>
        Interaction = 0,

        /// <summary>战斗/情绪/事件叫喊（Tier 1~2）。</summary>
        Bark = 1,

        /// <summary>通知/信件附言（Tier 1）。</summary>
        Letter = 2,

        /// <summary>环境周期脉冲（低频，Tier 0~1）。</summary>
        AmbientPulse = 3,
    }

    /// <summary>
    /// 话语时刻：一次可消费话语的机会。不可变值对象。
    /// 宿主可只实现 <see cref="MomentKind.Interaction"/>，其余时刻类型走环境脉冲降级。
    /// </summary>
    public sealed class UtteranceMoment
    {
        /// <summary>时刻唯一标识（框架或宿主生成，用于审计）。</summary>
        public string MomentId { get; }

        /// <summary>时刻类型。</summary>
        public MomentKind Kind { get; }

        /// <summary>说话 NPC（K_n 视锥的主体）。</summary>
        public string SpeakerId { get; }

        /// <summary>听众（通常为玩家的 PlayerId；V_p 视锥的主体）。</summary>
        public string ListenerId { get; }

        /// <summary>场景标识（粗视锥地理邻近用；缺失时可为空）。</summary>
        public string SceneKey { get; }

        /// <summary>游戏时间。</summary>
        public long GameTick { get; }

        public UtteranceMoment(string momentId, MomentKind kind, string speakerId,
            string listenerId, string sceneKey, long gameTick)
        {
            MomentId = momentId;
            Kind = kind;
            SpeakerId = speakerId;
            ListenerId = listenerId;
            SceneKey = sceneKey;
            GameTick = gameTick;
        }
    }
}
