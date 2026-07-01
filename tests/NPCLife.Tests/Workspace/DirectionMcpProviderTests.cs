using NPCLife.Cards;
using NPCLife.Core;
using NPCLife.Driver;
using NPCLife.Framework;
using NPCLife.Framework.Mcp;
using NPCLife.Tests.Helpers;
using NPCLife.Workspace;
using System;
using System.Collections.Generic;
using Xunit;

namespace NPCLife.Tests.WorkspaceTests
{
    /// <summary>
    /// DirectionMcpProvider 工具测试。覆盖 create_event 及其他导演工具的核心路径。
    /// </summary>
    public class DirectionMcpProviderTests
    {
        private static DriverConfig CreateConfig()
        {
            return new DriverConfig
            {
                DirectorCountThreshold = 3,
                DirectorImportanceThreshold = 10f,
                ScreenwriterCountThreshold = 3,
                ScreenwriterImportanceThreshold = 10f,
                ImproviserCountThreshold = 3,
                ImproviserImportanceThreshold = 10f,
                RecentHistoryCapacity = 20
            };
        }

        private static string Now() => DateTime.UtcNow.ToString("o");

        // ================================================================
        // create_event
        // ================================================================

        [Fact]
        public void CreateEvent_WithValidTarget_AppendsEventToPool()
        {
            var logger = new FakeLogger();
            var store = new FakeAuthorityStore();
            using (var manager = new WorkspaceManager(store, logger, Now, CreateConfig()))
            {
                // 创建一个活跃的编剧工作空间作为目标
                var writerWs = manager.Create("TestLine", WorkspaceRole.Screenwriter);
                var provider = new DirectionMcpProvider(() => manager, logger);

                var result = provider.CreateEvent(
                    targetWorkspaceId: writerWs.Id,
                    defName: "DirectorBeat_TestEvent",
                    description: "A mysterious figure arrives at the colony gates.",
                    importance: 3.0,
                    actorIds: "pawn_001,pawn_002"
                    );

                // 断言返回 JSON
                Assert.Contains("\"success\":true", result);
                Assert.Contains("\"eventId\":\"dir_", result);
                Assert.Contains("\"defName\":\"DirectorBeat_TestEvent\"", result);

                // 断言事件已写入目标工作空间的事件池
                Assert.Equal(1, writerWs.EventPool.PendingCount);
                Assert.Equal(3f, writerWs.EventPool.TotalImportance);

                // 断言事件内容正确（通过序列化往返）
                var drained = writerWs.EventPool.DrainPending();
                Assert.Single(drained);
                var evt = drained[0];
                Assert.StartsWith("dir_", evt.EventID);
                Assert.Equal("DirectorBeat_TestEvent", evt.DefName);
                Assert.Equal(3f, evt.Importance);
                Assert.Equal(2, evt.Actors.Count);
                Assert.True(evt.Payload.ContainsKey("description"));
                Assert.Equal("A mysterious figure arrives at the colony gates.", evt.Payload["description"]);
                Assert.True(evt.Payload.ContainsKey("source"));
                Assert.Equal("director", evt.Payload["source"]);
            }
        }

        [Fact]
        public void CreateEvent_WithMinimalParams_Succeeds()
        {
            var logger = new FakeLogger();
            var store = new FakeAuthorityStore();
            using (var manager = new WorkspaceManager(store, logger, Now, CreateConfig()))
            {
                var writerWs = manager.Create("MinimalLine", WorkspaceRole.Screenwriter);
                var provider = new DirectionMcpProvider(() => manager, logger);

                var result = provider.CreateEvent(
                    targetWorkspaceId: writerWs.Id,
                    defName: "DirectorBeat_Minimal",
                    description: "Something happens.");

                Assert.Contains("\"success\":true", result);

                var drained = writerWs.EventPool.DrainPending();
                Assert.Single(drained);
                var evt = drained[0];
                Assert.Equal(3f, evt.Importance); // 默认重要度
                Assert.Empty(evt.Actors);
            }
        }

        [Fact]
        public void CreateEvent_TargetNotFound_ReturnsError()
        {
            var logger = new FakeLogger();
            var store = new FakeAuthorityStore();
            using (var manager = new WorkspaceManager(store, logger, Now, CreateConfig()))
            {
                var provider = new DirectionMcpProvider(() => manager, logger);

                var result = provider.CreateEvent(
                    targetWorkspaceId: "nonexistent",
                    defName: "DirectorBeat_Ghost",
                    description: "No target.");

                Assert.Contains("\"success\":false", result);
                Assert.Contains("not found", result);
            }
        }

