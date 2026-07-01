using System.Collections.Generic;
using System.Linq;

namespace NPCLife.Cards
{
    /// <summary>
    /// 游戏事件的标准接口。所有具体事件实现必须实现此接口。
    /// 纯 DTO 接口，由宿主注入实现。
    /// </summary>
    public interface IGameEvent
    {
        /// <summary>事件唯一标识。</summary>
        string EventID { get; }

        /// <summary>事件定义名 (例如 "RaidEnemy", "QuestNode")。</summary>
        string DefName { get; }

        /// <summary>重要度。由事件绑定点直接声明，EventPool 直接累加。</summary>
        float Importance { get; }

        /// <summary>涉及的实体引用列表。</summary>
        IReadOnlyList<EventActorRef> Actors { get; }

        /// <summary>松结构扩展参数 (事件特有的数据)。</summary>
        IDictionary<string, string> Payload { get; }
    }

    /// <summary>
    /// IGameEvent 的具体可序列化实现。供事件缓存（KV 存储）和反序列化使用。
    /// 公开类，可在框架各处复用。
    /// </summary>
    public class EventCardData : IGameEvent, IExtensibleCard
    {
        public string EventID { get; set; }
        public string DefName { get; set; }
        public float Importance { get; set; }
        public List<EventActorRef> Actors { get; set; }
        public Dictionary<string, string> Payload { get; set; }
        public Dictionary<string, string> ExtensionFields { get; set; }
        IReadOnlyList<EventActorRef> IGameEvent.Actors => Actors;
        IDictionary<string, string> IGameEvent.Payload => Payload;

        /// <summary>从任意 IGameEvent 深拷贝创建。</summary>
        public static EventCardData From(IGameEvent source)
        {
            if (source == null) return null;
            return new EventCardData
            {
                EventID = source.EventID,
                DefName = source.DefName,
                Importance = source.Importance,
                Actors = source.Actors != null
                    ? source.Actors.Select(a => new EventActorRef { ID = a.ID, Name = a.Name, Role = a.Role, RefType = a.RefType }).ToList()
                    : new List<EventActorRef>(),
                Payload = source.Payload != null
                    ? new Dictionary<string, string>(source.Payload)
                    : new Dictionary<string, string>()
            };
        }
    }

    /// <summary>
    /// 事件涉及的实体引用。
    /// </summary>
    public struct EventActorRef
    {
        /// <summary>实体标识 (ThingID 或 Faction 名)。</summary>
        public string ID;

        /// <summary>显示名称。</summary>
        public string Name;

        /// <summary>角色: "Initiator"/"Target"/"Victim"/"Bystander"。</summary>
        public string Role;

        /// <summary>引用类型: "Pawn"/"Faction"/"Thing"。</summary>
        public string RefType;

        public static EventActorRef Pawn(string id, string name, string role)
        {
            return new EventActorRef
            {
                ID = id ?? "?",
                Name = name ?? "?",
                Role = role ?? "Bystander",
                RefType = "Pawn"
            };
        }

        public static EventActorRef Faction(string name, string role)
        {
            return new EventActorRef
            {
                ID = name ?? "?",
                Name = name ?? "?",
                Role = role ?? "Bystander",
                RefType = "Faction"
            };
        }
    }
}
