# Tutorials

We will bring this to life by showing how to create from scratch a tutorial showing a few scenarios and outputing some statistics as it goes along

I am using VSCode on a macbook, but this should work nicely anywhere as long as you have dotnet installed.

## 🚀 Getting Started: The "Crash Test" Tutorial

In this guide, we will create a **Memory Leaker**—a service that intentionally consumes memory until it crashes. We will verify that `ProcessSandbox.Runner` detects the leak and recycles the worker without crashing your main app.

### Phase 1: Project Setup (VS Code Terminal)

Open your terminal in VS Code and run these commands to set up a clean solution structure:

```bash
# 1. Create the folder structure
mkdir ProcessSandboxDemo
cd ProcessSandboxDemo

# 2. Create the solution
dotnet new sln

# 3. Create the "Host" app (Your main application)
dotnet new console -n SandboxHost

# 4. Create the "Library" (The code we want to isolate)
dotnet new classlib -n LegacyLibrary

# 5. Create the contracts (The interface and types we need to share). Note netstandard2.0 is unneccessary here, but you might want it in the future for maximum compatability.
dotnet new classlib -n Contracts -f netstandard2.0

# 6. Link projects to solution
dotnet sln add SandboxHost/SandboxHost.csproj
dotnet sln add LegacyLibrary/LegacyLibrary.csproj
dotnet sln add Contracts/Contracts.csproj

# 7. Add reference to contracts
dotnet add SandboxHost/SandboxHost.csproj reference Contracts/Contracts.csproj
dotnet add LegacyLibrary/LegacyLibrary.csproj reference Contracts/Contracts.csproj

# 8. Install ProcessSandbox.Runner
dotnet add SandboxHost/SandboxHost.csproj package ProcessSandbox.Runner --prerelease

# 9. Add Logging package
dotnet add SandboxHost/SandboxHost.csproj package Microsoft.Extensions.Logging.Console

# 10. Install ProcessSandbox.Abstractions into Contracts (So we can use the right version of messagepack)
dotnet add Contracts/Contracts.csproj package ProcessSandbox.Abstractions --prerelease

```

### Phase 2: Create the contracts

Open Contracts and replace with this:

```csharp
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
```

### Phase 3: Create the "Bad" Code

We need code that behaves badly. Open `LegacyLibrary/Class1.cs` and replace it with this:

```csharp
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
```

### Phase 4: The Sandbox Host

Now, let's configure the host to watch this service. We will set a strict memory limit of **500MB**.

Open `SandboxHost/Program.cs`:

```csharp
using Microsoft.Extensions.Logging;
using ProcessSandbox.Pool;
using LegacyLibrary.Contracts;
using ProcessSandbox.Proxy;

// 1. Setup minimal logging to see Sandbox internals
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning); // Only show warnings/errors from the pool
});

// 2. Configure the Pool
var config = new ProcessPoolConfiguration
{
    MinPoolSize = 1,
    MaxPoolSize = 1, // Keep it simple: 1 worker
    
    // The Critical Part: Point to our separate DLL
    // Note the replace of SandboxHost with LegacyLibrary in this instance just to get the right path
    // In your real application, you'll probably want to package legecy code separately and use configuration appropriately
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "LegacyLibrary.dll").Replace("SandboxHost", "LegacyLibrary"),
    ImplementationTypeName = "LegacyLibrary.UnstableService", // The actual type
    
    // SAFETY LIMITS
    MaxMemoryMB = 500, // Recycle if it uses > 500MB
    ProcessRecycleThreshold = 0 // Disable call-count recycling (rely on memory)
};

Console.WriteLine("--- 🛡️ Starting ProcessSandbox Monitor ---");
Console.WriteLine($"Policy: Max Memory = {config.MaxMemoryMB}MB");

// 3. Create the Proxy
var proxy = await ProcessProxy.CreateAsync<IUnstableService>(config, loggerFactory);

// 4. Run the Simulation Loop
var iteration = 1;

while (true)
{
    try
    {
        // A. Invoke the bad code
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.Write($"\n[Call #{iteration}] Sending request... ");
        
        // This call happens in the worker process
        proxy.LeakMemory(10); // Leak 10MB per call

        var info = proxy.GetProcessInfo(); 
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Success (Worker PID: {info.ProcessId}, Used: {info.MemoryMB}MB)");
        iteration++;
        await Task.Delay(100); // Wait a bit to watch the show
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ Error: {ex.Message}");
    }
}
```

### Phase 5: Run and Watch

1.  Open your terminal to the `ProcessSandboxDemo` folder.
2.  Run the host (make sure we have build the LegacyLibrary first) with dotnet build
    ```bash
    dotnet build
    dotnet run --project SandboxHost
    ```

### What you will see:

1.  **Initial Calls:** You will see the `(Worker PID: 1234)` stay the same. Memory usage will climb: 10MB... 20MB... 30MB... 40MB.
2.  **The Trigger:** Once it hits \~500MB, the Sandbox policies kick in.
3.  **The Recycle:** You won't see a crash. You won't see an exception.
4.  **The Result:** Suddenly, on the next call, the **Worker PID will change** (e.g., from 1234 to 5678). The memory usage will drop back to near zero.

You have just successfully swapped out a corrupted process for a fresh one without stopping your application\!