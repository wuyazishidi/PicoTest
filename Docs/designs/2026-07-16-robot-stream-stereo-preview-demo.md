# 2026-07-16 Robot Stream Stereo Preview Demo（Exp-RobotStreamStereoPreview）

## 背景

`Tools/run_stereo_viewer.py`（`run_stereo_left_viewer.py` 的姊妹脚本，本次新增）转发 `ego_stereo`
双目相机的**完整 SBS 画面**（不裁剪），端口 8889，其余（aiortc、HTTP 一次性 offer/answer、R/B 颜色
修正）与左目版完全一致。需要一个真正双目分眼的 Unity demo 来对接。

## 方案

**另起炉灶**，但最大化复用刚做完的 `Exp-RobotStreamLeftPreview`：

- 传输：直接**引用**（不复制）`Exp-RobotStreamLeftPreview` 的 `HttpOfferVideoSource` +
  `OfferPayload`——协议完全一样，唯一差异只是喂给它的 `serverUrl` 默认端口不同（8889）。
- 颜色修正：直接**复用** `Exp-RobotStreamLeftPreview/Resources/SwapRB.shader`——Unity 的
  `Resources.Load` 按虚拟路径合并全项目所有 `Resources/` 目录，不需要新增/复制这个 shader 文件。
- 标定：复用 `Experiment.RobotStream` 的 `RobotCalib.BuildEyeCalibrations`，这次**左右眼都用**
  （不像左目版只取 left）——因为这次是货真价实的双目画面。
- 显示：`FisheyeDomeRenderer`，`leftUVRect=(0,0,0.5,1)`、`rightUVRect=(0.5,0,0.5,1)`（SBS 分半，
  同 `Exp-RobotStream` 的 `RobotStreamFeeder` 用法）——`leftTex`/`rightTex` 指向同一张
  （颜色修正后的）纹理，左右眼各自的 UV rect 负责取对应半边。
- 交互：capture 位姿补偿 + cmd 调参 + A/B 对比 + HUD + 安全退出——照抄
  `RobotStreamLeftPreviewFeeder`（只是标定/UV 从单目改回双目）。

## 与两个既有 demo 的关系

- `Exp-RobotStream`：同样是 SBS 双目 + 穹顶，但信令走 Node `signaling.js` 中继；本 demo 信令走
  `run_stereo_viewer.py` 的一次性 HTTP offer/answer（同 `Exp-RobotStreamLeftPreview`）。
- `Exp-RobotStreamLeftPreview`：同样的 HTTP offer/answer 传输，但那边画面单目（源本身裁了右目）；
  本 demo 画面双目，UV 整图改回分半，标定从"只取左眼"改回"左右眼都用"。

## 验收标准

同 `Docs/designs/2026-07-16-robot-stream-left-preview-demo.md` 的 4 条，換成本 demo 的路径/端口
（8889）与双目断言（左右半边应显示不同内容——PlayMode 冒烟测试用假源的红/蓝分色验证这一点）。
