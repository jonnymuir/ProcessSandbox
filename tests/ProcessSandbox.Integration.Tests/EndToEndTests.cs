using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;
using ProcessSandbox.Tests.TestImplementations;

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
            builder.AddConsole();
            builder.SetMinimumLevel(LogLevel.Information);
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
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        using var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);
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
        using var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);
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
        using var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);
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
        using var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);
        
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
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));
        config.MaxPoolSize = 3;

        using var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);

        // Act
        var tasks = new Task<int>[10];
        for (int i = 0; i < 10; i++)
        {
            var a = i;
            var b = i + 1;
            tasks[i] = Task.Run(() => proxy.Add(a, b));
        }

        var results = await Task.WhenAll(tasks);

        // Assert
        for (int i = 0; i < 10; i++)
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
        using var proxy = await ProcessProxy.CreateAsync<ITestService>(config, _loggerFactory);

        var input = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var result = proxy.ProcessBytes(input);

        // Assert
        Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, result);
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
            MethodCallTimeout = TimeSpan.FromSeconds(10),
            ProcessStartTimeout = TimeSpan.FromSeconds(10),
            HealthCheckInterval = TimeSpan.FromSeconds(5),
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