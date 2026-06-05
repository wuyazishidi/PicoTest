using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace YIUIFramework.Editor.MCP
{
    internal static class YIUIMCPPortRecovery
    {
        private const int AddressFamilyIpv4 = 2;
        private const int WaitStepMs = 100;
        private const int InitialWaitAttempts = 10;
        private const int FinalWaitAttempts = 20;
        private const int KillWaitMs = 2000;

        public static bool TryForceReleasePort(int port, int currentProcessId)
        {
            if (port <= 0)
            {
                YIUIMCPLog.LogError($"强制释放端口失败，无效端口: {port}");
                return false;
            }

            if (WaitForPortRelease(port, InitialWaitAttempts))
            {
                return true;
            }

            var listenerProcessIds = GetListeningProcessIds(port);
            if (listenerProcessIds.Count == 0)
            {
                YIUIMCPLog.LogError($"端口 {port} 仍未释放，但未找到可结束的监听进程");
                return false;
            }

            foreach (var processId in listenerProcessIds)
            {
                if (processId == currentProcessId)
                {
                    YIUIMCPLog.Log($"端口 {port} 仍由当前进程({processId})持有，已尝试释放旧监听，继续等待系统回收");
                    continue;
                }

                TryKillProcess(processId, port);
            }

            return WaitForPortRelease(port, FinalWaitAttempts);
        }

        private static bool WaitForPortRelease(int port, int attempts)
        {
            for (var i = 0; i < attempts; i++)
            {
                if (GetListeningProcessIds(port).Count == 0)
                {
                    return true;
                }

                Thread.Sleep(WaitStepMs);
            }

            return GetListeningProcessIds(port).Count == 0;
        }

        private static void TryKillProcess(int processId, int port)
        {
            try
            {
                using (var process = Process.GetProcessById(processId))
                {
                    var processName = process.ProcessName;
                    YIUIMCPLog.Log($"端口 {port} 被进程 {processName}({processId}) 占用，正在尝试结束该进程...");
                    process.Kill();

                    if (!process.WaitForExit(KillWaitMs))
                    {
                        YIUIMCPLog.LogError($"结束占用端口 {port} 的进程 {processName}({processId}) 超时");
                        return;
                    }

                    YIUIMCPLog.Log($"已结束占用端口 {port} 的进程 {processName}({processId})");
                }
            }
            catch (Exception e)
            {
                YIUIMCPLog.LogError($"结束占用端口 {port} 的进程 {processId} 失败: {e.Message}");
            }
        }

        private static HashSet<int> GetListeningProcessIds(int port)
        {
            var processIds = new HashSet<int>();
            var bufferSize = 0;

            var result = GetExtendedTcpTable(IntPtr.Zero, ref bufferSize, true, AddressFamilyIpv4, TcpTableClass.OwnerPidListener, 0);
            if (result != 0 && result != 122)
            {
                YIUIMCPLog.LogError($"读取 TCP 监听表失败，错误码: {result}");
                return processIds;
            }

            var tableBuffer = Marshal.AllocHGlobal(bufferSize);
            try
            {
                result = GetExtendedTcpTable(tableBuffer, ref bufferSize, true, AddressFamilyIpv4, TcpTableClass.OwnerPidListener, 0);
                if (result != 0)
                {
                    YIUIMCPLog.LogError($"读取 TCP 监听表失败，错误码: {result}");
                    return processIds;
                }

                var rowCount = Marshal.ReadInt32(tableBuffer);
                var rowPtr = IntPtr.Add(tableBuffer, sizeof(int));
                var rowSize = Marshal.SizeOf(typeof(MibTcpRowOwnerPid));

                for (var i = 0; i < rowCount; i++)
                {
                    var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(rowPtr);
                    if (ParsePort(row.localPort) == port)
                    {
                        processIds.Add((int)row.owningPid);
                    }

                    rowPtr = IntPtr.Add(rowPtr, rowSize);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(tableBuffer);
            }

            return processIds;
        }

        private static int ParsePort(byte[] rawPort)
        {
            if (rawPort == null || rawPort.Length < 2)
            {
                return -1;
            }

            return (rawPort[0] << 8) + rawPort[1];
        }

        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr tcpTable, ref int tcpTableLength, bool sort, int ipVersion, TcpTableClass tcpTableType, uint reserved);

        private enum TcpTableClass
        {
            OwnerPidListener = 3,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MibTcpRowOwnerPid
        {
            public uint state;
            public uint localAddr;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] localPort;

            public uint remoteAddr;

            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 4)]
            public byte[] remotePort;

            public uint owningPid;
        }
    }
}
