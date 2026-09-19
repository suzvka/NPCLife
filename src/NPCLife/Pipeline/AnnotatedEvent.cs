using NPCLife.Cards;
using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 采集层的宿主上下文（机械句柄的可选输入源）。
    /// 宿主能提供多少就填多少；缺失项由 <see cref="CaptureLayer"/> 用合成值补齐。
    /// 除 <see cref="Raw"/> 外全部可选——绝大多数 mod 作者不会提供任何语义句柄。
    /// </summary>
    public sealed class EventCaptureContext
    {
        /// <summary>原始游戏事件（必填，事件流的最小摄入对象）。</summary>
        public IGameEvent Raw;

        /// <summary>机械句柄：actor 集合。null/空时由事件 Actors 推导，再缺失用合成值。</summary>
        public IReadOnlyList<string> ActorIds;

        /// <summary>机械句柄：游戏 tick。缺省时传 0（时间序仍由写入顺序保证）。</summary>
        public long GameTick;

        /// <summary>机械句柄：空间定位（地图/区域键）。null 时由 Payload 推导，再缺失用合成值。</summary>
        public string LocationKey;

        /// <summary>机械句柄：mod 程序集 / 触发钩子溯源。null 时用合成值。</summary>
        public string Provenance;

        /// <summary>仅含原始事件的最小上下文（全句柄走推导/合成路径）。</summary>
        public static EventCaptureContext FromRaw(IGameEvent raw)
        {
            return new EventCaptureContext { Raw = raw };
        }
    }

    /// <summary>
    /// 机械标注事件——事件流的最小存储单元。
    /// 不可变：写入事件流后任何组件不得修改；语义在消费时刻现算，不在此固化。
    /// </summary>
    public sealed class AnnotatedEvent
    {
        /// <summary>原始事件（DefName/Importance/Actors/Payload/TTL 数据基线）。</summary>
        public IGameEvent Raw { get; }

        /// <summary>机械句柄：actor 集合（非空，缺失时为合成值）。</summary>
        public IReadOnlyList<string> ActorIds { get; }

        /// <summary>机械句柄：游戏时间。</summary>
        public long GameTick { get; }

        /// <summary>机械句柄：空间定位（非空，缺失时为合成值）。</summary>
        public string LocationKey { get; }

        /// <summary>机械句柄：mod/钩子来源（非空，缺失时为合成值）。</summary>
        public string Provenance { get; }

        /// <summary>内容指纹：DefName + 排序后的 Payload KV。</summary>
        public string Fingerprint { get; }

        public AnnotatedEvent(IGameEvent raw, IReadOnlyList<string> actorIds, long gameTick,
            string locationKey, string provenance, string fingerprint)
        {
            Raw = raw;
            ActorIds = actorIds;
            GameTick = gameTick;
            LocationKey = locationKey;
            Provenance = provenance;
            Fingerprint = fingerprint;
        }

        /// <summary>事件 ID 便捷访问（Raw 为 null 时返回 null）。</summary>
        public string EventId => Raw != null ? Raw.EventID : null;
    }

    /// <summary>
    /// 采集层：在游戏→框架边界把原始事件转化为带机械句柄的规范化事件。
    ///
    /// 铁律：
    /// - 句柄由本层从上下文**机械推导**，不要求 mod 作者提供；
    /// - 任何句柄缺失 → 使用合成值，**永不拒绝摄入**；
    /// - 零 LLM、零语义判断——意义在消费时刻才结账。
    /// </summary>
    public static class CaptureLayer
    {
        /// <summary>DefName 缺失时的合成值。</summary>
        public const string SyntheticDefName = "__synthetic_def__";

        /// <summary>空间定位缺失时的合成值（合成位置互不邻近，地理特征自然失效）。</summary>
        public const string SyntheticLocation = "__synthetic_location__";

        /// <summary>溯源缺失时的合成值。</summary>
        public const string SyntheticProvenance = "__synthetic_provenance__";

        /// <summary>actor 集合缺失时的合成占位（空 actor 会让事件在 actor 索引中不可见，故用占位 ID 保底）。</summary>
        public const string SyntheticActor = "__synthetic_actor__";

        /// <summary>Payload 中可作为空间定位候选的键（按优先级）。</summary>
        private static readonly string[] LocationPayloadKeys = { "mapId", "map", "location" };

        /// <summary>
        /// 把原始事件规范化为标注事件。raw 为 null 时返回 null——空指针不是"句柄缺失"，
        /// 是唯一会被拒绝的输入。
        /// </summary>
        public static AnnotatedEvent Annotate(EventCaptureContext ctx)
        {
            if (ctx == null || ctx.Raw == null) return null;
            var raw = ctx.Raw;

            var actorIds = ResolveActorIds(ctx.ActorIds, raw);
            string locationKey = ResolveLocationKey(ctx.LocationKey, raw);
            string provenance = string.IsNullOrEmpty(ctx.Provenance) ? SyntheticProvenance : ctx.Provenance;

            return new AnnotatedEvent(
                raw,
                actorIds,
                ctx.GameTick,
                locationKey,
                provenance,
                DeriveFingerprint(raw));
        }

        /// <summary>
        /// actor 集合推导：优先上下文提供 → 事件 Actors 引用 → 合成占位。
        /// 结果保证非空（空 actor 集会让事件在 actor 索引中不可见，违背永不阻断）。
        /// </summary>
        private static IReadOnlyList<string> ResolveActorIds(IReadOnlyList<string> provided, IGameEvent raw)
        {
            if (provided != null)
            {
                var cleaned = provided.Where(id => !string.IsNullOrEmpty(id)).ToList();
                if (cleaned.Count > 0) return cleaned;
            }
            if (raw.Actors != null && raw.Actors.Count > 0)
            {
                var fromEvent = raw.Actors
                    .Where(a => !string.IsNullOrEmpty(a.ID))
                    .Select(a => a.ID)
                    .Distinct()
                    .ToList();
                if (fromEvent.Count > 0) return fromEvent;
            }
            return new[] { SyntheticActor };
        }

        /// <summary>
        /// 空间定位推导：优先上下文提供 → Payload 中的地图/位置键 → 合成占位。
        /// </summary>
        private static string ResolveLocationKey(string provided, IGameEvent raw)
        {
            if (!string.IsNullOrEmpty(provided)) return provided;
            if (raw.Payload != null)
            {
                foreach (var key in LocationPayloadKeys)
                {
                    if (raw.Payload.TryGetValue(key, out var value) && !string.IsNullOrEmpty(value))
                        return value;
                }
            }
            return SyntheticLocation;
        }

        /// <summary>
        /// 内容指纹：DefName + 排序后的 Payload KV，用作材料去重与缓存键。
        /// DefName 缺失时用合成值参与指纹。
        /// </summary>
        public static string DeriveFingerprint(IGameEvent evt)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append(evt.DefName ?? SyntheticDefName);
            if (evt.Payload != null && evt.Payload.Count > 0)
            {
                foreach (var kv in evt.Payload.OrderBy(k => k.Key))
                    sb.Append('|').Append(kv.Key).Append('=').Append(kv.Value ?? "");
            }
            return sb.ToString();
        }
    }
}
