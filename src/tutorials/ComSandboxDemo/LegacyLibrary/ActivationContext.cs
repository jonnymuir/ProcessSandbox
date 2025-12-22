using System;
using System.Runtime.InteropServices;

namespace LegacyLibrary;

/// <summary>
/// Manages a Windows Activation Context for COM manifest activation
/// </summary>
public class ActivationContext : IDisposable
{
    private struct ACTCTX
    {
        public int cbSize;
        public uint dwFlags;
        public string lpSource;
        public ushort wProcessorArchitecture;
        public ushort wLangId;
        public string lpAssemblyDirectory;
        public string lpResourceName;
        public string lpApplicationName;
        public IntPtr hModule;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateActCtx(ref ACTCTX actctx);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ActivateActCtx(IntPtr hActCtx, out IntPtr lpCookie);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeactivateActCtx(uint dwFlags, IntPtr ulCookie);

    private IntPtr _hActCtx = IntPtr.Zero;
    private IntPtr _cookie = IntPtr.Zero;

    /// <summary>
    /// Creates and activates an Activation Context from the given manifest path
    /// </summary>
    /// <param name="manifestPath"></param>
    /// <exception cref="Exception"></exception>
    public ActivationContext(string manifestPath)
    {
        var context = new ACTCTX {
            cbSize = Marshal.SizeOf(typeof(ACTCTX)),
            lpSource = manifestPath,
            dwFlags = 0 // ACTCTX_FLAG_PROCESSOR_ARCHITECTURE_VALID not needed for simple path
        };

        _hActCtx = CreateActCtx(ref context);
        if (_hActCtx == new IntPtr(-1)) 
            throw new Exception($"Failed to create Activation Context for {manifestPath}. Error: {Marshal.GetLastWin32Error()}");

        if (!ActivateActCtx(_hActCtx, out _cookie))
            throw new Exception("Failed to activate Activation Context.");
    }

    /// <summary>
    /// Disposes the Activation Context
    /// </summary>
    public void Dispose()
    {
        if (_cookie != IntPtr.Zero) DeactivateActCtx(0, _cookie);
    }
}