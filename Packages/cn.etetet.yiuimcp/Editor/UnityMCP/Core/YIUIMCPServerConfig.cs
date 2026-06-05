using System;
using System.IO;
using Sirenix.OdinInspector;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace YIUIFramework.Editor.MCP
{
    /// <summary>
    /// MCP Server 启动模式
    /// </summary>
    public enum EYIUIMCPStartMode
    {
        /// <summary>
        /// 关闭YIUIMCP功能 （默认行为）
        /// 首次安装后修改 防止美术,策划不需要这个功能还要手动关闭,所以默认关闭更友好
        /// </summary>
        [LabelText("关闭启动")]
        Close = 0,

        /// <summary>
        /// 始终自动启动
        /// </summary>
        [LabelText("自动启动")]
        Auto = 1,

        /// <summary>
        /// 手动启动（任何时候都需要手动启动）(基本上不用)
        /// </summary>
        [LabelText("手动启动")]
        Manual = 2,
    }

    /// <summary>
    /// MCP Server 配置管理
    /// </summary>
    public static class YIUIMCPServerConfig
    {
        private const string KEY_START_MODE = "YIUIMCP_StartMode";

        private static EYIUIMCPStartMode m_StartMode;

        private const int DefaultPort = 3212;
        private static int _port = -1;

        public static int Port
        {
            get
            {
                if (_port <= 0)
                {
                    _port = LoadPortFromFile();
                }

                return _port;
            }
        }

        public static void Initialize()
        {
            m_StartMode = (EYIUIMCPStartMode)EditorPrefs.GetInt(KEY_START_MODE, (int)EYIUIMCPStartMode.Auto);
        }

        /// <summary>
        /// 启动模式
        /// </summary>
        public static EYIUIMCPStartMode StartMode
        {
            get => m_StartMode;
            set
            {
                m_StartMode = value;
                EditorPrefs.SetInt(KEY_START_MODE, (int)value);

                //任何改变都强制编译 这样才能重启域
                CompilationPipeline.RequestScriptCompilation(RequestScriptCompilationOptions.CleanBuildCache);
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
            }
        }

        private static string GetPortFilePath()
        {
            var packagePath = Path.GetFullPath(Path.Combine(Application.dataPath, "../Packages/cn.etetet.yiuimcp"));
            return Path.Combine(packagePath, "UTO/.port");
        }

        private static int LoadPortFromFile()
        {
            try
            {
                var portFilePath = GetPortFilePath();
                if (File.Exists(portFilePath))
                {
                    var content = File.ReadAllText(portFilePath).Trim();
                    if (int.TryParse(content, out int port) && port > 0 && port <= 65535)
                    {
                        return port;
                    }
                }
            }
            catch (Exception e)
            {
                YIUIMCPLog.LogError($"读取端口配置失败: {e.Message}");
            }

            return DefaultPort;
        }

        public static void SavePortToFile(int port)
        {
            try
            {
                var portFilePath = GetPortFilePath();
                var dir = Path.GetDirectoryName(portFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllText(portFilePath, port.ToString());
                YIUIMCPLog.Log($"端口配置已保存: {port}");
            }
            catch (Exception e)
            {
                YIUIMCPLog.LogError($"保存端口配置失败: {e.Message}");
            }
        }

        public static void SetPort(int port)
        {
            if (port <= 0 || port > 65535)
            {
                YIUIMCPLog.LogError($"无效的端口号: {port}");
                return;
            }

            _port = port;
            SavePortToFile(port);
        }
    }
}