        [Fact]
        public void CreateEvent_TargetNotActive_ReturnsError()
        {
            var logger = new FakeLogger();
            var store = new FakeAuthorityStore();
            using (var manager = new WorkspaceManager(store, logger, Now, CreateConfig()))
            {
                var writerWs = manager.Create("ClosedLine", WorkspaceRole.Screenwriter);
                manager.UpdateStatus(writerWs.Id, WorkspaceStatus.Completed);
                var provider = new DirectionMcpProvider(() => manager, logger);

                var result = provider.CreateEvent(
                    targetWorkspaceId: writerWs.Id,
                    defName: "DirectorBeat_Late",
                    description: "Too late.");

                Assert.Contains("\"success\":false", result);
                Assert.Contains("not Active", result);
            }
        }

        [Fact]
        public void CreateEvent_ManagerUnavailable_ReturnsError()
        {
            var logger = new FakeLogger();
            var provider = new DirectionMcpProvider(() => null, logger);

            var result = provider.CreateEvent(
                targetWorkspaceId: "any",
                defName: "DirectorBeat_NoManager",
                description: "No manager.");

            Assert.Contains("\"success\":false", result);
            Assert.Contains("unavailable", result);
        }

        [Fact]
        public void CreateEvent_MultipleEventsInSameWorkspace()
        {
            var logger = new FakeLogger();
            var store = new FakeAuthorityStore();
            using (var manager = new WorkspaceManager(store, logger, Now, CreateConfig()))
            {
                var writerWs = manager.Create("MultiLine", WorkspaceRole.Screenwriter);
                var provider = new DirectionMcpProvider(() => manager, logger);

                provider.CreateEvent(writerWs.Id, "DirectorBeat_First", "First event.", 2.0);
                provider.CreateEvent(writerWs.Id, "DirectorBeat_Second", "Second event.", 4.0);

                Assert.Equal(2, writerWs.EventPool.PendingCount);
                Assert.Equal(6f, writerWs.EventPool.TotalImportance);

                var drained = writerWs.EventPool.DrainPending();
                Assert.Equal(2, drained.Count);
                Assert.Contains(drained, e => e.DefName == "DirectorBeat_First");
                Assert.Contains(drained, e => e.DefName == "DirectorBeat_Second");
                // 验证事件 ID 不重复
                Assert.NotEqual(drained[0].EventID, drained[1].EventID);
            }
        }

        [Fact]
        public void CreateEvent_ActorRefs_HaveCorrectStructure()
        {
            var logger = new FakeLogger();
            var store = new FakeAuthorityStore();
            using (var manager = new WorkspaceManager(store, logger, Now, CreateConfig()))
            {
                var writerWs = manager.Create("ActorLine", WorkspaceRole.Screenwriter);
                var provider = new DirectionMcpProvider(() => manager, logger);

                provider.CreateEvent(
                    writerWs.Id,
                    "DirectorBeat_ActorTest",
                    "Testing actors.",
                    actorIds: "pawn_hero,pawn_villain");

                var drained = writerWs.EventPool.DrainPending();
                var evt = drained[0];

                Assert.Equal(2, evt.Actors.Count);
                Assert.Equal("pawn_hero", evt.Actors[0].ID);
                Assert.Equal("Pawn", evt.Actors[0].RefType);
                Assert.Equal("Bystander", evt.Actors[0].Role);
                Assert.Equal("pawn_villain", evt.Actors[1].ID);
            }
        }

        // ================================================================
        // Fake IAuthorityStore for testing
        // ================================================================

        private class FakeAuthorityStore : IAuthorityStore
        {
            private readonly Dictionary<string, object> _data = new Dictionary<string, object>();

            public void Store<T>(string key, T value) => _data[key] = value;

            public T Retrieve<T>(string key, T fallback = default)
            {
                if (_data.TryGetValue(key, out var v) && v is T typed)
                    return typed;
                return fallback;
            }

            public bool Contains(string key) => _data.ContainsKey(key);

            public void Remove(string key) => _data.Remove(key);
        }
    }
}
