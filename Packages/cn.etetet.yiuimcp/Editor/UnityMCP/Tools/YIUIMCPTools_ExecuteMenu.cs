using System.Threading.Tasks;
using Sirenix.OdinInspector;
using UnityEditor;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 执行菜单参数
    /// </summary>
    [HideLabel]
    [HideReferenceObjectPicker]
    public class ExecuteMenuParams : YIUIMCPBaseParams
    {
        /// <summary>
        /// 菜单路径（例如："Assets/Refresh"、"Window/General/Console"）
        /// </summary>
        [LabelText("菜单路径")]
        public string menuPath;
    }

    /// <summary>
    /// 执行Unity菜单命令工具
    /// </summary>
    [YIUIMCPTools("ExecuteMenu", "执行Unity菜单命令")]
    public class YIUIMCPTools_ExecuteMenu : YIUIMCPBaseExecutor<ExecuteMenuParams>
    {
        protected override async Task<YIUIMCPResult> Run(ExecuteMenuParams data)
        {
            // 检查菜单路径是否为空
            if (string.IsNullOrWhiteSpace(data.menuPath))
            {
                return YIUIMCPResult.FailureLog("菜单路径不能为空");
            }

            YIUIMCPLog.Log($"正在执行菜单命令: {data.menuPath}");

            // 执行菜单命令
            bool success = EditorApplication.ExecuteMenuItem(data.menuPath);

            await Task.CompletedTask;

            if (success)
            {
                YIUIMCPLog.Log($"菜单命令执行成功: {data.menuPath}");
                return YIUIMCPResult.Success($"成功执行菜单命令: {data.menuPath}");
            }
            else
            {
                return YIUIMCPResult.FailureLog($"菜单命令执行失败: {data.menuPath}（菜单可能不存在或当前不可用）");
            }
        }
    }
}
