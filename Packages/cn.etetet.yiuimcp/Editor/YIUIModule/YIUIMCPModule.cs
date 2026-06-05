using Sirenix.OdinInspector;
using Sirenix.OdinInspector.Editor;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// YIUIMCP
    /// </summary>
    #if YIUI
    [YIUIAutoMenu("YIUIMCP", 400000)]
    public class YIUIMcpModule : BaseYIUIToolModule
    #else
    public class YIUIMCPEditorWindow : OdinEditorWindow
    #endif
    {
        #if !YIUI
        [MenuItem("Tools/YIUIMCP")]
        private static void OpenWindow()
        {
            var window = GetWindow<YIUIMCPEditorWindow>();
            window.titleContent = new GUIContent("YIUIMCP");
            window.Show();
        }

        protected override void Initialize()
        {
            base.Initialize();
            YIUIMCPTools = new();
            YIUIMCPTools.Update();
        }
        #else
        public override void Initialize()
        {
            base.Initialize();
            YIUIMCPTools = new();
            YIUIMCPTools.Update();
        }
        #endif

        private bool IsRunning => YIUIMCPServerHelper.IsRunning;
        private bool IsNotRunning => !YIUIMCPServerHelper.IsRunning;

        [Button("文档", 30, Icon = SdfIconType.Link45deg, IconAlignment = IconAlignment.LeftOfText)]
        [PropertyOrder(int.MinValue)]
        public void OpenDocument()
        {
            Application.OpenURL("https://my.feishu.cn/wiki/MgFKwCSujiePvokPw7rcz46ZnSb");
        }

        [PropertyOrder(1)]
        [BoxGroup("服务器状态", centerLabel: true)]
        [HorizontalGroup("服务器状态/Status")]
        [ShowInInspector]
        [DisplayAsString]
        [HideLabel]
        public string ServerStatus => YIUIMCPServerHelper.IsRunning ? "● 运行中" : "○ 已停止";

        [PropertyOrder(2)]
        [BoxGroup("服务器状态", centerLabel: true)]
        [HorizontalGroup("服务器状态/Status")]
        [ShowInInspector]
        [DisplayAsString]
        [ShowIf(nameof(IsRunning))]
        [HideLabel]
        public string ListeningPort => $"端口: {YIUIMCPServerConfig.Port}";

        [PropertyOrder(3)]
        [BoxGroup("服务器状态", centerLabel: true)]
        [HorizontalGroup("服务器状态/Status")]
        [ShowInInspector]
        [DisplayAsString]
        [ShowIf(nameof(IsRunning))]
        [HideLabel]
        public string ServerUrl => $"http://127.0.0.1:{YIUIMCPServerConfig.Port}/";

        [OnInspectorGUI]
        [PropertyOrder(19)]
        private void Space1()
        {
            GUILayout.Space(10);
        }

        [FormerlySerializedAs("AutoStartMode")]
        [PropertyOrder(0)]
        [BoxGroup("控制", centerLabel: true)]
        [HideLabel]
        [EnumToggleButtons]
        [OnValueChanged(nameof(OnAutoStartModeChanged))]
        [ShowInInspector]
        public EYIUIMCPStartMode StartMode = YIUIMCPServerConfig.StartMode; //[InfoBox("完全手动：任何时候都需要手动启动\n首次手动后自动：第一次手动启动后，之后会自动启动\n始终自动：Unity 启动时自动启动（默认）", InfoMessageType.Info)]

        private void OnAutoStartModeChanged()
        {
            YIUIMCPLog.Log($"启动模式已更改为: {StartMode}");
            YIUIMCPServerConfig.StartMode = StartMode;
        }

        private bool _isEditingPort = false;

        [PropertyOrder(5)]
        [BoxGroup("控制", centerLabel: true)]
        [HorizontalGroup("控制/Port")]
        [LabelText("端口")]
        [LabelWidth(50)]
        [ShowInInspector]
        [EnableIf(nameof(_isEditingPort))]
        private int _editPort = YIUIMCPServerConfig.Port;

        [PropertyOrder(5)]
        [BoxGroup("控制", centerLabel: true)]
        [HorizontalGroup("控制/Port", Width = 80)]
        [Button("应用", 25)]
        [GUIColor(0f, 1, 0f)]
        [ShowIf(nameof(_isEditingPort))]
        private void ApplyPort()
        {
            if (_editPort <= 0 || _editPort > 65535)
            {
                YIUIMCPLog.LogError("端口号必须在 1-65535 之间");
                return;
            }

            if (_editPort != YIUIMCPServerConfig.Port)
            {
                YIUIMCPServerConfig.SetPort(_editPort);

                if (IsRunning)
                {
                    RestartServer();
                }
            }

            _isEditingPort = false;
        }

        [PropertyOrder(5)]
        [BoxGroup("控制", centerLabel: true)]
        [HorizontalGroup("控制/Port", Width = 80)]
        [Button("修改", 25)]
        [GUIColor(0.4f, 0.8f, 1f)]
        [HideIf(nameof(_isEditingPort))]
        private void ChangePort()
        {
            _editPort = YIUIMCPServerConfig.Port;
            _isEditingPort = true;
        }

        private bool ShowStartServerButton => IsNotRunning && StartMode != EYIUIMCPStartMode.Close;

        [PropertyOrder(20)]
        [BoxGroup("控制", centerLabel: true)]
        [HorizontalGroup("控制/Buttons")]
        [GUIColor(0.4f, 0.8f, 0.4f)]
        [Button("启动", 40, Icon = SdfIconType.PlayFill, IconAlignment = IconAlignment.LeftOfText)]
        [ShowIf(nameof(ShowStartServerButton))]
        private void StartServer()
        {
            YIUIMCPLog.Log("手动启动服务器");
            YIUIMCPServerHelper.OnStartServer();
            CloseWindow();
        }

        private bool ShowStopServerButton => IsRunning && StartMode != EYIUIMCPStartMode.Close;

        [PropertyOrder(20)]
        [BoxGroup("控制", centerLabel: true)]
        [HorizontalGroup("控制/Buttons")]
        [GUIColor(0.8f, 0.4f, 0.4f)]
        [Button("停止", 40, Icon = SdfIconType.StopFill, IconAlignment = IconAlignment.LeftOfText)]
        [ShowIf(nameof(ShowStopServerButton))]
        private void StopServer()
        {
            YIUIMCPLog.Log("手动停止服务器");
            YIUIMCPServerHelper.OnStopServer();
            CloseWindow();
        }

        private bool ShowRestartServerButton => IsRunning && StartMode != EYIUIMCPStartMode.Close;
        private bool ShowForceRestartServerButton => StartMode != EYIUIMCPStartMode.Close;

        [PropertyOrder(20)]
        [BoxGroup("控制", centerLabel: true)]
        [HorizontalGroup("控制/Buttons")]
        [GUIColor(0.6f, 0.6f, 0.8f)]
        [Button("重启", 40, Icon = SdfIconType.ArrowRepeat, IconAlignment = IconAlignment.LeftOfText)]
        [ShowIf(nameof(ShowRestartServerButton))]
        private void RestartServer()
        {
            YIUIMCPLog.Log("手动重启服务器");
            YIUIMCPServerHelper.OnStopServer();
            YIUIMCPServerHelper.OnStartServer();
            CloseWindow();
        }

        [PropertyOrder(20)]
        [BoxGroup("控制", centerLabel: true)]
        [HorizontalGroup("控制/Buttons")]
        [GUIColor(1f, 0.65f, 0.2f)]
        [Button("强制启动", 40, Icon = SdfIconType.ExclamationTriangleFill, IconAlignment = IconAlignment.LeftOfText)]
        [ShowIf(nameof(ShowForceRestartServerButton))]
        private void ForceRestartServer()
        {
            YIUIMCPLog.Log("手动强制启动服务器");
            YIUIMCPServerHelper.OnForceRestartServer();
            CloseWindow();
        }

        private void CloseWindow()
        {
            #if YIUI
            YIUIAutoTool.CloseWindow();
            #else
            var window = GetWindow<YIUIMCPEditorWindow>();
            window?.Close();
            #endif
        }

        #if YIUIMCP_EXTRA //作用不大的额外工具操作
        [PropertyOrder(30)]
        [BoxGroup("工具", centerLabel: true)]
        [HorizontalGroup("工具/Buttons")]
        [GUIColor(0f, 1f, 0f)]
        [Button("刷新 注册表", 35, Icon = SdfIconType.ArrowClockwise, IconAlignment = IconAlignment.LeftOfText)]
        private void RefreshTools()
        {
            YIUIMCPTools.Update();
            YIUIMCPLog.Log($"刷新 注册表, 共 {YIUIMCPToolsRegistry.FlowCount} 个流程, 共 {YIUIMCPToolsRegistry.ToolCount} 个工具");
        }

        [PropertyOrder(31)]
        [BoxGroup("工具", centerLabel: true)]
        [HorizontalGroup("工具/Buttons")]
        [GUIColor(0.9f, 0.7f, 0.5f)]
        [Button("复制 MCP 配置到剪贴板", 35, Icon = SdfIconType.Clipboard, IconAlignment = IconAlignment.LeftOfText)]
        private void CopyMcpConfig()
        {
            var config = $@"{{
  ""mcpServers"": {{
    ""unity"": {{
      ""url"": ""http://127.0.0.1:{YIUIMCPServer.Port}/""
    }}
  }}
}}";
            EditorGUIUtility.systemCopyBuffer = config;
            YIUIMCPLog.Log("配置已复制到剪贴板");
        }
        #endif

        [OnInspectorGUI]
        [PropertyOrder(39)]
        private void Space2()
        {
            GUILayout.Space(10);
        }

        [PropertyOrder(1101)]
        public YIUIMCPToolsModule YIUIMCPTools;
    }
}