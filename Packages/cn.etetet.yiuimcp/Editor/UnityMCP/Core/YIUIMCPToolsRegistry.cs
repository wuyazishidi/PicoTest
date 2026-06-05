using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace YIUIFramework.Editor.MCP
{
    public static class YIUIMCPToolsRegistry
    {
        public class YIUIMCPToolInfo
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public IYIUIMCPExecutor Executor;
            public Type ParamType;
        }

        private static readonly Dictionary<string, YIUIMCPToolInfo> _tools = new();

        public static IReadOnlyDictionary<string, YIUIMCPToolInfo> Tools => _tools;

        public static int ToolCount => _tools.Count;

        private static readonly Dictionary<string, YIUIMCPToolInfo> _flows = new();

        public static IReadOnlyDictionary<string, YIUIMCPToolInfo> Flows => _flows;

        public static int FlowCount => _flows.Count;

        private static bool m_Initialized = false;

        public static void Initialize()
        {
            if (m_Initialized)
            {
                return;
            }

            m_Initialized = true;

            _tools.Clear();
            _flows.Clear();

            var assemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in assemblies)
            {
                try
                {
                    if (ShouldSkipAssembly(assembly))
                    {
                        continue;
                    }

                    foreach (var type in assembly.GetTypes())
                    {
                        // 检查 Tool 特性
                        var toolAttr = type.GetCustomAttribute<YIUIMCPToolsAttribute>();
                        if (toolAttr != null)
                        {
                            if (TryCreateToolInfo(type, toolAttr.Name, toolAttr.Description, out var toolInfo))
                            {
                                if (_tools.ContainsKey(toolAttr.Name))
                                {
                                    YIUIMCPLog.LogError($": {toolAttr.Name}. 已存在相同名称的MCP工具 请保证唯一性.");
                                }
                                else
                                {
                                    _tools.Add(toolAttr.Name, toolInfo);
                                }
                            }
                        }

                        // 检查 Flow 特性
                        var flowAttr = type.GetCustomAttribute<YIUIMCPFlowAttribute>();
                        if (flowAttr != null)
                        {
                            if (TryCreateToolInfo(type, flowAttr.Name, flowAttr.Description, out var flowInfo))
                            {
                                if (_flows.ContainsKey(flowAttr.Name))
                                {
                                    YIUIMCPLog.LogError($": {flowAttr.Name}. 已存在相同名称的MCP流程 请保证唯一性.");
                                }
                                else
                                {
                                    _flows.Add(flowAttr.Name, flowInfo);
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    YIUIMCPLog.LogError($"扫描程序集失败 {assembly.FullName}: {e.Message}");
                }
            }
        }

        private static bool ShouldSkipAssembly(Assembly assembly)
        {
            return assembly.FullName.StartsWith("System") ||
                   assembly.FullName.StartsWith("Unity") ||
                   assembly.FullName.StartsWith("Microsoft");
        }

        private static bool TryCreateToolInfo(Type type, string name, string description, out YIUIMCPToolInfo toolInfo)
        {
            toolInfo = null;

            // 验证类型
            if (!typeof(IYIUIMCPExecutor).IsAssignableFrom(type) || type.IsAbstract)
            {
                YIUIMCPLog.LogError($": {type.FullName} 标记了 MCP 特性但未实现 IYIUIMCPExecutor.");
                return false;
            }

            // 创建实例
            IYIUIMCPExecutor executorInstance;
            try
            {
                executorInstance = Activator.CreateInstance(type) as IYIUIMCPExecutor;
            }
            catch (Exception e)
            {
                YIUIMCPLog.LogError($" 创建工具实例失败 {type.FullName}: {e.Message}");
                return false;
            }

            if (executorInstance == null)
            {
                YIUIMCPLog.LogError($"创建工具实例返回空 {type.FullName}");
                return false;
            }

            // 获取参数类型
            Type paramType = typeof(YIUIMCPBaseParams);
            var baseType = type;
            while (baseType != null)
            {
                if (baseType.IsGenericType && baseType.GetGenericTypeDefinition() == typeof(YIUIMCPBaseExecutor<>))
                {
                    paramType = baseType.GetGenericArguments()[0];
                    break;
                }
                baseType = baseType.BaseType;
            }

            toolInfo = new YIUIMCPToolInfo
            {
                Name = name,
                Description = description,
                Executor = executorInstance,
                ParamType = paramType
            };

            return true;
        }

        public static async Task<YIUIMCPResult> Invoke(string toolName, string jsonParams)
        {
            if (!_tools.TryGetValue(toolName, out var toolInfo))
            {
                return YIUIMCPResult.FailureLog($"{toolName}, 没有这个MCP工具");
            }

            var paramType = toolInfo.ParamType ?? typeof(YIUIMCPBaseParams);
            YIUIMCPBaseParams paramInstance = null;

            if (!string.IsNullOrEmpty(jsonParams))
            {
                try
                {
                    var deserialized = JsonConvert.DeserializeObject(jsonParams, paramType);
                    paramInstance = deserialized as YIUIMCPBaseParams;
                    if (paramInstance == null)
                    {
                        return YIUIMCPResult.FailureLog($"{toolName}, 参数类型错误: {paramType.Name} 必须继承 YIUIMCPBaseParams");
                    }
                }
                catch (Exception ex)
                {
                    return YIUIMCPResult.FailureLog($"{toolName}, 反序列化参数失败 {toolName}. 预期 {paramType.Name}. Error: {ex.Message}");
                }
            }
            else
            {
                try
                {
                    var instance = Activator.CreateInstance(paramType);
                    paramInstance = instance as YIUIMCPBaseParams;
                    if (paramInstance == null)
                    {
                        return YIUIMCPResult.FailureLog($"{toolName}, 参数类型错误: {paramType.Name} 必须继承 YIUIMCPBaseParams");
                    }
                }
                catch (Exception ex)
                {
                    return YIUIMCPResult.FailureLog($"{toolName}, 创建默认参数失败 {toolName}. 预期 {paramType.Name}. Error: {ex.Message}");
                }
            }

            try
            {
                var result = await toolInfo.Executor.Execute(paramInstance);
                result.message = $"{toolName}, {result.message}";
                return result;
            }
            catch (Exception ex)
            {
                return YIUIMCPResult.FailureLog($"{toolName}, 执行工具失败: {ex.InnerException?.Message ?? ex.Message}");
            }
        }
    }
}