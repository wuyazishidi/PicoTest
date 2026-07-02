// Assets/Main/Vst/VstCamera.cs
// 移植自 G:\Sdy\ClaudeSdy\YC-Ego\Assets\Scripts\Core\VstCamera.cs（PICO 4U VST raw 鱼眼采集）。
// 改动：去掉 RecordingConfig（分辨率/fps 用常量）、YCEgo.Utils.Log → 本文件内 Log shim、
// 命名空间 PicoTest.Vst。保留全部 PICO Enterprise 崩溃规避逻辑（原生线程禁 JNI、延迟泵、重试）。
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.XR.PICO.TOBSupport;
using UnityEngine;

namespace PicoTest.Vst
{
    /// <summary>本地 Log shim（替代 YCEgo.Utils.Log），仅在 UnityMain 调用。</summary>
    internal static class Log
    {
        public static void Info(string m) => Debug.Log("[VST] " + m);
        public static void Warn(string m) => Debug.LogWarning("[VST] " + m);
        public static void Error(string m) => Debug.LogError("[VST] " + m);
    }

    /// <summary>
    /// PICO 4 Ultra VST 相机生命周期。开机开一次、整个会话保持流。
    /// KEY_OUTPUT_CAMERA_RAW_DATA=TRUE → raw 鱼眼 RGBA32（去畸变在我们的穹顶 shader 做）。
    /// 帧回调在原生线程 —— 订阅者必须线程安全，禁任何 Unity/JNI 调用。
    /// 需 System ≥ 5.15 + Enterprise 激活。
    /// </summary>
    public static class VstCamera
    {
        // Interlace SBS：每回调一整张 SBS 帧（左右已拼）。2560×960 → 每眼 1280×960。
        public static int Width = 2560;
        public static int Height = 960;
        public static int PerEyeWidth = 1280;
        public const int BytesPerPixel = 4;
        public static int FrameSize = 2560 * 960 * BytesPerPixel;
        public static int BufferSize = 2560 * 960 * BytesPerPixel + 1024 * 1024;
        public static int Fps = 30; // 30 是固件认的切换键（采集帧率）；60 为出厂默认。Feeder 经 Configure 覆盖

        public static event Action<Frame> OnFrame;

        public static bool IsOpen { get; private set; }
        public static bool IsStreaming { get; private set; }
        public static long FramesReceived { get; private set; }
        public static int LastFrameDatasize { get; private set; }
        public static int LastFrameBpp { get; private set; }

        public static RGBCameraParamsNew Params;
        public static bool ParamsValid;
        public static Matrix4x4 LeftExtrinsics = Matrix4x4.identity;
        public static Matrix4x4 RightExtrinsics = Matrix4x4.identity;
        public static bool ExtrinsicsValid;

        static IntPtr _buffer = IntPtr.Zero;
        static bool _initRequested;
        static bool _bound;
        static readonly object _stateLock = new object();

        static bool _fetchParamsPending;
        static int _fetchParamsRetries;
        const int MaxParamsFetchRetries = 10;

        static volatile bool _firstFramePendingLog;
        static int _firstFrameW, _firstFrameH;
        static long _firstFrameDataSize, _firstFrameTsSdkNs;
        static int _firstFrameStatus;

        static volatile int _trace;
        static int _traceLogged;
        static readonly string[] _traceNames =
        {
            "(init)", "bind_cb:enter", "bind_cb:UseGlobalPose_done", "open_cb:enter",
            "open_cb:AllocHGlobal_begin", "open_cb:AllocHGlobal_done", "open_cb:buffer_touch_done",
            "open_cb:SetCameraFrameBuffer_begin", "open_cb:SetCameraFrameBuffer_done",
            "open_cb:StartGetImageData_begin", "open_cb:StartGetImageData_done",
            "open_cb:exit", "frame_cb:first_frame_received",
        };

        /// <summary>可在 Initialize 前设分辨率/fps。</summary>
        public static void Configure(int width, int height, int fps)
        {
            Width = width; Height = height; PerEyeWidth = width / 2; Fps = fps;
            FrameSize = width * height * BytesPerPixel;
            BufferSize = FrameSize + 1024 * 1024;
        }

