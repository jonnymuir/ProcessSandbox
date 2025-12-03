using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MessagePack;
using ProcessSandbox.Worker;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandbox.Tests.TestImplementations;

namespace ProcessSandbox.Tests.Worker;

/// <summary>
/// Tests for the HealthReporter class.
/// </summary>
public class HealthReporterTests
{
    /// <summary>
    /// Increments the call count and verifies it is reported correctly.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task IncrementCallCount_IncrementsCounter()
    {
        // Arrange
        var pipeName = Guid.NewGuid().ToString();
        using var server = new ProcessSandbox.IPC.NamedPipeServerChannel(pipeName);
        using var client = new ProcessSandbox.IPC.NamedPipeClientChannel(pipeName);

        var connectTask = server.WaitForConnectionAsync();
        await Task.Delay(50);
        await client.ConnectAsync();
        await connectTask;

        using var reporter = new HealthReporter(
            server,
            intervalMs: 10000, // Long interval so it doesn't auto-report during test
            NullLogger<HealthReporter>.Instance);

        // Act
        reporter.IncrementCallCount();
        reporter.IncrementCallCount();
        reporter.IncrementCallCount();

        // Send a health report
        await reporter.SendHealthReportAsync();

        // Receive on client side
        var message = await client.ReceiveMessageAsync();

        // Assert
        Assert.NotNull(message);
        Assert.Equal(MessageType.HealthReport, message.MessageType);

        var report = message.GetHealthReport();
        Assert.Equal(3, report.CallCount);
        Assert.True(report.WorkingSetBytes > 0);
    }

    /// <summary>
    /// Sends a health report and verifies the contents.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task SendHealthReportAsync_SendsValidReport()
    {
        // Arrange
        var pipeName = Guid.NewGuid().ToString();
        using var server = new ProcessSandbox.IPC.NamedPipeServerChannel(pipeName);
        using var client = new ProcessSandbox.IPC.NamedPipeClientChannel(pipeName);

        var connectTask = server.WaitForConnectionAsync();
        await Task.Delay(50);
        await client.ConnectAsync();
        await connectTask;

        using var reporter = new HealthReporter(
            server,
            intervalMs: 10000,
            NullLogger<HealthReporter>.Instance);

        // Act
        await reporter.SendHealthReportAsync();
        var message = await client.ReceiveMessageAsync();

        // Assert
        Assert.NotNull(message);
        var report = message.GetHealthReport();
        Assert.True(report.WorkingSetBytes > 0);
        Assert.True(report.HandleCount > 0);
        Assert.Equal(0, report.CallCount); // No calls yet
    }
}