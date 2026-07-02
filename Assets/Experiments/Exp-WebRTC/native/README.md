# Exp-WebRTC / native —— C API wrapper（libpicowebrtc）

**状态：待外部预编译库 + 原生工具链，当前沙箱无法构建/测试。** 这里是 C API 契约（`picowebrtc.h`）与构建说明；实现（`picowebrtc.cpp`）需在有 libwebrtc + Android NDK 的环境完成。

## 依赖
- **libwebrtc**：`shiguredo/webrtc-build` 预编译包（arm64-v8a for PICO；x86_64 for PC 联调）。放构建期链接。
- **libyuv**：I420 → RGBA(SBS) 转换（多在 libwebrtc 内附带，或单独链）。

## 构建（示意）
1. 取 shiguredo/webrtc-build 对应版本的 `libwebrtc.a`/头文件（arm64-v8a）。
2. 用 Android NDK（项目 toolchain，见 `Builder.EnsureAndroidToolchain`）编 `picowebrtc.cpp` + 链 libwebrtc + libyuv → `libpicowebrtc.so`。
3. 产物放：
   - `Assets/Plugins/Android/libs/arm64-v8a/libwebrtc.so`
   - `Assets/Plugins/Android/libs/arm64-v8a/libpicowebrtc.so`
   IL2CPP/Gradle 自动并入 APK。
4. C# 侧：Player Settings / asmdef 定义 `WEBRTC_NATIVE` 后，`WebRtcInterop`/`WebRtcVideoSource` 才编入原生调用；缺库时默认走 `FakeStereoVideoSource`（M0）。

## 契约
见 `picowebrtc.h`。C# 绑定：`Scripts/Native/WebRtcInterop.cs`。帧回调线程禁 JNI/Unity（C# 只 Marshal.Copy + 双缓冲）。

## 注意
- wrapper 必须 `extern "C"`（避免 C++ mangling）。
- 帧输出固定 RGBA(SBS 2560×720)，与穹顶 shader 期望一致。
- 编解码优先硬解（PICO H.264/H.265）；软解兜底。
