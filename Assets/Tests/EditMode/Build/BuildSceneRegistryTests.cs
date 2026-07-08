// Assets/Tests/EditMode/Build/BuildSceneRegistryTests.cs
using System.IO;
using System.Linq;
using NUnit.Framework;
using PicoTest.Editor.Build;

namespace PicoTest.Tests.EditMode.Build
{
    /// <summary>
    /// Builder.SceneRegistry 守护：注册的场景必须真实存在（场景改名/移动时此测试先红，
    /// 而不是等到有人点构建菜单才发现），key/APK 名不得重复或为空。
    /// </summary>
    public class BuildSceneRegistryTests
    {
        [Test]
        public void AllRegisteredScenesExistOnDisk()
        {
            foreach (var e in Builder.SceneRegistry)
                Assert.That(File.Exists(e.ScenePath), $"注册表 key={e.Key} 的场景不存在：{e.ScenePath}");
        }

        [Test]
        public void KeysAreUniqueAndNonEmpty()
        {
            var keys = Builder.SceneRegistry.Select(e => e.Key).ToArray();
            foreach (var k in keys)
                Assert.That(string.IsNullOrWhiteSpace(k), Is.False, "空 key");
            Assert.AreEqual(keys.Length, keys.Distinct().Count(), "key 重复");
        }

        [Test]
        public void ApkNamesAreUniqueApkFiles()
        {
            var names = Builder.SceneRegistry.Select(e => e.ApkName()).ToArray();
            foreach (var n in names)
            {
                Assert.That(string.IsNullOrWhiteSpace(n), Is.False, "空 APK 名");
                Assert.That(n, Does.EndWith(".apk"), $"非 .apk 产物名：{n}");
            }
            Assert.AreEqual(names.Length, names.Distinct().Count(), "APK 名重复（会互相覆盖）");
        }
    }
}
