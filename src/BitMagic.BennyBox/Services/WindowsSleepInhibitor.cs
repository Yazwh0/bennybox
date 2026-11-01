using System.ComponentModel;
using System.Runtime.InteropServices;
using BitMagic.BennyBox.Core.Services;
using Microsoft.Extensions.Logging;

namespace BitMagic.BennyBox.Services;

// A power request rather than SetThreadExecutionState, which belongs to the calling thread and so
// doesn't suit a download that hops between pool threads. It keeps the system awake with the screen
// locked or off, and shows up in `powercfg /requests` with the reason below. It only stops idle sleep:
// closing the lid or choosing Sleep still sleeps, which DownloadManager recovers from afterwards.
public sealed class WindowsSleepInhibitor : ISleepInhibitor
{
    private const uint PowerRequestContextVersion = 0;
    private const uint PowerRequestContextSimpleString = 1;
    private const int PowerRequestSystemRequired = 1;

    private readonly ILogger<WindowsSleepInhibitor> _logger;
    private readonly Lock _lock = new();
    private IntPtr _request;

    public WindowsSleepInhibitor(ILogger<WindowsSleepInhibitor> logger)
    {
        _logger = logger;
    }

    public void SetActive(bool active)
    {
        lock (_lock)
        {
            if (_request == IntPtr.Zero && !TryCreateRequest())
            {
                return;
            }

            var succeeded = active
                ? PowerSetRequest(_request, PowerRequestSystemRequired)
                : PowerClearRequest(_request, PowerRequestSystemRequired);
            if (!succeeded)
            {
                _logger.LogWarning(new Win32Exception(Marshal.GetLastWin32Error()), "Couldn't change the download power request");
            }
        }
    }

    private bool TryCreateRequest()
    {
        var reason = Marshal.StringToHGlobalUni("Benny Box is downloading");
        try
        {
            var context = new ReasonContext
            {
                Version = PowerRequestContextVersion,
                Flags = PowerRequestContextSimpleString,
                SimpleReasonString = reason
            };

            var request = PowerCreateRequest(ref context);
            if (request == new IntPtr(-1))
            {
                _logger.LogWarning(new Win32Exception(Marshal.GetLastWin32Error()), "Couldn't create the download power request");
                return false;
            }

            _request = request;
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(reason);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ReasonContext
    {
        public uint Version;
        public uint Flags;
        public IntPtr SimpleReasonString;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr PowerCreateRequest(ref ReasonContext context);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerSetRequest(IntPtr request, int requestType);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PowerClearRequest(IntPtr request, int requestType);
}
