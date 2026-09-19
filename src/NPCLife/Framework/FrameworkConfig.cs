using System;
using System.Collections.Generic;
using System.Globalization;

namespace NPCLife.Framework
{
    /// <summary>
    /// 框架全局配置。统一管理诊断开关。
    /// 纯数据类，零外部依赖。
    ///
    /// 合并优先级（低→高）：
    ///   默认值 &lt; 配置文件(FrameworkConfig.FromJson) &lt; 代码覆盖
    ///
    /// 冻结后所有 setter 抛出 InvalidOperationException，保证运行时配置不可变。
    /// </summary>
    public class FrameworkConfig
    {
        private bool _frozen;

        // ---- 子配置区域 ----

        /// <summary>诊断配置区。</summary>
        public DiagnosticSection Diagnostics { get; set; }

        // ---- 冻结机制 ----

        /// <summary>是否已冻结。冻结后所有 setter 抛出 InvalidOperationException。</summary>
        public bool IsFrozen => _frozen;

        /// <summary>
        /// 冻结配置。调用后任何修改尝试将抛出 InvalidOperationException。
        /// 通常在 Initialize() 完成后调用。
        /// </summary>
        public void Freeze()
        {
            _frozen = true;
        }

        private void ThrowIfFrozen()
        {
            if (_frozen) throw new InvalidOperationException("FrameworkConfig is frozen. Cannot modify after Freeze().");
        }

        // ---- 校验 ----

        /// <summary>
        /// 校验配置合法性。返回错误描述列表，空列表表示合法。
        /// </summary>
        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Diagnostics == null) errors.Add("Diagnostics section is null.");
            return errors;
        }

        // ---- 序列化 / 反序列化 ----

        /// <summary>
        /// 序列化为 JSON 字符串。
        /// </summary>
        public string ToJson()
        {
            var w = new JsonWriter(512);

            // Diagnostics
            var diag = new JsonWriter(128);
            diag.Prop("enableVerboseLogging", Diagnostics?.EnableVerboseLogging ?? false);
            diag.Prop("enableToolCallTracing", Diagnostics?.EnableToolCallTracing ?? false);
            diag.Prop("enableEventTracing", Diagnostics?.EnableEventTracing ?? false);
            diag.Prop("logLevel", Diagnostics?.LogLevel ?? "Info");
            w.PropRaw("diagnostics", diag.Close());

            return w.Close();
        }

        /// <summary>
        /// 从 JSON 字符串反序列化。解析失败时返回默认配置。
        /// </summary>
        public static FrameworkConfig FromJson(string json)
        {
            var config = CreateDefault();
            if (string.IsNullOrEmpty(json) || json == "{}") return config;

            try
            {
                var dict = JsonParser.ParseDict(json);

                if (dict.TryGetValue("diagnostics", out string diagJson))
                {
                    var dd = JsonParser.ParseDict(diagJson);
                    if (dd.TryGetValue("enableVerboseLogging", out string v) && bool.TryParse(v, out bool vv))
                        config.Diagnostics.EnableVerboseLogging = vv;
                    if (dd.TryGetValue("enableToolCallTracing", out string t) && bool.TryParse(t, out bool tv))
                        config.Diagnostics.EnableToolCallTracing = tv;
                    if (dd.TryGetValue("enableEventTracing", out string e) && bool.TryParse(e, out bool ev))
                        config.Diagnostics.EnableEventTracing = ev;
                    if (dd.TryGetValue("logLevel", out string ll))
                        config.Diagnostics.LogLevel = ll;
                }
            }
            catch
            {
                // 解析失败，返回默认值
            }

            return config;
        }

        /// <summary>创建默认配置。</summary>
        public static FrameworkConfig CreateDefault()
        {
            return new FrameworkConfig
            {
                Diagnostics = new DiagnosticSection()
            };
        }
    }

    /// <summary>
    /// 诊断配置区。控制日志详细程度和链路追踪。
    /// </summary>
    public class DiagnosticSection
    {
        /// <summary>启用详细日志（含 prompt 内容、工具参数等）。</summary>
        public bool EnableVerboseLogging = false;

        /// <summary>启用工具调用追踪（每次调用记录完整参数和结果）。</summary>
        public bool EnableToolCallTracing = false;

        /// <summary>启用事件总线追踪（记录所有事件发布/订阅轨迹）。</summary>
        public bool EnableEventTracing = false;

        /// <summary>日志级别："Debug" / "Info" / "Warning" / "Error"。</summary>
        public string LogLevel = "Info";
    }
}
