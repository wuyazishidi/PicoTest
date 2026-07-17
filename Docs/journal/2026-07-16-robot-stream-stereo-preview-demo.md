# 2026-07-16 Robot Stream Stereo Preview Demo（Exp-RobotStreamStereoPreview）

分支：`fisheye-stereo-dome`。设计 `Docs/designs/2026-07-16-robot-stream-stereo-preview-demo.md`。
需求：`Tools/run_stereo_left_viewer.py` 只推左目单目（本次同一天早些时候完成的
`Exp-RobotStreamLeftPreview` 对接的就是这个），用户确认后要"真双目"版本——新增
`Tools/run_stereo_viewer.py`（转发完整 SBS、不裁剪、端口 8889），本 demo 是它的 Unity 对接端。

## 做了什么

全部在 `Assets/Experiments/Exp-RobotStreamStereoPreview/`，**最大化复用**而非复制：

| 产出 | 说明 |
|---|---|
| `RobotStreamStereoPreviewFeeder` | 结构同 `RobotStreamLeftPreviewFeeder`，但 UV 改回 SBS 分半（`leftUVRect=(0,0,0.5,1)`/`rightUVRect=(0.5,0,0.5,1)`，同 `RobotStreamFeeder`），标定用 `RobotCalib.BuildEyeCalibrations` 的左右眼（左目版只取 left） |
| （无新文件）传输 | 直接**引用** `Experiment.RobotStreamLeftPreview` 的 `HttpOfferVideoSource`——同一套 HTTP offer/answer 握手，双目/单目对它没有区别，只是画面宽了一倍，零改动复用 |
| （无新文件）颜色修正 | 直接**复用** `Exp-RobotStreamLeftPreview/Resources/SwapRB.shader`——`Resources.Load` 按虚拟路径合并全项目 `Resources/` 目录，跨实验目录也找得到，无需复制 |
| 场景生成器 + Builder 注册 `robotstereo` | Build/Install APK 菜单自动出现 |
| PlayMode 冒烟测试 | 假源（SBS 红/蓝）验证左右半边确实是两块不同画面，非左目版"整图复用"那种 |

新增的代码只有一个 `.cs`（Feeder）+ asmdef/Editor/Tests 脚手架——没有重新实现传输层或颜色修正，
两者都是跨实验目录直接引用/复用已验证过的东西。

## 为何这么做

- **不复制 `HttpOfferVideoSource`**：协议完全一样（一次性 HTTP offer/answer，无 trickle），双目
  只是喂给它的源画面宽了一倍，对这个类完全透明，加一份引用比复制一份代码更诚实。
- **不复制 `SwapRB.shader`**：Unity `Resources.Load` 是按名字在全项目所有 `Resources/` 目录里找，
  不关心物理上在哪个实验文件夹——两个 demo 共享同一份 shader 资产，颜色修正逻辑改一处两边同步。
- **标定左右眼都用**：这次是真双目源，`RobotCalib.BuildEyeCalibrations` 本来就返回 (left, right)，
  左目版当时只取了 left 是因为源只有左目；现在源双目了，用回完整返回值即可，`RobotCalib` 本身不改。

## 测试结果

编译 Success 0 错误（asmdef 一次配对：吸取了左目版当时漏引用 `Unity.WebRTC` 的教训，这次没犯，
不过本 demo 的 Feeder 本身不直接触碰 `RTCPeerConnection` 类型，只用 `HttpOfferVideoSource`/
`IWebRtcVideoSource`，所以其实不需要 `Unity.WebRTC` 引用）。

**EditMode 119/119、PlayMode 12（+1 skip）/12 全绿**，`.gates/tests-green` 已写。
`PicoTest/Robot Stream Stereo Preview/Build Demo Scene` 菜单已跑，场景文件已生成。

### 插曲：一个无关的假阳性失败（已修）

跑 EditMode 时连续 3 次稳定复现 `CaptureSessionTests.Snapshot_ReflectsRecordingState` 失败
（`FrameCount` 断言为 0 + TearDown 文件占用 IOException），与本次改动的 WebRTC/机器人代码毫无
交集。查源码定位到根因：`SessionRecorder.GetFrameCount()` 读的是**写线程**增的计数器
（`Assets/Main/Core/Recording/SessionRecorder.cs:161,175`），但测试在 `Tick()`
（主线程同步入队）后**零等待**立刻断言该计数 >0——这是测试本身的竞态（不是本次引入的回归），
平时写线程调度够快掩盖了这个洞，这次会话密集触发好几轮域重载/编译，主线程和写线程的调度节奏被
打乱，竞态窗口暴露了。

已修：`CaptureSessionTests.Snapshot_ReflectsRecordingState` 改成轮询等待
`FrameCount>0`（2s 超时）而不是断言瞬时值，`Tick()`/`GetSnapshot()` 语义不变，只改测试的等待方式。
`SessionRecorder` 本身不改（异步写线程是有意设计，"IO 不进采集线程"）。修完后连续 3 次背靠背
EditMode 119/119 全绿（含之前稳定复现失败的那个场景），PlayMode 12（+1 skip）/12 全绿。

## 遗留 / 下一步

1. **PC 环回**：跑 `python Tools/run_stereo_viewer.py`（及其上游 `composed_camera`/`ego_stereo`
   推流服务），`serverUrl=http://127.0.0.1:8889`，肉眼验证穹顶左右眼真的是两路不同画面、颜色对。
2. **真机**：`Build APK/Robot Stream Stereo Preview` → 装机 → 局域网连机器人侧服务、立体分眼、
   A/B 对比、cmd 调参。目前卡在还没定下机器人的实际连接方式/IP（问了用户，用户还没回复具体怎么连）。
