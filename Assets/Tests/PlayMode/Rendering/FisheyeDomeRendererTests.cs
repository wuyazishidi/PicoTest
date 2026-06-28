// Assets/Tests/PlayMode/Rendering/FisheyeDomeRendererTests.cs
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using PicoTest.Rendering;

namespace PicoTest.Tests.PlayMode.Rendering
{
    public class FisheyeDomeRendererTests
    {
        private static FisheyeCalibration Cal()
        {
            var c = ScriptableObject.CreateInstance<FisheyeCalibration>();
            c.fx = c.fy = 500; c.cx = c.cy = 800; c.width = c.height = 1600;
            return c;
        }

        [UnityTest]
        public IEnumerator WorldLocked_ParentsDomeToAnchor_NotCamera()
        {
            var go = new GameObject("renderer");
            var anchor = new GameObject("RobotHeadAnchor").transform;
            var r = go.AddComponent<FisheyeDomeRenderer>();
            r.frame = FisheyeDomeRenderer.RenderFrame.WorldLocked;
            r.robotHeadAnchor = anchor;
            r.leftCalibration = Cal(); r.rightCalibration = Cal();
            r.Initialize();
            yield return null;
            Assert.AreEqual(anchor, r.DomeTransform.parent);
            Object.Destroy(go); Object.Destroy(anchor.gameObject);
        }

        [UnityTest]
        public IEnumerator PushesIntrinsicsToMaterialBlock()
        {
            var go = new GameObject("renderer");
            var r = go.AddComponent<FisheyeDomeRenderer>();
            r.leftCalibration = Cal(); r.rightCalibration = Cal();
            r.Initialize();
            r.PushParameters();
            yield return null;
            var mpb = new MaterialPropertyBlock();
            r.DomeRenderer.GetPropertyBlock(mpb);
            var intrin = mpb.GetVector("_LeftIntrin");
            Assert.AreEqual(500f, intrin.x, 1e-3);
            Assert.AreEqual(800f, intrin.z, 1e-3);
            Object.Destroy(go);
        }
    }
}
