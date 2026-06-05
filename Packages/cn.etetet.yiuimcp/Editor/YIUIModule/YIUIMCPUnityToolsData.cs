using System;
using System.IO;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEngine;

namespace YIUIFramework.Editor.MCP
{
    [HideLabel]
    [HideReferenceObjectPicker]
    public class YIUIMCPUnityToolsData
    {
        [HideInInspector]
        public YIUIMCPToolsRegistry.YIUIMCPToolInfo ToolInfo;

        [TableColumnWidth(200, Resizable = false)]
        [VerticalGroup("信息")]
        [ReadOnly]
        [HideLabel]
        public string Name;

        [VerticalGroup("信息")]
        [HideLabel]
        [ReadOnly]
        public string Description;

        [VerticalGroup("参数")]
        [ShowInInspector]
        private YIUIMCPBaseParams BaseParams;

        public bool IsFlow { get; private set; }

        [VerticalGroup("信息")]
        [HorizontalGroup("信息/按钮")]
        [Button("", 25, Icon = SdfIconType.FolderFill, IconAlignment = IconAlignment.LeftOfText)]
        [ShowIf(nameof(IsFlow))]
        private void OpenFolder()
        {
            var path = GetFlowPath();
            var directoryPath = Directory.GetParent(path);
            Application.OpenURL(directoryPath.FullName);
        }

        [TableColumnWidth(100, Resizable = false)]
        [VerticalGroup("信息")]
        [HorizontalGroup("信息/按钮")]
        [Button("", 25, Icon = SdfIconType.FolderSymlinkFill, IconAlignment = IconAlignment.LeftOfText)]
        [ShowIf(nameof(IsFlow))]
        private void CopyPath()
        {
            var path = GetFlowPath();
            EditorGUIUtility.systemCopyBuffer = path;
            YIUIMCPLog.ShowNotification($"已复制[{Name}]路径");
        }

        private string GetFlowPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, $"../Packages/cn.etetet.yiuimcp/Config/{Name}.ps1"));
        }

        public void ChangeTimeoutMs(bool value)
        {
            BaseParams.ChangeTimeoutMs = value;
        }

        public YIUIMCPUnityToolsData(YIUIMCPToolsRegistry.YIUIMCPToolInfo toolInfo, bool isFlow)
        {
            ToolInfo = toolInfo;
            Name = toolInfo.Name;
            Description = toolInfo.Description;
            IsFlow = isFlow;

            try
            {
                var instance = Activator.CreateInstance(toolInfo.ParamType);
                BaseParams = instance as YIUIMCPBaseParams;
                if (BaseParams == null)
                {
                    YIUIMCPLog.LogError($"{toolInfo.Name}, 参数类型错误: {toolInfo.ParamType.Name} 必须继承 YIUIMCPBaseParams");
                }
            }
            catch (Exception ex)
            {
                YIUIMCPLog.LogError($"{toolInfo.Name}, 创建默认参数失败 {toolInfo.Name}. 预期 {toolInfo.ParamType.Name}. Error: {ex.Message}");
            }
        }

        [VerticalGroup("执行")]
        [TableColumnWidth(50, Resizable = false)]
        [Button("", 30, Icon = SdfIconType.FileCodeFill, IconAlignment = IconAlignment.LeftOfText)]
        [PropertyOrder(999)]
        private void Execute()
        {
            if (IsFlow)
            {
                ExecuteFlow();
            }
            else
            {
                ExecuteTool();
            }

            #if YIUI
            YIUIAutoTool.CloseWindow();
            #else
            var window = EditorWindow.GetWindow<YIUIMCPEditorWindow>();
            if (window != null)
            {
                window.Close();
            }
            #endif
        }

        private void ExecuteFlow()
        {
            try
            {
                var scriptPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, $"../Packages/cn.etetet.yiuimcp/Config/{Name}.ps1");

                var args = BuildFlowArguments(BaseParams);

                YIUIMCPLog.Log($"YIUIMCP-Flow-执行-[{Name}:{Description}] 参数:[{args}]");

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -ExecutionPolicy Bypass -Command \"{scriptPath}\" {args} -NoWait 0",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                YIUIMCPLog.LogError($"执行流程异常: {ex.Message}");
            }
        }

        private string BuildFlowArguments(YIUIMCPBaseParams baseParams)
        {
            var args = new System.Collections.Generic.List<string>();

            // 获取基类的字段名（需要排除）
            var baseFields = typeof(YIUIMCPBaseParams).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var baseFieldNames = new System.Collections.Generic.HashSet<string>();
            foreach (var field in baseFields)
            {
                baseFieldNames.Add(field.Name);
            }

            // 只获取子类特有的字段
            var fields = baseParams.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var field in fields)
            {
                // 跳过基类字段
                if (baseFieldNames.Contains(field.Name))
                    continue;

                var value = field.GetValue(baseParams);
                if (value != null)
                {
                    // 根据类型构造参数
                    if (value is bool boolValue)
                    {
                        // PowerShell 布尔参数：-ParamName:$true 或 -ParamName:$false
                        //args.Add($"-{field.Name}:${(boolValue ? "True" : "False")}");
                        args.Add($"-{field.Name} {(boolValue ? "1" : "0")}");
                    }
                    else if (value is string strValue)
                    {
                        // 字符串参数：-ParamName "value"
                        args.Add($"-{field.Name} \"{strValue}\"");
                    }
                    else if (value is int || value is long || value is float || value is double)
                    {
                        // 数字参数：-ParamName value
                        args.Add($"-{field.Name} {value}");
                    }
                    else
                    {
                        // 其他类型转为字符串
                        args.Add($"-{field.Name} \"{value}\"");
                    }
                }
            }

            return string.Join(" ", args);
        }

        private void ExecuteTool()
        {
            try
            {
                string scriptPath = System.IO.Path.Combine(UnityEngine.Application.dataPath, "../Packages/cn.etetet.yiuimcp/Config/invoke-uto-tool.ps1");

                string paramsJson = SerializeParams(BaseParams);

                YIUIMCPLog.Log($"YIUIMCP-Tool-执行-[{Name}:{Description}] 参数:[{paramsJson}]");

                // Base64 编码，避免转义问题
                byte[] bytes = System.Text.Encoding.UTF8.GetBytes(paramsJson);
                string base64 = System.Convert.ToBase64String(bytes);

                var startInfo = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoExit -ExecutionPolicy Bypass -Command \"{scriptPath}\" -Tool {Name} -ParamsBase64 {base64} -NoWait 0",
                    UseShellExecute = true,
                    CreateNoWindow = false
                };

                System.Diagnostics.Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                YIUIMCPLog.LogError($"执行工具异常: {ex.Message}");
            }
        }

        private string SerializeParams(YIUIMCPBaseParams baseParams)
        {
            var paramsDict = new System.Collections.Generic.Dictionary<string, object>();

            // 获取基类的字段名（需要排除）
            var baseFields = typeof(YIUIMCPBaseParams).GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
            var baseFieldNames = new System.Collections.Generic.HashSet<string>();
            foreach (var field in baseFields)
            {
                baseFieldNames.Add(field.Name);
            }

            // 只获取子类特有的字段
            var fields = baseParams.GetType().GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);

            foreach (var field in fields)
            {
                // 跳过基类字段
                if (baseFieldNames.Contains(field.Name))
                    continue;

                var value = field.GetValue(baseParams);
                if (value != null)
                {
                    paramsDict[field.Name] = value;
                }
            }

            return Newtonsoft.Json.JsonConvert.SerializeObject(paramsDict);
        }
    }
}