        public static void Initialize()
        {
            lock (_stateLock)
            {
                if (_initRequested) return;
                _initRequested = true;
            }
            Log.Info($"configured Width={Width} Height={Height} PerEyeWidth={PerEyeWidth} FrameSize={FrameSize / (1024 * 1024)}MB");

            try
            {
                Log.Info("InitEnterpriseService(isCamera=true)…");
                bool init = PXR_Enterprise.InitEnterpriseService(true);
                Log.Info($"InitEnterpriseService → {init}");
                if (!init)
                {
                    Log.Error("Enterprise camera token unavailable. 需 PICO Business Manager / Enterprise 激活。跳过相机。");
                    return;
                }

                try { PXR_Enterprise.UnBindEnterpriseService(); Log.Info("defensive pre-bind UnBind done"); }
                catch (Exception e) { Log.Warn($"pre-bind unbind threw (ok on first launch): {e.Message}"); }

                Log.Info("BindEnterpriseService…");
                PXR_Enterprise.BindEnterpriseService(bound =>
                {
                    _trace = 1;
                    Log.Info($"bind callback → {bound}");
                    if (!bound) { Log.Error("enterprise service bind failed; cannot open camera."); return; }
                    _bound = true;
                    try { PXR_Enterprise.UseGlobalPose(true); Log.Info("UseGlobalPose(true)"); }
                    catch (Exception e) { Log.Warn($"UseGlobalPose threw: {e.Message}"); }
                    _trace = 2;
                    OpenCamera();
                });
            }
            catch (Exception e) { Log.Error($"Initialize crashed: {e}"); }
        }

        static void OpenCamera()
        {
            try
            {
                // KEY_OUTPUT_CAMERA_RAW_DATA 必须经 Configurefor4U(dict) 在 open 前设；
                // 与 MCTF/EIS/MFNR 混进一个 dict 会静默丢掉它 → 返回 rectified 而非 raw。
                var configDict = new Dictionary<string, string>
                {
                    { PXRCapture.KEY_OUTPUT_CAMERA_RAW_DATA, PXRCapture.VALUE_TRUE },
                };
                if (Fps == 30) configDict[PXRCapture.KEY_VIDEO_FPS] = "30";
                PXR_Enterprise.Configurefor4U(configDict);
                Log.Info($"Configurefor4U(RAW_DATA=TRUE, FPS={(Fps == 30 ? "30" : "default 60")})");

                var openDict = new Dictionary<string, string>
                {
                    { PXRCapture.KEY_MCTF, PXRCapture.VALUE_TRUE },
                    { PXRCapture.KEY_EIS,  PXRCapture.VALUE_FALSE },
                    { PXRCapture.KEY_MFNR, PXRCapture.VALUE_TRUE },
                };
                Log.Info("OpenCameraAsyncfor4U(MCTF=TRUE, EIS=FALSE, MFNR=TRUE)…");
                PXR_Enterprise.OpenCameraAsyncfor4U(OnCameraOpened, openDict);
            }
            catch (Exception e) { Log.Error($"OpenCameraAsyncfor4U threw: {e}"); }
        }

        static volatile bool _startRetryPending;
        static int _startRetryAttempts;
        static float _nextStartRetryTime;
        const int MaxStartRetries = 10;
        const float StartRetryDelaySec = 0.6f;

        static volatile bool _openCallbackPendingLog;
        static bool _openCallbackSuccess;
        static bool _firstStartResult;

        static void OnCameraOpened(bool success)
        {
            // Binder 线程 —— 绝对禁 JNI（含 Debug.Log）。状态捕获到纯 C#，PumpFromMain 再 log。
            _trace = 3;
            _openCallbackSuccess = success;
            _openCallbackPendingLog = true;
            if (!success) return;

            try
            {
                if (_buffer == IntPtr.Zero)
                {
                    _trace = 4;
                    _buffer = Marshal.AllocHGlobal(BufferSize);
                    _trace = 5;
                    for (int i = 0; i < BufferSize; i += 4096) Marshal.WriteByte(_buffer, i, 0);
                    _trace = 6;
                }
                IsOpen = true;
                _startRetryAttempts = 0;
                _startRetryPending = true;
                _fetchParamsPending = true;
                _fetchParamsRetries = 0;
                try
                {
                    _trace = 7;
                    PXR_Enterprise.SetCameraFrameBufferfor4U(Width, Height, ref _buffer, FrameCallback);
                    _trace = 8;
                    _trace = 9;
                    _firstStartResult = PXR_Enterprise.StartGetImageDatafor4U(PXRCaptureRenderMode.PXRCapture_RenderMode_Interlace, Width, Height);
                    _trace = 10;
                    IsStreaming = _firstStartResult;
                    if (_firstStartResult) _startRetryPending = false;
                }
                catch { _firstStartResult = false; }
                _nextStartRetryTime = Time.unscaledTime + StartRetryDelaySec;
                _trace = 11;
            }
            catch { /* no logging from binder thread */ }
        }

