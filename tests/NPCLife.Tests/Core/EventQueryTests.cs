using System.Collections.Generic;
using NPCLife.Core;
using Xunit;

namespace NPCLife.Tests.Core
{
    /// <summary>
    /// EventQuery 纯逻辑断言测试。
    /// 验证查询对象的默认值和参数组合。
    /// </summary>
    public class EventQueryTests
    {
        [Fact]
        public void All_ReturnsQueryWithAllNullFilters()
        {
            var q = EventQuery.All;

            Assert.Null(q.ActorId);
            Assert.Null(q.MinImportance);
            Assert.Null(q.Limit);
            Assert.Null(q.Offset);
        }

        [Fact]
        public void DefaultConstructor_AllNull()
        {
            var q = new EventQuery();

            Assert.Null(q.ActorId);
            Assert.Null(q.MinImportance);
            Assert.Null(q.Limit);
            Assert.Null(q.Offset);
        }

        [Fact]
        public void CombinedFilters_AllSetCorrectly()
        {
            var q = new EventQuery
            {
                ActorId = "pawn_001",
                MinImportance = 3f,
                Limit = 20,
                Offset = 5
            };

            Assert.Equal("pawn_001", q.ActorId);
            Assert.Equal(3f, q.MinImportance);
            Assert.Equal(20, q.Limit);
            Assert.Equal(5, q.Offset);
        }
    }
}
