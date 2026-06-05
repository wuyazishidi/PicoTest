using System.Collections;
using Unity.EditorCoroutines.Editor;
using UnityEditor;
using UnityEngine;

namespace YIUIFramework.Editor.MCP
{
    public static class YIUIMCPLog
    {
        //TODO功能 以后可以添加日志等级 用来屏蔽一些日志
        //待定用 0-9 等级 0 表示所有日志都显示 9为最高等级
        //可以通过配置文件来设置显示等级
        public const int ShowLogLevel = 0;

        public static void Log(string message, int level = 0)
        {
            if (level < ShowLogLevel)
            {
                return;
            }

            #if YIUIMCP_DEBUG_LOG
            Debug.Log($"<color=green>[YIUIMCP]</color> {message}, 实例ID: {YIUIMCPServer.InstanceId}");
            #else
            Debug.Log($"<color=green>[YIUIMCP]</color> {message}");
            #endif
        }

        public static void LogError(string message, int level = 0)
        {
            if (level < ShowLogLevel)
            {
                return;
            }

            #if YIUIMCP_DEBUG_LOG
            Debug.LogError($"<color=red>[YIUIMCP]</color> {message}, 实例ID: {YIUIMCPServer.InstanceId}");
            #else
            Debug.LogError($"<color=red>[YIUIMCP]</color> {message}");
            #endif
        }

        /// <summary>
        /// 显示自定义时长的编辑器通知
        /// </summary>
        public static void ShowNotification(string content, float showSeconds = 2f)
        {
            var mainWindow = EditorWindow.GetWindow(typeof(EditorWindow));
            mainWindow.ShowNotification(new GUIContent(content));
            EditorCoroutineUtility.StartCoroutine(RemoveAfterDelay(showSeconds, mainWindow), null);
        }

        static IEnumerator RemoveAfterDelay(float delay, EditorWindow window)
        {
            yield return new WaitForSeconds(delay);
            if (window != null)
            {
                window.RemoveNotification();
            }
        }
    }
}