        static bool TryStartStream()
        {
            try
            {
                PXR_Enterprise.SetCameraFrameBufferfor4U(Width, Height, ref _buffer, FrameCallback);
                bool started = PXR_Enterprise.StartGetImageDatafor4U(PXRCaptureRenderMode.PXRCapture_RenderMode_Interlace, Width, Height);
                IsStreaming = started;
                if (started) _startRetryPending = false;
                return started;
            }
            catch { return false; }
        }

        static void PumpStartRetry()
        {
            if (!_startRetryPending) return;
            if (Time.unscaledTime < _nextStartRetryTime) return;
            _startRetryAttempts++;
            _nextStartRetryTime = Time.unscaledTime + StartRetryDelaySec;
            if (_startRetryAttempts > MaxStartRetries)
            {
                _startRetryPending = false;
                Log.Error($"StartGetImageDatafor4U FAILED after {MaxStartRetries} retries. " +
                          "pxrcaptureservice 持续拒绝 RAW+Interlace —— 需硬关机（长按电源 10s，等 1 分钟，再开）清设备级状态。");
                return;
            }
            bool ok = TryStartStream();
            Log.Info($"StartGetImageDatafor4U retry #{_startRetryAttempts}/{MaxStartRetries} → {ok}");
            if (ok) Log.Info($"RAW + Interlace streaming live after {_startRetryAttempts} retries");
        }

        static void TryFetchCameraParams()
        {
            try
            {
                Params = PXR_Enterprise.GetCameraParametersNewfor4U(PerEyeWidth, Height);
                ParamsValid = Params.fx != 0 || Params.fy != 0;
                if (ParamsValid)
                    Log.Info($"intrinsics fx={Params.fx:F3} fy={Params.fy:F3} cx={Params.cx:F3} cy={Params.cy:F3}");
                else
                    Log.Warn("GetCameraParametersNewfor4U returned zero intrinsics — will retry");

                ExtrinsicsValid = PXR_Enterprise.GetCameraExtrinsicsfor4U(out LeftExtrinsics, out RightExtrinsics);
                if (ExtrinsicsValid)
                    Log.Info($"extrinsics L={LeftExtrinsics.GetColumn(3)} R={RightExtrinsics.GetColumn(3)}");
                else
                    Log.Warn("GetCameraExtrinsicsfor4U returned false");
            }
            catch (Exception e) { Log.Warn($"param fetch threw: {e.Message}"); }
        }

        static void FrameCallback(Frame frame)
        {
            // SDK 原生线程 —— 绝对禁 JNI。只捕获纯 C# 状态，OnFrame 订阅者也只许纯 C# 队列操作。
            FramesReceived++;
            if (FramesReceived == 1)
            {
                LastFrameDatasize = (int)frame.datasize;
                int pix = (int)frame.width * (int)frame.height;
                LastFrameBpp = pix > 0 ? LastFrameDatasize / pix : 0;
                _firstFrameW = (int)frame.width;
                _firstFrameH = (int)frame.height;
                _firstFrameDataSize = (long)frame.datasize;
                _firstFrameTsSdkNs = (long)frame.timestamp;
                _firstFrameStatus = (int)frame.status;
                _firstFramePendingLog = true;
                _trace = 12;
            }
            try { OnFrame?.Invoke(frame); }
            catch { /* no logging from native thread */ }
        }

