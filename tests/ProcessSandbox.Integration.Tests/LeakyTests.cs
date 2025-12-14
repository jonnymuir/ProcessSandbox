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
            MethodCallTimeout = TimeSpan.FromSeconds(15),
            ProcessStartTimeout = TimeSpan.FromSeconds(15),
            VerboseWorkerLogging = false,
            RecycleAfterCalls = 10

        };

        var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);
        int totalIterations = 0;
        // Act
        for (int i = 0; i < 100; i++)
        {
            string result = proxy.Echo("memoryleak:50");
            proxy.DoNothing();
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
            MethodCallTimeout = TimeSpan.FromSeconds(15),
            ProcessStartTimeout = TimeSpan.FromSeconds(15),
            VerboseWorkerLogging = false,
            RecycleAfterCalls = 10

        };

        var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);
        int totalIterations = 0;

        var tasks = new List<Task>();

        using var semaphore = new SemaphoreSlim(10);

        // Act
        for (int i = 0; i < 1000; i++)
        {
            await semaphore.WaitAsync();

            var task = Task.Run(() =>
            {
                try
                {
                    if(proxy.Echo("memoryleak:50")=="memoryleak:50")
                    {
                        Interlocked.Increment(ref totalIterations);
                    }
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
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }
}