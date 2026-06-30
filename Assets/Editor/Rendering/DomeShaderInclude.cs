// Assets/Editor/Rendering/DomeShaderInclude.cs
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PicoTest.Editor.Rendering
{
    /// <summary>
    /// 把 PicoTest/FisheyeDome 加入 GraphicsSettings 的 Always Included Shaders。
    /// 穹顶材质在运行时经 Shader.Find 创建，无任何打包资源引用它 → IL2CPP 构建会剥离该 shader
    /// → Shader.Find 返回 null → 真机全黑。加入 Always Included 即强制打包。
    /// </summary>
    public static class DomeShaderInclude
    {
        private const string ShaderName = "PicoTest/FisheyeDome";

        [MenuItem("PicoTest/Fix/Ensure FisheyeDome Shader Included")]
        public static void Ensure()
        {
            var shader = Shader.Find(ShaderName);
            if (shader == null) { Debug.LogError($"[DomeShaderInclude] shader '{ShaderName}' not found in editor"); return; }

            var so = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
            var arr = so.FindProperty("m_AlwaysIncludedShaders");

            for (int i = 0; i < arr.arraySize; i++)
            {
                if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    Debug.Log($"[DomeShaderInclude] '{ShaderName}' already in Always Included Shaders");
                    return;
                }
            }

            int idx = arr.arraySize;
            arr.InsertArrayElementAtIndex(idx);
            arr.GetArrayElementAtIndex(idx).objectReferenceValue = shader;
            so.ApplyModifiedProperties();
            AssetDatabase.SaveAssets();
            Debug.Log($"[DomeShaderInclude] added '{ShaderName}' to Always Included Shaders ({idx + 1} total)");
        }
    }
}
