using System;
using Sirenix.OdinInspector;
using UnityEngine;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 所有 MCP 原子工具参数的基类
    /// 所有字段用小写驼峰命名法 统一命名规范
    /// </summary>
    [HideLabel]
    [HideReferenceObjectPicker]
    public class YIUIMCPBaseParams
    {
        /// <summary>
        /// 是否改变执行超时时间
        /// </summary>
        [HideInInspector]
        public bool ChangeTimeoutMs = false;

        /// <summary>
        /// 执行超时时间（毫秒），默认 30000ms
        /// </summary>
        [LabelText("执行超时时间（毫秒）")]
        [ShowIf(nameof(ChangeTimeoutMs))]
        [HorizontalGroup("超时")]
        public int timeoutMs = 30000;

        /// <summary>
        /// 执行前延迟（毫秒），默认 0ms
        /// </summary>
        [LabelText("执行前延迟（毫秒）")]
        [ShowIf(nameof(ChangeTimeoutMs))]
        [HorizontalGroup("超时")]
        public int delayBeforeMs = 0;

        /// <summary>
        /// 执行后延迟（毫秒），默认 1000ms
        /// </summary>
        [LabelText("执行后延迟（毫秒）")]
        [ShowIf(nameof(ChangeTimeoutMs))]
        [HorizontalGroup("超时")]
        public int delayAfterMs = 1000;
    }
}