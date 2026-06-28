using NPCLife.Driver;
using Xunit;

namespace NPCLife.Tests.Driver
{
    /// <summary>
    /// DriverConfig 单元测试。验证配置默认值和阈值查询。
    /// </summary>
    public class DriverConfigTests
    {
        [Fact]
        public void CreateDefault_HasSensibleDefaults()
        {
            var config = DriverConfig.CreateDefault();

            Assert.Equal(5, config.DirectorCountThreshold);
            Assert.Equal(15f, config.DirectorImportanceThreshold);
            Assert.Equal(5, config.ScreenwriterCountThreshold);
            Assert.Equal(15f, config.ScreenwriterImportanceThreshold);
            Assert.Equal(5, config.ImproviserCountThreshold);
            Assert.Equal(15f, config.ImproviserImportanceThreshold);
            Assert.Equal(200, config.RecentHistoryCapacity);
            Assert.Equal(10, config.MaxAgentRounds);
        }

        [Fact]
        public void GetEffectiveImportanceThreshold_ReturnsCorrectRoleValues()
        {
            var config = new DriverConfig
            {
                DirectorImportanceThreshold = 10f,
                ImproviserImportanceThreshold = 20f,
                ScreenwriterImportanceThreshold = 30f
            };

            Assert.Equal(10f, config.GetEffectiveImportanceThreshold(Workspace.WorkspaceRole.Director));
            Assert.Equal(20f, config.GetEffectiveImportanceThreshold(Workspace.WorkspaceRole.Improviser));
            Assert.Equal(30f, config.GetEffectiveImportanceThreshold(Workspace.WorkspaceRole.Screenwriter));
        }

        [Fact]
        public void GetEffectiveCountThreshold_ReturnsCorrectRoleValues()
        {
            var config = new DriverConfig
            {
                DirectorCountThreshold = 3,
                ImproviserCountThreshold = 7,
                ScreenwriterCountThreshold = 10
            };

            Assert.Equal(3, config.GetEffectiveCountThreshold(Workspace.WorkspaceRole.Director));
            Assert.Equal(7, config.GetEffectiveCountThreshold(Workspace.WorkspaceRole.Improviser));
            Assert.Equal(10, config.GetEffectiveCountThreshold(Workspace.WorkspaceRole.Screenwriter));
        }
    }
}
