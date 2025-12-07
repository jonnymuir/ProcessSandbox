namespace LegacyLibrary;
using System.Diagnostics;
using LegacyLibrary.Contracts;

/// <summary>
/// A service that simulates instability by leaking memory
/// </summary>
public class UnstableService : IUnstableService
{
    // A static list that never gets cleared = Classic Memory Leak
    private static readonly List<byte[]> _memoryHog = new();

    /// <summary>
    /// Returns the current process ID and memory usage
    /// </summary>
    public ProcessInfo GetProcessInfo()
    {
        var process = Process.GetCurrentProcess();
        
        return new ProcessInfo()
        {
            ProcessId = process.Id,
            MemoryMB = process.WorkingSet64 / (1024.0 * 1024.0)
        };
    }

    /// <summary>
    /// Simulates a memory leak by allocating unmanaged memory
    /// </summary>
    /// <param name="megabytes"></param>
    public void LeakMemory(int megabytes)
    {
        // Allocate unmanaged memory to simulate a heavy leak
        var data = new byte[megabytes * 1024 * 1024];
        
        // Fill it so it actually commits to RAM
        new Random().NextBytes(data);
        
        // Add to static list so GC cannot collect it
        _memoryHog.Add(data); 
    }
}