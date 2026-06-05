using System.Threading.Tasks;

namespace YIUIFramework.Editor.MCP
{
    public interface IYIUIMCPExecutor
    {
        Task<YIUIMCPResult> Execute(YIUIMCPBaseParams baseParams);
    }

    public abstract class YIUIMCPBaseExecutor<T> : IYIUIMCPExecutor where T : YIUIMCPBaseParams, new()
    {
        public async Task<YIUIMCPResult> Execute(YIUIMCPBaseParams baseParams)
        {
            return await YIUIMCPExecutor.ExecuteAsync(baseParams, p => Run((T)p));
        }

        protected abstract Task<YIUIMCPResult> Run(T data);
    }
}