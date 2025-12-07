using System;
using MessagePack;

namespace LegacyLibrary.Contracts;

/// <summary>
/// Contains key information about the worker process (for demo/debugging)
/// </summary>
[MessagePackObject]
public class ProcessInfo
{
    /// <summary>
    /// The ID of the worker process
    /// </summary>
    [Key(0)]
    public int ProcessId { get; set; }

    /// <summary>
    /// The memory usage of the worker process in megabytes
    /// </summary>
    [Key(1)]
    public double MemoryMB { get; set; }
};

/// <summary>
/// A service that simulates instability by leaking memory
/// </summary>
public interface IUnstableService
{
    /// <summary>
    /// Returns the current process Info for debugging in the demo
    /// </summary>
    /// <returns></returns>
    ProcessInfo GetProcessInfo();
    /// <summary>
    /// Simulates a memory leak by allocating unmanaged memory
    /// </summary>
    /// <param name="megabytes"></param>
    void LeakMemory(int megabytes);
}
