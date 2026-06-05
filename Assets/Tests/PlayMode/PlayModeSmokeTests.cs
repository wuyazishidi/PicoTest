using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PicoTest.Tests.PlayMode
{
    /// <summary>PlayMode 冒烟测试：验证运行时测试链路（不依赖 XR 子系统）。</summary>
    public class PlayModeSmokeTests
    {
        [UnityTest]
        public IEnumerator GameObject_Creation_Works()
        {
            var go = new GameObject("SmokeTestObject");
            yield return null;
            Assert.IsNotNull(GameObject.Find("SmokeTestObject"));
            Object.Destroy(go);
        }
    }
}
