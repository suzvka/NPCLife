using System;
using System.Collections.Generic;

namespace NPCLife.Framework
{
    /// <summary>
    /// 框架状态内省接口。将静态 FrameworkStatus 抽象为可注入实例。
    /// </summary>
    public interface IFrameworkStatus
    {
        /// <summary>注册组件状态报告器。</summary>
        void RegisterReporter(string componentName, Func<ComponentStatus> reporter);

        /// <summary>注册能力标识。</summary>
        void RegisterCapability(string capabilityName, bool supported);

        /// <summary>健康检查。</summary>
        HealthReport HealthCheck();

        /// <summary>清除所有报告器和能力注册。</summary>
        void Clear();
    }
}
