using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;
using ProcessSandbox.Tests.TestImplementations;
using Microsoft.Extensions.Logging.Abstractions;

namespace ProcessSandbox.Tests.Integration;

/// <summary>
/// Tests for process pool statistics.
/// </summary>
public class ProcessPoolStatisticsTests
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly string _testAssemblyPath;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessPoolStatisticsTests"/> class.
    /// </summary>
    public ProcessPoolStatisticsTests()
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
    /// Tests that pool statistics are tracked correctly.
    /// </summary>
    /// <returns>A task representing the asynchronous operation.</returns>
    [Fact]
    public async Task ProcessPool_TracksStatistics_Correctly()
    {
        // Arrange
        var config = new ProcessPoolConfiguration
        {
            MinPoolSize = 2,
            MaxPoolSize = 3,
            ImplementationAssemblyPath = _testAssemblyPath,
            ImplementationTypeName = typeof(TestServiceImpl).FullName!
        };

        var pool = new ProcessPool(config, _loggerFactory);
        await pool.InitializeAsync();

        // Act
        var stats1 = pool.GetStatistics();
        
        // Make some calls
        var invocation = new Abstractions.Messages.MethodInvocationMessage(
            Guid.NewGuid(),
            "Echo",
            10000)
        {
            ParameterTypeNames = new[] { typeof(string).AssemblyQualifiedName! },
            SerializedParameters = Abstractions.SerializationHelper.SerializeParameters(new object[] { "test" })
        };

        var worker = await pool.GetAvailableWorkerAsync();
        await worker.InvokeMethodAsync(invocation);
        pool.ReturnWorker(worker);

        worker = await pool.GetAvailableWorkerAsync();
        await worker.InvokeMethodAsync(invocation);
        pool.ReturnWorker(worker);

        var stats2 = pool.GetStatistics();

        // Assert
        Assert.Equal(2, stats1.TotalWorkers);
        Assert.Equal(2, stats1.HealthyWorkers);
        Assert.Equal(0, stats1.TotalCalls);

        Assert.Equal(2, stats2.TotalWorkers);
        Assert.Equal(2, stats2.HealthyWorkers);
        Assert.Equal(2, stats2.TotalCalls);

        pool.Dispose();
    }
}