#if UNITY_EDITOR && YIUI
using System;
using Sirenix.OdinInspector;

namespace YIUIFramework.Editor
{
    [Flags]
    [LabelText("YIUIMCP")]
    [YIUIEnumUnityMacro]
    public enum YIUIMCPMacroData : long
    {
        [LabelText("所有")]
        ALL = -1,

        [LabelText("无")]
        NONE = 0,

        [LabelText("Debug")]
        YIUIMCP_DEBUG = 1,

        [LabelText("DEBUG_LOG")]
        YIUIMCP_DEBUG_LOG = 1 << 1,
    }
}
#endif