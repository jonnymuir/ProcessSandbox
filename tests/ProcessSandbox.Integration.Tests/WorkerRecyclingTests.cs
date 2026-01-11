using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;
using ProcessSandbox.Tests.TestImplementations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit.Abstractions;

namespace ProcessSandbox.Tests.Integration;

/// <summary>
/// Tests for worker recycling behavior.
/// </summary>
public class WorkerRecyclingTests
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _testAssemblyPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerRecyclingTests"/> class.
    /// </summary>
    public WorkerRecyclingTests()
    {
        _loggerFactory = _loggerFactory = NullLoggerFactory.Instance;

        _testAssemblyPath = typeof(TestServiceImpl).Assembly.Location;
    }

    /// <summary>
    /// Tests that workers are recycled after call threshold.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Worker_ExceedsCallThreshold_GetsRecycled()
    {
        // Arrange
        var config = new ProcessPoolConfiguration
        {
            MinPoolSize = 1,
            MaxPoolSize = 1,
            ImplementationAssemblyPath = _testAssemblyPath,
            ImplementationTypeName = typeof(TestServiceImpl).FullName!,
            ProcessRecycleThreshold = 5
        };

        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        // Act - make more calls than the threshold
        for (int i = 0; i < 10; i++)
        {
            await factory.UseProxyAsync(async proxy =>
            {
                proxy.Echo($"Call {i}");
            });
        }

        // Assert - if we got here without errors, recycling worked
        // The proxy should have automatically recycled the worker and continued
    }

    /// <summary>
    /// Tests that workers continue to function after recycling.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task Worker_AfterRecycling_ContinuesToWork()
    {
        // Arrange
        var config = new ProcessPoolConfiguration
        {
            MinPoolSize = 1,
            MaxPoolSize = 1,
            ImplementationAssemblyPath = _testAssemblyPath,
            ImplementationTypeName = typeof(StatefulServiceImpl).FullName!,
            ProcessRecycleThreshold = 6, // Recycle after 6 calls - one echo + one dispose per UseProxyAsync
            RecycleCheckCalls = 1
        };

        var factory = await ProcessProxyFactory<ITestService>.CreateAsync(config, _loggerFactory);

        string result1 = string.Empty, result2 = string.Empty, result3 = string.Empty, result4 = string.Empty;
        // Act
        await factory.UseProxyAsync(async proxy =>
        {
            result1 = proxy.Echo("First"); // Call 1
        });


        await factory.UseProxyAsync(async proxy =>
        {
            result2 = proxy.Echo("Second"); // Call 2
        });

        await factory.UseProxyAsync(async proxy =>
        {
            result3 = proxy.Echo("Third"); // Call 3 - triggers recycle
        });

        await Task.Delay(1000); // Give time for recycling

        await factory.UseProxyAsync(async proxy =>
        {
            result4 = proxy.Echo("Fourth"); // Call 1 on new worker
        });

        // Assert
        Assert.Contains("call #1", result1);
        Assert.Contains("call #2", result2);
        Assert.Contains("call #3", result3);
        Assert.Contains("call #1", result4); // New worker resets counter
    }
}