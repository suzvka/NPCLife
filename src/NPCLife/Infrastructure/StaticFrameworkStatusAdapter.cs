using NPCLife.Framework;
using System;
using System.Collections.Generic;

namespace NPCLife.Infrastructure
{
    /// <summary>委托到静态 FrameworkStatus 的 IFrameworkStatus 实现。</summary>
    internal class StaticFrameworkStatusAdapter : IFrameworkStatus
    {
        public void RegisterReporter(string componentName, Func<ComponentStatus> reporter)
            => FrameworkStatus.RegisterReporter(componentName, reporter);

        public void RegisterCapability(string capabilityName, bool supported)
            => FrameworkStatus.RegisterCapability(capabilityName, supported);

        public HealthReport HealthCheck() => FrameworkStatus.HealthCheck();

        public void Clear() => FrameworkStatus.Clear();
    }
}
