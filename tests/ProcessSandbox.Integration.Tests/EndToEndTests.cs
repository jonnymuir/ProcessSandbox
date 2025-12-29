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
public class EndToEndTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _testAssemblyPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="EndToEndTests"/> class.
    /// </summary>
    public EndToEndTests()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            // Minimum level for logging (e.g., Information, Debug, or Trace)
            builder.SetMinimumLevel(LogLevel.Warning);

            // Add the Debug provider
            builder.AddDebug();
            builder.AddConsole();
        });

        _testAssemblyPath = typeof(TestServiceImpl).Assembly.Location;
    }

    /// <summary>
    /// Tests basic method invocation through the proxy.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Proxy_BasicMethodInvocation_ReturnsCorrectResult()
    {

        _loggerFactory.CreateLogger<EndToEndTests>().LogInformation("Starting Proxy_BasicMethodInvocation_ReturnsCorrectResult test.");

        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);
        var result = proxy.Echo("Hello");

        // Assert
        Assert.Equal("Hello", result);
    }

    /// <summary>
    /// Tests method with multiple parameters.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Proxy_MethodWithMultipleParameters_ReturnsCorrectResult()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);
        var result = proxy.Add(5, 3);

        // Assert
        Assert.Equal(8, result);
    }

    /// <summary>
    /// Tests void method invocation.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Proxy_VoidMethod_ExecutesWithoutError()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);
        proxy.DoNothing(); // Should not throw

        // Assert - no exception thrown
    }

    /// <summary>
    /// Tests exception propagation from worker to proxy.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Proxy_MethodThrowsException_PropagatesExceptionToProxy()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(ThrowingServiceImpl));

        // Act
        var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);

        // Assert
        var ex = Assert.Throws<Abstractions.RemoteInvocationException>(() => proxy.Echo("test"));
        Assert.Contains("Echo failed", ex.Message);
    }

    /// <summary>
    /// Tests concurrent method invocations.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Proxy_ConcurrentInvocations_HandlesCorrectly()
    {
        int numberOfTasks = 10;

        var logger = _loggerFactory.CreateLogger<EndToEndTests>();
        logger.LogInformation("Starting Proxy_ConcurrentInvocations_HandlesCorrectly test.");

        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));
        config.MaxPoolSize = 3;

        var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);

        logger.LogInformation("ProcessProxy created. Starting concurrent invocations.");

        // Act
        var tasks = new Task<int>[numberOfTasks];
        for (int i = 0; i < numberOfTasks; i++)
        {
            var a = i;
            var b = i + 1;
            logger.LogInformation("Starting task {TaskNumber} to add {A} and {B}.", i, a, b);
            tasks[i] = Task.Run(() => proxy.Add(a, b));
        }

        logger.LogInformation("All tasks started. Awaiting results.");

        var results = await Task.WhenAll(tasks);

        logger.LogInformation("All tasks completed.");
        // Assert
        for (int i = 0; i < numberOfTasks; i++)
        {
            Assert.Equal(i + i + 1, results[i]);
        }
    }

    /// <summary>
    /// Tests byte array parameter handling.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Proxy_ByteArrayParameter_HandlesCorrectly()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));
        var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);

        var input = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var result = proxy.ProcessBytes(input);

        // Assert
        Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, result);
    }

    /// <summary>
    /// Tests C# COM object with chained dependencies without registry.
    /// </summary>
    /// <returns></returns>
    [ProcessSandbox.Integration.Tests.WindowsFact]
    public async Task Proxy_CSharpComChained_WorksWithoutRegistry()
    {
        // Arrange
        var config = new ProcessPoolConfiguration
        {
            MinPoolSize = 1,
            MaxPoolSize = 1,
            // We use the current test assembly as the "host"
            ImplementationAssemblyPath = typeof(PrimaryService).Assembly.Location,
            ImplementationTypeName = typeof(PrimaryService).FullName!,
            ComClsid = new Guid("11111111-1111-1111-1111-111111111111"),
            // Flattened dependencies for the secondary object
            ExtraComDependencies =
            [
                new() {
                    DllPath = typeof(InternalEngine).Assembly.Location!,
                    Clsid = new Guid("22222222-2222-2222-2222-222222222222")
                }
            ],
            MaxMemoryMB = 512
        };

        // Act
        var proxy = await ProcessProxy.CreateAsync<IPrimaryService>(config, _loggerFactory);
        var result = proxy.GetCombinedReport();

        // Assert
        Assert.Equal("Primary reporting: C# Internal Engine Active", result);
    }

    private ProcessPoolConfiguration CreateTestConfiguration(Type implementationType)
    {
        return new ProcessPoolConfiguration
        {
            MinPoolSize = 1,
            MaxPoolSize = 5,
            ImplementationAssemblyPath = _testAssemblyPath,
            ImplementationTypeName = implementationType.FullName!,
            MaxMemoryMB = 512,
            MaxGdiHandles = 5000,
            ProcessRecycleThreshold = 100,
            VerboseWorkerLogging = false
        };
    }

    /// <summary>
    /// Disposes test resources.
    /// </summary>
    public void Dispose()
    {
        _loggerFactory?.Dispose();
    }
}