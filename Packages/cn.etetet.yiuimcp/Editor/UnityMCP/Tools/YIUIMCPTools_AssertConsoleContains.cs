using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Sirenix.OdinInspector;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 控制台日志断言参数
    /// </summary>
    [HideLabel]
    [HideReferenceObjectPicker]
    public class AssertConsoleContainsParams : YIUIMCPBaseParams
    {
        /// <summary>
        /// 日志类型过滤（默认仅普通日志）
        /// </summary>
        [LabelText("日志类型")]
        public EYIUIConsoleLogType logType = EYIUIConsoleLogType.LogMask;

        /// <summary>
        /// 关键词（单个）
        /// </summary>
        [LabelText("关键词")]
        public string keyword;

        /// <summary>
        /// 关键词数组 JSON（示例: ["A", "B"]）
        /// </summary>
        [LabelText("关键词数组JSON")]
        public string keywordsJson;

        /// <summary>
        /// 是否使用正则匹配
        /// </summary>
        [LabelText("使用正则")]
        public bool useRegex = false;

        /// <summary>
        /// 是否忽略大小写
        /// </summary>
        [LabelText("忽略大小写")]
        public bool ignoreCase = true;

        /// <summary>
        /// 是否要求全部关键词都匹配（默认匹配任意一个即可）
        /// </summary>
        [LabelText("全部关键词匹配")]
        public bool matchAll = false;

        /// <summary>
        /// 只在最新 N 条日志中搜索（默认 200）
        /// </summary>
        [LabelText("尾部日志条数")]
        public int tailCount = 200;

        /// <summary>
        /// 是否移除堆栈信息
        /// </summary>
        [LabelText("移除堆栈")]
        public bool removeStackTrace = true;
    }

    /// <summary>
    /// 断言控制台日志包含关键词
    /// </summary>
    [YIUIMCPTools("AssertConsoleContains", "断言控制台日志包含关键词")]
    public class YIUIMCPTools_AssertConsoleContains : YIUIMCPBaseExecutor<AssertConsoleContainsParams>
    {
        protected override async Task<YIUIMCPResult> Run(AssertConsoleContainsParams data)
        {
            await Task.CompletedTask;

            if (!TryBuildKeywords(data, out var keywords, out var keywordError))
            {
                return YIUIMCPResult.FailureLog(keywordError);
            }

            var logs = YIUIMCPHelper.GetConsoleLogs(data.logType, data.removeStackTrace);
            if (logs.Count == 0)
            {
                return YIUIMCPResult.FailureLog("断言失败: 控制台没有符合过滤条件的日志");
            }

            var scopedLogs = ScopeTailLogs(logs, data.tailCount);
            var matchMap = new Dictionary<string, List<string>>();

            foreach (var keyword in keywords)
            {
                var matchedLines = scopedLogs
                    .Where(line => IsMatched(line, keyword, data.useRegex, data.ignoreCase))
                    .Take(5)
                    .ToList();

                matchMap[keyword] = matchedLines;
            }

            var matchedKeywords = matchMap.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).ToList();
            var success = data.matchAll ? matchedKeywords.Count == keywords.Count : matchedKeywords.Count > 0;

            if (!success)
            {
                var fail = new StringBuilder();
                fail.AppendLine("断言失败: 未命中期望日志关键词");
                fail.AppendLine($"匹配模式: {(data.matchAll ? "ALL" : "ANY")}");
                fail.AppendLine($"关键词: {string.Join(", ", keywords)}");
                fail.AppendLine($"搜索日志数: {scopedLogs.Count}");
                fail.AppendLine("日志样本(尾部最多20条):");

                foreach (var line in scopedLogs.Skip(Math.Max(0, scopedLogs.Count - 20)))
                {
                    fail.AppendLine($"- {line}");
                }

                return YIUIMCPResult.FailureLog(fail.ToString().TrimEnd());
            }

            var pass = new StringBuilder();
            pass.AppendLine("断言通过: 命中期望日志关键词");
            pass.AppendLine($"匹配模式: {(data.matchAll ? "ALL" : "ANY")}");
            pass.AppendLine($"命中关键词: {string.Join(", ", matchedKeywords)}");

            foreach (var key in matchedKeywords)
            {
                foreach (var line in matchMap[key])
                {
                    pass.AppendLine($"[{key}] {line}");
                }
            }

            return YIUIMCPResult.Success(pass.ToString().TrimEnd());
        }

        private static List<string> ScopeTailLogs(List<string> logs, int tailCount)
        {
            if (tailCount <= 0 || logs.Count <= tailCount)
            {
                return logs;
            }

            return logs.Skip(logs.Count - tailCount).ToList();
        }

        private static bool TryBuildKeywords(AssertConsoleContainsParams data, out List<string> keywords, out string error)
        {
            keywords = new List<string>();
            error = string.Empty;

            if (!string.IsNullOrWhiteSpace(data.keyword))
            {
                keywords.Add(data.keyword.Trim());
            }

            if (!string.IsNullOrWhiteSpace(data.keywordsJson))
            {
                try
                {
                    var parsed = JsonConvert.DeserializeObject<List<string>>(data.keywordsJson);
                    if (parsed != null)
                    {
                        foreach (var item in parsed)
                        {
                            if (!string.IsNullOrWhiteSpace(item))
                            {
                                keywords.Add(item.Trim());
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    error = $"参数错误: keywordsJson 解析失败: {e.Message}";
                    return false;
                }
            }

            keywords = keywords.Distinct(StringComparer.Ordinal).ToList();
            if (keywords.Count == 0)
            {
                error = "参数错误: keyword 或 keywordsJson 至少提供一个";
                return false;
            }

            return true;
        }

        private static bool IsMatched(string line, string keyword, bool useRegex, bool ignoreCase)
        {
            if (useRegex)
            {
                var options = ignoreCase ? RegexOptions.IgnoreCase : RegexOptions.None;
                try
                {
                    return Regex.IsMatch(line, keyword, options);
                }
                catch
                {
                    return false;
                }
            }

            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            return line.IndexOf(keyword, comparison) >= 0;
        }
    }
}
