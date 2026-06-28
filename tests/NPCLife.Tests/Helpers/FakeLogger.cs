using System.Collections.Generic;
using NPCLife.Framework;

namespace NPCLife.Tests.Helpers
{
    /// <summary>
    /// 空操作 FakeLogger。记录所有日志到列表，便于断言。
    /// </summary>
    public class FakeLogger : ILogger
    {
        public readonly List<string> Messages = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public readonly List<string> Errors = new List<string>();

        public void Message(string msg) => Messages.Add(msg);
        public void Warning(string msg) => Warnings.Add(msg);
        public void Error(string msg) => Errors.Add(msg);
    }
}
