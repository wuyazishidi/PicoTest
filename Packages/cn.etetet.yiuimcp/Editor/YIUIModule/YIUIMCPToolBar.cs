#if YIUI
using UnityEditor;
using UnityEngine;

namespace YIUIFramework.Editor.MCP
{
    [InitializeOnLoad]
    public static class YIUIMCPToolBar
    {
        private static Texture _cachedIcon1;
        private static Texture _cachedIcon2;

        static YIUIMCPToolBar()
        {
            YIUIToolbarExtender.AddRightToolbarGUI(OnYIUIMCPToolbarGUI, 1100);
        }

        private static void OnYIUIMCPToolbarGUI()
        {
            Texture icon = null;
            if (YIUIMCPServerHelper.IsRunning)
            {
                _cachedIcon1 ??= AssetDatabase.LoadAssetAtPath<Texture>("Packages/cn.etetet.yiuimcp/Editor/YIUIModule/Icon/YIUIMCPIcon1.png");
                icon = _cachedIcon1;
            }
            else
            {
                _cachedIcon2 ??= AssetDatabase.LoadAssetAtPath<Texture>("Packages/cn.etetet.yiuimcp/Editor/YIUIModule/Icon/YIUIMCPIcon2.png");
                icon = _cachedIcon2;
            }

            GUILayout.Space(5);
            GUIContent iconContent = new(string.Empty, icon);
            iconContent.tooltip = "YIUIMCP";
            if (GUILayout.Button(iconContent))
            {
                YIUIAutoTool.OpenWindow();
                EditorApplication.delayCall += () =>
                {
                    var window = EditorWindow.GetWindow<YIUIAutoTool>();
                    if (window != null)
                    {
                        window.SelectModule("YIUIMCP");
                    }
                };
            }

            GUILayout.Space(5);
        }
    }
}
#endif