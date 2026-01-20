using Microsoft.Extensions.Logging;
using ProcessSandbox.Pool;
using LegacyLibrary.Contracts;
using ProcessSandbox.Proxy;
using System.Diagnostics;

// 1. Setup minimal logging to see Sandbox internals
using var loggerFactory = LoggerFactory.Create(builder =>
{
    builder.AddConsole();
    builder.SetMinimumLevel(LogLevel.Warning); // Only show warnings/errors from the pool
});

// 2. Configure the Pool
var config = new ProcessPoolConfiguration
{
    MinPoolSize = 2,
    MaxPoolSize = 2, // Keep it simple: 1 worker
    
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

// 3. Create the Proxy Factory
var factory = await ProcessProxyFactory<IUnstableService>.CreateAsync(config, loggerFactory);

// 4. Run the Simulation Loop
var iteration = 1;

while (true)
{
    // A. Invoke the bad code
    Console.ForegroundColor = ConsoleColor.Cyan;
    Console.Write($"\n[Call #{iteration}] Sending request... ");
    
    // This shows the closure pattern:
    var info = await factory.UseProxyAsync<ProcessInfo>(async proxy =>
    {
        // Leak Memory and Get Process Info will be sent to the same proxy in the prcess worker
        proxy.LeakMemory(10); // Leak 10MB per call

        return proxy.GetProcessInfo(); 
    });

    Console.ForegroundColor = ConsoleColor.Green;
    Console.WriteLine($"Success from the proxy (Worker PID: {info.ProcessId}, Used: {info.MemoryMB}MB)");

    // Or if you prefer the lease pattern - it is your responsibility to Dispose the lease:
    using var lease = await factory.AcquireLeaseAsync();
    lease.LeakMemory(10);
    info = lease.GetProcessInfo();
    Console.WriteLine($"Success from the lease (Worker PID: {info.ProcessId}, Used: {info.MemoryMB}MB)");

    iteration++;
}