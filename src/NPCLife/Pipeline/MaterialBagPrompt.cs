using System.Collections.Generic;
using NPCLife.Framework;

namespace NPCLife.Pipeline
{
    /// <summary>
    /// 把 <see cref="MaterialBag"/> 机械序列化为写手的 user 消息。
    /// 纯结构化投影，零语义判断：三区材料 + 话语记忆 + 人格 + 锚点。
    /// Persona / Anchors 为空时对应字段省略。
    /// </summary>
    public static class MaterialBagPrompt
    {
        public static string BuildUserMessage(MaterialBag bag)
        {
            var w = new JsonWriter(1024);

            if (bag.Moment != null)
            {
                var mw = new JsonWriter(128);
                var m = bag.Moment;
                mw.Prop("kind", m.Kind.ToString());
                mw.Prop("speaker", m.SpeakerId);
                mw.Prop("listener", m.ListenerId);
                mw.Prop("scene", m.SceneKey);
                mw.Prop("tick", m.GameTick);
                w.PropRaw("moment", mw.Close());
            }

            if (bag.Persona != null)
            {
                var pw = new JsonWriter(128);
                pw.Prop("id", bag.Persona.ID);
                pw.Prop("name", bag.Persona.Name);
                pw.Prop("faction", bag.Persona.FactionLabel);
                pw.Prop("pawnRelation", bag.Persona.PawnRelation);
                w.PropRaw("persona", pw.Close());
            }

            WriteRegion(w, "sharedExperience", bag.SharedExperience);
            WriteRegion(w, "npcOnly", bag.NpcOnly);
            WriteRegion(w, "playerOnly", bag.PlayerOnly);

            if (bag.RecentUtterances != null && bag.RecentUtterances.Count > 0)
            {
                var items = new List<string>(bag.RecentUtterances.Count);
                foreach (var u in bag.RecentUtterances)
                {
                    var uw = new JsonWriter(96);
                    uw.Prop("speaker", u.SpeakerId);
                    uw.Prop("text", u.Text);
                    if (u.ReferencedEventIds != null && u.ReferencedEventIds.Count > 0)
                        uw.Array("refs", u.ReferencedEventIds);
                    items.Add(uw.Close());
                }
                w.ArrayRaw("recentUtterances", items);
            }

            if (bag.Anchors != null && bag.Anchors.Count > 0)
            {
                var items = new List<string>(bag.Anchors.Count);
                foreach (var a in bag.Anchors)
                {
                    var aw = new JsonWriter(96);
                    aw.Prop("term", a.Term);
                    aw.Prop("definition", a.Definition);
                    items.Add(aw.Close());
                }
                w.ArrayRaw("knowledgeAnchors", items);
            }

            if (bag.OpenLoops != null && bag.OpenLoops.Count > 0)
            {
                var items = new List<string>(bag.OpenLoops.Count);
                foreach (var loop in bag.OpenLoops)
                {
                    var raw = loop.Event?.Raw;
                    var lw = new JsonWriter(128);
                    lw.Prop("id", loop.EventId);
                    lw.Prop("defName", raw != null ? raw.DefName : null);
                    lw.Prop("salience", loop.Salience, "F2");
                    lw.Prop("region", loop.Region.ToString());
                    if (raw != null && raw.Payload != null && raw.Payload.Count > 0)
                    {
                        var pw = new JsonWriter(96);
                        foreach (var kv in raw.Payload) pw.Prop(kv.Key, kv.Value);
                        lw.PropRaw("payload", pw.Close());
                    }
                    items.Add(lw.Close());
                }
                w.ArrayRaw("openLoops", items);
            }

            return w.Close();
        }

        private static void WriteRegion(JsonWriter w, string name, IReadOnlyList<MaterialItem> region)
        {
            if (region == null || region.Count == 0) return;
            var items = new List<string>(region.Count);
            foreach (var item in region)
            {
                var raw = item.Event?.Raw;
                var iw = new JsonWriter(192);
                iw.Prop("id", item.EventId);
                iw.Prop("defName", raw != null ? raw.DefName : null);
                iw.Prop("importance", item.Salience, "F2"); // 显著度（判别器给出）
                if (raw != null && raw.Importance > 0f) iw.Prop("weight", raw.Importance, "F1");
                if (item.Event != null && item.Event.ActorIds != null && item.Event.ActorIds.Count > 0)
                    iw.Array("actors", item.Event.ActorIds);
                if (raw != null && raw.Payload != null && raw.Payload.Count > 0)
                {
                    var pw = new JsonWriter(128);
                    foreach (var kv in raw.Payload) pw.Prop(kv.Key, kv.Value);
                    iw.PropRaw("payload", pw.Close());
                }
                items.Add(iw.Close());
            }
            w.ArrayRaw(name, items);
        }
    }
}
