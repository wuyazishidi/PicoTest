using UnityEngine;

namespace PicoTest
{
    /// <summary>
    /// 场景入口（宪法 #14：场景极简，内容由代码实例化）。
    /// M1：仅放一个验证物体；M2 起在此装配采集/UI/RemoteBridge 模块。
    /// </summary>
    public class Bootstrap : MonoBehaviour
    {
        private void Start()
        {
            Debug.Log($"[Bootstrap] PicoTest started. SchemaVersion={Core.CoreInfo.SchemaVersion}");

            // M1 验证载体：一个面前的立方体（确认渲染/坐标系正常）
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "VerificationCube";
            cube.transform.position = new Vector3(0, 1.2f, 2f);
            cube.transform.localScale = Vector3.one * 0.3f;
        }
    }
}
