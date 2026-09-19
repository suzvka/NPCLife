using NPCLife.Cards;
using NPCLife.Framework;
using System.Collections.Generic;
using System.Text;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 事件流的 JSON-Lines 日志载体（每行一条标注事件）。
    /// 把宿主事件流水转为本格式，<see cref="Replay"/> 即可逐行重放灌入任意 <see cref="IEventStream"/>，
    /// 在回放集上离线验证三区划分的机械正确率——全程零 LLM。
    ///
    /// 仅用 JsonWriter / JsonParser，零外部依赖。
    /// 每行形如：{"eventId":"...","defName":"...","importance":1.2,"gameTick":10,"locationKey":"...","provenance":"...","fingerprint":"...","actors":["a","b"],"payload":{"k":"v"}}
    /// </summary>
    public static class EventStreamLog
    {
        /// <summary>把单条标注事件编码为一行 JSON。</summary>
        public static string ToLine(AnnotatedEvent e)
        {
            var w = new JsonWriter(256);
            var raw = e.Raw;
            w.Prop("eventId", raw != null ? raw.EventID : null);
            w.Prop("defName", raw != null ? raw.DefName : null);
            if (raw != null) w.Prop("importance", raw.Importance, "F4");
            w.Prop("gameTick", e.GameTick);
            w.Prop("locationKey", e.LocationKey);
            w.Prop("provenance", e.Provenance);
            w.Prop("fingerprint", e.Fingerprint);
            w.Array("actors", e.ActorIds);
            if (raw != null && raw.Payload != null && raw.Payload.Count > 0)
            {
                var pw = new JsonWriter(128);
                foreach (var kv in raw.Payload) pw.Prop(kv.Key, kv.Value);
                w.PropRaw("payload", pw.Close());
            }
            return w.Close();
        }

        /// <summary>从一行 JSON 重建标注事件；无法解析时返回 null（重放侧跳过损坏行）。</summary>
        public static AnnotatedEvent FromLine(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return null;
            var d = JsonParser.ParseDict(line);
            if (d.Count == 0) return null;

            d.TryGetValue("eventId", out var eventId);
            d.TryGetValue("defName", out var defName);
            d.TryGetValue("importance", out var importanceRaw);
            d.TryGetValue("gameTick", out var tickRaw);
            d.TryGetValue("locationKey", out var locationKey);
            d.TryGetValue("provenance", out var provenance);
            d.TryGetValue("fingerprint", out var fingerprint);
            d.TryGetValue("actors", out var actorsRaw);
            d.TryGetValue("payload", out var payloadRaw);

            float importance = 0f;
            if (!string.IsNullOrEmpty(importanceRaw))
                float.TryParse(importanceRaw, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out importance);
            long tick = 0;
            if (!string.IsNullOrEmpty(tickRaw)) long.TryParse(tickRaw, out tick);

            var payload = string.IsNullOrEmpty(payloadRaw)
                ? new Dictionary<string, string>()
                : JsonParser.ParseDict(payloadRaw);

            var data = new EventCardData
            {
                EventID = eventId,
                DefName = defName,
                Importance = importance,
                Payload = payload,
                Actors = new List<EventActorRef>()
            };

            var actorIds = string.IsNullOrEmpty(actorsRaw)
                ? new List<string>()
                : JsonParser.ParseStringArray(actorsRaw);

            // 事件流按 actorId 索引即可，actors 引用在此重建为 Pawn（回放不还原 Role）。
            foreach (var id in actorIds)
                data.Actors.Add(EventActorRef.Pawn(id, id, "Bystander"));

            return new AnnotatedEvent(
                data, actorIds, tick,
                string.IsNullOrEmpty(locationKey) ? CaptureLayer.SyntheticLocation : locationKey,
                string.IsNullOrEmpty(provenance) ? CaptureLayer.SyntheticProvenance : provenance,
                string.IsNullOrEmpty(fingerprint) ? CaptureLayer.DeriveFingerprint(data) : fingerprint);
        }

        /// <summary>整个流编码为多行日志（每行一条，\n 分隔）。</summary>
        public static string ToLog(IEventStream stream)
        {
            var sb = new StringBuilder();
            foreach (var e in stream.Query(StreamQuery.All))
            {
                sb.Append(ToLine(e)).Append('\n');
            }
            return sb.ToString();
        }

        /// <summary>
        /// 从多行日志重放灌入目标流。返回成功重放的条数；空/损坏行静默跳过。
        /// </summary>
        public static int Replay(string log, IEventStream target)
        {
            if (string.IsNullOrEmpty(log) || target == null) return 0;
            int count = 0;
            foreach (var line in log.Split('\n'))
            {
                var e = FromLine(line);
                if (e != null && target.TryAppend(e)) count++;
            }
            return count;
        }
    }
}
