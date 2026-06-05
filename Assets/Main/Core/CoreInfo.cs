namespace PicoTest.Core
{
    /// <summary>
    /// 核心层元信息。Main.Core 程序集禁止引用 UnityEngine（noEngineReferences），
    /// 采集调度/序列化/传输协议等纯逻辑都放在这一层，便于无 Unity 快速测试。
    /// </summary>
    public static class CoreInfo
    {
        /// <summary>采集数据 Schema 版本，数据格式变更时必须递增并记录于 Docs/decisions.md。</summary>
        public const int SchemaVersion = 1;

        /// <summary>冒烟测试探针。</summary>
        public static string Ping() => "pong";
    }
}
