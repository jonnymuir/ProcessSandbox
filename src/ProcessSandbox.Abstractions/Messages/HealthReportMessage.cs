using System;
using MessagePack;

namespace ProcessSandbox.Abstractions.Messages;

/// <summary>
/// Represents resource usage and health metrics from a worker process.
/// </summary>
[MessagePackObject]
public class HealthReportMessage
{
    /// <summary>
    /// Physical memory usage in bytes (Working Set).
    /// </summary>
    [Key(0)]
    public long WorkingSetBytes { get; set; }

    /// <summary>
    /// Private memory usage in bytes (committed memory).
    /// </summary>
    [Key(1)]
    public long PrivateBytes { get; set; }

    /// <summary>
    /// Number of GDI objects currently allocated.
    /// </summary>
    [Key(2)]
    public int GdiObjects { get; set; }

    /// <summary>
    /// Number of USER objects currently allocated.
    /// </summary>
    [Key(3)]
    public int UserObjects { get; set; }

    /// <summary>
    /// Total number of handles currently open.
    /// </summary>
    [Key(4)]
    public int HandleCount { get; set; }

    /// <summary>
    /// Number of method calls processed since worker started.
    /// </summary>
    [Key(5)]
    public int CallCount { get; set; }

    /// <summary>
    /// Time when the worker process started.
    /// </summary>
    [Key(6)]
    public DateTime StartTime { get; set; }

    /// <summary>
    /// Total processor time used by the process.
    /// </summary>
    [Key(7)]
    public TimeSpan TotalProcessorTime { get; set; }

    /// <summary>
    /// Gets the uptime of the worker process.
    /// </summary>
    [IgnoreMember]
    public TimeSpan Uptime => DateTime.UtcNow - StartTime;

    /// <summary>
    /// Gets working set in megabytes for easier readability.
    /// </summary>
    [IgnoreMember]
    public double WorkingSetMB => WorkingSetBytes / (1024.0 * 1024.0);

    /// <summary>
    /// Gets private bytes in megabytes for easier readability.
    /// </summary>
    [IgnoreMember]
    public double PrivateBytesMB => PrivateBytes / (1024.0 * 1024.0);

    /// <summary>
    /// Creates a health report from the current process.
    /// </summary>
    /// <param name="callCount">Number of calls processed.</param>
    /// <param name="startTime">When the process started.</param>
    /// <returns>A new health report message.</returns>
    public static HealthReportMessage FromCurrentProcess(int callCount, DateTime startTime)
    {
        var process = System.Diagnostics.Process.GetCurrentProcess();

        return new HealthReportMessage
        {
            WorkingSetBytes = process.WorkingSet64,
            PrivateBytes = process.PrivateMemorySize64,
            GdiObjects = GetGdiObjectCount(process),
            UserObjects = GetUserObjectCount(process),
            HandleCount = process.HandleCount,
            CallCount = callCount,
            StartTime = startTime,
            TotalProcessorTime = process.TotalProcessorTime
        };
    }

    private static int GetGdiObjectCount(System.Diagnostics.Process process)
    {
        try
        {
            // P/Invoke to GetGuiResources - will implement in helper class
            return NativeMethods.GetGuiResources(process.Handle, 0); // GR_GDIOBJECTS = 0
        }
        catch
        {
            return 0;
        }
    }

    private static int GetUserObjectCount(System.Diagnostics.Process process)
    {
        try
        {
            // P/Invoke to GetGuiResources - will implement in helper class
            return NativeMethods.GetGuiResources(process.Handle, 1); // GR_USEROBJECTS = 1
        }
        catch
        {
            return 0;
        }
    }
}

/// <summary>
/// Native methods for retrieving GDI/USER object counts.
/// </summary>
internal static class NativeMethods
{
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
}