        /// <summary>每帧由 MonoBehaviour.Update 调用：把原生线程捕获、需 JNI 的活在 UnityMain 执行。</summary>
        public static void PumpFromMain()
        {
            int traceSnap = _trace;
            while (_traceLogged < traceSnap)
            {
                _traceLogged++;
                string n = (_traceLogged >= 0 && _traceLogged < _traceNames.Length) ? _traceNames[_traceLogged] : $"#{_traceLogged}";
                Log.Info($"trace → {n}");
            }

            if (_openCallbackPendingLog)
            {
                _openCallbackPendingLog = false;
                Log.Info($"open callback success={_openCallbackSuccess}");
                if (_openCallbackSuccess)
                    Log.Info($"first StartGetImageDatafor4U → {_firstStartResult} (Interlace, {Width}x{Height})");
            }

            PumpStartRetry();

            if (_firstFramePendingLog)
            {
                _firstFramePendingLog = false;
                Log.Info($"FIRST FRAME w={_firstFrameW} h={_firstFrameH} datasize={_firstFrameDataSize} bpp={LastFrameBpp} ts_sdk_ns={_firstFrameTsSdkNs} status={_firstFrameStatus}");
                if (LastFrameBpp != 4)
                    Log.Warn($"bpp={LastFrameBpp} != 4 — RAW 可能返回了非 RGBA32 格式。");
            }
            if (_fetchParamsPending && _fetchParamsRetries < MaxParamsFetchRetries && FramesReceived > 0)
            {
                TryFetchCameraParams();
                _fetchParamsRetries++;
                if (ParamsValid) { _fetchParamsPending = false; Log.Info($"intrinsics fetched after {_fetchParamsRetries} frame(s)"); }
                else if (_fetchParamsRetries >= MaxParamsFetchRetries) { _fetchParamsPending = false; Log.Error("intrinsics still zero after retries"); }
            }
        }

        /// <summary>按 PICO 官方示例关闭相机：仅 CloseCamerafor4U()（不解绑 Enterprise 服务/不释放缓冲）。</summary>
        public static void CloseCamera()
        {
            try
            {
                if (IsStreaming || IsOpen)
                {
                    PXR_Enterprise.CloseCamerafor4U();
                    IsStreaming = false; IsOpen = false;
                    Log.Info("CloseCamerafor4U（官方示例关闭相机）");
                }
            }
            catch (Exception e) { Log.Error($"CloseCamera crashed: {e}"); }
        }

        public static void PauseForScreenOff()
        {
            try
            {
                if (IsStreaming || IsOpen)
                {
                    Log.Info("PauseForScreenOff → CloseCamerafor4U");
                    PXR_Enterprise.CloseCamerafor4U();
                    IsStreaming = false; IsOpen = false;
                }
            }
            catch (Exception e) { Log.Error($"close on pause crashed: {e}"); }

            if (_bound)
            {
                try { Log.Info("PauseForScreenOff → UnBindEnterpriseService"); PXR_Enterprise.UnBindEnterpriseService(); }
                catch (Exception e) { Log.Error($"unbind on pause crashed: {e}"); }
                _bound = false;
            }
        }

        public static void ResumeAfterScreenOn()
        {
            if (!_initRequested) return;
            if (IsOpen) return;
            if (_bound) { Log.Info("ResumeAfterScreenOn → OpenCamera (already bound)"); OpenCamera(); }
            else
            {
                Log.Info("ResumeAfterScreenOn → BindEnterpriseService (re-bind)");
                try
                {
                    PXR_Enterprise.BindEnterpriseService(bound =>
                    {
                        Log.Info($"re-bind callback → {bound}");
                        if (!bound) { Log.Error("enterprise service re-bind failed on resume."); return; }
                        _bound = true;
                        try { PXR_Enterprise.UseGlobalPose(true); } catch (Exception e) { Log.Warn($"UseGlobalPose on resume threw: {e.Message}"); }
                        OpenCamera();
                    });
                }
                catch (Exception e) { Log.Error($"re-bind on resume crashed: {e}"); }
            }
        }

        public static void Shutdown()
        {
            lock (_stateLock) { if (!_initRequested) return; }
            try { if (IsStreaming || IsOpen) { PXR_Enterprise.CloseCamerafor4U(); IsStreaming = false; IsOpen = false; } }
            catch (Exception e) { Log.Error($"close crashed: {e}"); }
            if (_buffer != IntPtr.Zero) { Marshal.FreeHGlobal(_buffer); _buffer = IntPtr.Zero; }
            try { PXR_Enterprise.UnBindEnterpriseService(); }
            catch (Exception e) { Log.Error($"UnBindEnterpriseService crashed: {e}"); }
            _bound = false;
            Log.Info("shutdown");
        }
    }
}
