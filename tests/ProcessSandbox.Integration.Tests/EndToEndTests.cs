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
using System.Reflection.Metadata.Ecma335;
using System.IO.Pipelines;

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
            builder.SetMinimumLevel(LogLevel.Information);

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
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        string result = string.Empty;

        await factory.UseProxyAsync(async proxy =>
        {
            result = proxy.Echo("Hello");
        });

        // Assert
        Assert.Equal("Hello", result);
    }

    /// <summary>
    /// Tests basic method invocation through the proxy.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Proxy_BasicMethodInvocation_with_a_return()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        string result = await factory.UseProxyAsync<string>(async proxy =>
        {
            return proxy.Echo("Hello");
        });

        // Assert
        Assert.Equal("Hello", result);
    }

    /// <summary>
    /// Tests basic method invocation through the proxy.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Proxy_SequentialMethodInvocation()
    {

        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        string result = string.Empty;

        await factory.UseProxyAsync(async proxy =>
        {
            result = proxy.Echo("Hello");
            result = proxy.Echo("Hello2");
            result = proxy.Echo("Hello3");
        });

        // Assert
        Assert.Equal("Hello3", result);
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
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        int result = 0;

        await factory.UseProxyAsync(async proxy =>
        {
            result = proxy.Add(5, 3);
        });

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
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);
        await factory.UseProxyAsync(async proxy =>
        {
            proxy.DoNothing(); // Should not throw
        });
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
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        // Assert
        RemoteInvocationException ex = null!;
        
        await factory.UseProxyAsync(async proxy =>
        {
            ex = Assert.Throws<Abstractions.RemoteInvocationException>(() => proxy.Echo("test"));
        });

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

        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        logger.LogInformation("ProcessProxy created. Starting concurrent invocations.");

        // Act
        var tasks = new Task<int>[numberOfTasks];
        for (int i = 0; i < numberOfTasks; i++)
        {
            var a = i;
            var b = i + 1;
            logger.LogInformation("Starting task {TaskNumber} to add {A} and {B}.", i, a, b);
            tasks[i] = Task.Run(async () => 
            {
                int ret = 0;
                await factory.UseProxyAsync(async proxy => ret = proxy.Add(a, b));
                return ret;
            });
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
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        var input = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        byte[] result = null!;
        await factory.UseProxyAsync(async proxy => result = proxy.ProcessBytes(input));

        // Assert
        Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, result);
    }

    /// <summary>
    /// Tests using a factory to create the proxy.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task Proxy_keeps_the_same_object_reference_within_same_action_block()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        string result = string.Empty;

        await factory.UseProxyAsync(async proxy => {
            proxy.Set("Hello");
            result = proxy.Read();
        });

        // Assert
        Assert.Equal("Hello", result);
    }

    /// <summary>
    /// Tests using a factory to create the proxy.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task Proxy_doesnt_keeps_the_same_object_reference_between_action_blocks()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        await factory.UseProxyAsync(async proxy => {
            proxy.Set("Hello");
        });

        string result = await factory.UseProxyAsync(async proxy => {
            return proxy.Read();
        });

        // Assert
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Tests using a factory to create the proxy.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task Proxy_keeps_the_same_object_reference_within_same_lease()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        using var lease = await factory.AcquireLeaseAsync();
        lease.Set("Hello");
        string result = lease.Read();

        // Assert
        Assert.Equal("Hello", result);
    }

    /// <summary>
    /// Tests using a factory to create the proxy.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task Proxy_doesnt_keeps_the_same_object_reference_between_different_leases()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));

        // Act
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        using var lease1 = await factory.AcquireLeaseAsync();
        lease1.Set("Hello");

        using var lease2 = await factory.AcquireLeaseAsync();
        string result = lease2.Read();

        // Assert
        Assert.Equal(string.Empty, result);
    }

    /// <summary>
    /// Tests using a factory to create the proxy.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task Proxy_keeps_the_same_object_reference_between_action_blocks_if_new_instance_per_proxy_is_disabled()
    {
        // Arrange
        var config = CreateTestConfiguration(typeof(TestServiceImpl));
        config.MaxPoolSize = 1;
        config.NewInstancePerProxy = false;

        // Act
        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        await factory.UseProxyAsync(async proxy => {
            proxy.Set("Hello");
        });

        string result = await factory.UseProxyAsync(async proxy => {
            return proxy.Read();
        });

        // Assert
        Assert.Equal("Hello", result);
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
                    Clsid = new Guid("33333333-3333-3333-3333-333333333333")
                }
            ],
            MaxMemoryMB = 512
        };

        // Act
        var factory = await ProcessProxyFactory<IPrimaryService>.CreateAsync(config, _loggerFactory);
        string result = string.Empty;
        await factory.UseProxyAsync(async proxy => result = proxy.GetCombinedReport());;

        // Assert
        Assert.Equal("Success: C# Internal Engine Active", result);
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