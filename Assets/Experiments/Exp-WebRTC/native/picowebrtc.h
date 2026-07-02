/*
 * picowebrtc.h —— PicoTest WebRTC C API wrapper 契约（extern "C"）。
 * 实现基于 shiguredo/webrtc-build 的 libwebrtc（C++），对外只暴露 C 符号供 Unity P/Invoke。
 * 与 C# 侧 Assets/Experiments/Exp-WebRTC/Scripts/Native/WebRtcInterop.cs 一一对应。
 *
 * 帧输出约定：接收端把远端双目鱼眼流解码为 I420，wrapper 内用 libyuv 转 RGBA(SBS，
 * 2560x720，左右各半)，通过 on_frame 回调把连续 RGBA 缓冲交给 C#（在 libwebrtc 解码线程）。
 * 回调线程严禁调用 JNI/Unity —— C# 侧只做 Marshal.Copy + 双缓冲。
 */
#ifndef PICOWEBRTC_H
#define PICOWEBRTC_H

#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/* data: RGBA(SBS) 连续内存; size: 字节数; width/height: 整帧像素; ts_ns: 采集/显示时间戳; user: 透传 */
typedef void (*wrtc_frame_cb)(const uint8_t* data, int size, int width, int height, int64_t ts_ns, void* user);

/* kind: 0=offer 1=answer 2=candidate; text: SDP 或 candidate 串(以 \0 结尾) */
typedef void (*wrtc_signal_cb)(int kind, const char* text, void* user);

/* 创建 PeerConnection 上下文。返回不透明句柄；失败返回 NULL。 */
void* wrtc_create(wrtc_frame_cb on_frame, wrtc_signal_cb on_signal, void* user);

/* 启动。offer_sdp_or_null==NULL 表示接收端(等待远端 offer)；非空表示以该 offer 作主叫。返回 0 成功。 */
int wrtc_start(void* handle, const char* offer_sdp_or_null);

/* 设置远端 SDP。kind: 0=offer(收到对端 offer,本端将产生 answer 经 on_signal 回传) 1=answer。 */
void wrtc_set_remote_sdp(void* handle, int kind, const char* sdp);

/* 加入远端 ICE candidate。 */
void wrtc_add_ice(void* handle, const char* candidate, const char* sdp_mid, int sdp_mline_index);

/* 关闭并释放。 */
void wrtc_close(void* handle);

#ifdef __cplusplus
}
#endif

#endif /* PICOWEBRTC_H */
