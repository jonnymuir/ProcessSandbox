using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;
using ProcessSandbox.Tests.TestImplementations;
using ProcessSandbox.Abstractions;
using Microsoft.Extensions.Logging.Abstractions;

namespace ProcessSandbox.Tests.Integration;

/// <summary>
/// Integration tests for the complete ProcessSandbox system.
/// </summary>
public class LeakyTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _testAssemblyPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndToEndTests"/> class.
    /// </summary>
    public LeakyTests()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            // Minimum level for logging (e.g., Information, Debug, or Trace)
            builder.SetMinimumLevel(LogLevel.Debug);

            // Add the Debug provider
            builder.AddDebug();
            builder.AddConsole();
        });

        _testAssemblyPath = typeof(LeakyServiceImpl).Assembly.Location;
    }

    /// <summary>
    /// Tests basic method invocation through the proxy.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task MemoryLeakSequentialTest()
    {

        // Arrange
        var config = new ProcessPoolConfiguration
        {
            MinPoolSize = 1,
            MaxPoolSize = 1,
            ImplementationAssemblyPath = _testAssemblyPath,
            ImplementationTypeName = typeof(LeakyServiceImpl).FullName!,
            MaxMemoryMB = 512,
            MaxGdiHandles = 5000,
            ProcessRecycleThreshold = 0,
            VerboseWorkerLogging = false,
            RecycleCheckCalls = 10

        };

        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);
        int totalIterations = 0;
        // Act
        for (int i = 0; i < 100; i++)
        {
            await factory.UseProxyAsync(async proxy =>
            {
                string result = proxy.Echo("memoryleak:50");
                proxy.DoNothing();
            });
            // Assert
            totalIterations++;
        }

        Assert.Equal(100, totalIterations);
    }


    /// <summary>
    /// Tests basic method invocation through the proxy.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task MemoryLeakConcurrentTest()
    {

        // Arrange
        var config = new ProcessPoolConfiguration
        {
            MinPoolSize = 1,
            MaxPoolSize = 10,
            ImplementationAssemblyPath = _testAssemblyPath,
            ImplementationTypeName = typeof(LeakyServiceImpl).FullName!,
            MaxMemoryMB = 512,
            MaxGdiHandles = 5000,
            ProcessRecycleThreshold = 0,
            VerboseWorkerLogging = false,
            RecycleCheckCalls = 10

        };

        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);
        int totalIterations = 0;

        var tasks = new List<Task>();

        using var semaphore = new SemaphoreSlim(10);

        // Act
        for (int i = 0; i < 1000; i++)
        {
            await semaphore.WaitAsync();

            var task = Task.Run(async () =>
            {
                try
                {
                    await factory.UseProxyAsync(async proxy =>
                    {
                        if (proxy.Echo("memoryleak:50") == "memoryleak:50")
                        {
                            Interlocked.Increment(ref totalIterations);
                        }
                    });
                }
                finally
                {
                    semaphore.Release(); // Release the slot for the next iteration
                }
            });
            tasks.Add(task);

        }
        await Task.WhenAll(tasks);
        Assert.Equal(1000, totalIterations);
    }

    /// <summary>
    /// Tests process crash resilience.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task ProcessCrashResilienceTest()
    {
        // Arrange
        var config = new ProcessPoolConfiguration
        {
            MinPoolSize = 1,
            MaxPoolSize = 1,
            ImplementationAssemblyPath = _testAssemblyPath,
            ImplementationTypeName = typeof(LeakyServiceImpl).FullName!,
        };

        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        // 1. Verify the crash is caught and reported correctly
        // We expect a RemoteInvocationException (or your specific WorkerCrashedException)
        var exception = await Assert.ThrowsAsync<WorkerCrashedException>(async () =>
        {
            // This call will trigger the 'unsafe' crash in the worker
            await factory.UseProxyAsync(async proxy => proxy.Echo("crash"));
        });

        // Check if the message indicates a process failure rather than a logical app error
        Assert.Contains("terminated", exception.Message.ToLower());

        // 2. Verify Self-Healing: The next call should work!
        // The Pool should have detected the exit of the previous process and give us a fresh one.
        string recoveryResult = string.Empty;
        
        await factory.UseProxyAsync(async proxy => recoveryResult = proxy.Echo("I am alive"));

        Assert.Equal("I am alive", recoveryResult);
    }


    /// <summary>
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }
}