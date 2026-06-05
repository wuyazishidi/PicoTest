using System.Threading.Tasks;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// 编译流程
    /// </summary>
    [YIUIMCPFlow("compile-unity-flow", "流程编译Unity")]
    public class YIUIMCPFlow_Compile : YIUIMCPBaseExecutor<CompileParams>
    {
        protected override async Task<YIUIMCPResult> Run(CompileParams data)
        {
            await Task.CompletedTask;
            return YIUIMCPResult.Success();
        }
    }
}