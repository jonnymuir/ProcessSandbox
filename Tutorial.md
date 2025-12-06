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

# 5. Link projects to solution
dotnet sln add SandboxHost/SandboxHost.csproj
dotnet sln add LegacyLibrary/LegacyLibrary.csproj

# 6. Add reference: Host depends on Library (for the interface)
dotnet add SandboxHost/SandboxHost.csproj reference LegacyLibrary/LegacyLibrary.csproj

# 7. Install ProcessSandbox.Runner and Worker into the Host
dotnet add SandboxHost/SandboxHost.csproj package ProcessSandbox.Runner --prerelease
dotnet add SandboxHost/SandboxHost.csproj package ProcessSandbox.Worker --prerelease


# 8. Add Logging package
dotnet add SandboxHost/SandboxHost.csproj package Microsoft.Extensions.Logging.Console
```

### Phase 2: Create the "Bad" Code

We need code that behaves badly. Open `LegacyLibrary/Class1.cs` and replace it with this:

```csharp
namespace LegacyLibrary;

public interface IUnstableService
{
    int GetProcessId();
    void LeakMemory(int megabytes);
}

public class UnstableService : IUnstableService
{
    // A static list that never gets cleared = Classic Memory Leak
    private static readonly List<byte[]> _memoryHog = new();

    /// <summary>
    /// Returns the current process ID for debugging in the demo
    /// </summary>
    /// <returns></returns>
    public int GetProcessId()
    {
        return Environment.ProcessId;
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

### Phase 3: The Sandbox Host

Now, let's configure the host to watch this service. We will set a strict memory limit of **50MB**.

Open `SandboxHost/Program.cs`:

```csharp
using Microsoft.Extensions.Logging;
using ProcessSandbox.Pool;
using LegacyLibrary;
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
    ImplementationAssemblyPath = typeof(UnstableService).Assembly.Location,
    ImplementationTypeName = typeof(UnstableService).FullName!,
    
    // SAFETY LIMITS
    MaxMemoryMB = 50, // Recycle if it uses > 50MB
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
        var remotePid = proxy.GetProcessId(); 
        proxy.LeakMemory(10); // Leak 10MB per call
        
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"Success (Worker PID: {remotePid})");

        iteration++;
        await Task.Delay(1000); // Wait a second to watch the show
    }
    catch (Exception ex)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"❌ Error: {ex.Message}");
    }
}
```

### Phase 4: Run and Watch

1.  Open your terminal to the `ProcessSandboxDemo` folder.
2.  Run the host:
    ```bash
    dotnet run --project SandboxHost
    ```

### What you will see:

1.  **Calls 1-4:** You will see the `(Worker PID: 1234)` stay the same. Memory usage will climb: 10MB... 20MB... 30MB... 40MB.
2.  **The Trigger:** Once it hits \~50MB, the Sandbox policies kick in.
3.  **The Recycle:** You won't see a crash. You won't see an exception.
4.  **The Result:** Suddenly, on the next call, the **Worker PID will change** (e.g., from 1234 to 5678). The memory usage will drop back to near zero.

You have just successfully swapped out a corrupted process for a fresh one without stopping your application\!