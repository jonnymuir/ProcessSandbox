using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandbox.IPC;

namespace ProcessSandbox.Worker;

/// <summary>
/// Periodically reports health metrics to the parent process.
/// </summary>
public class HealthReporter : IDisposable
{
    private readonly IIpcChannel _channel;
    private readonly int _intervalMs;
    private readonly ILogger<HealthReporter> _logger;
    private readonly DateTime _startTime;
    private readonly CancellationTokenSource _cts;
    private readonly Task _reporterTask;
    private int _callCount;
    private bool _disposed;

    /// <summary>
    /// Creates a new health reporter.
    /// </summary>
    /// <param name="channel"></param>
    /// <param name="intervalMs"></param>
    /// <param name="logger"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public HealthReporter(
        IIpcChannel channel,
        int intervalMs,
        ILogger<HealthReporter> logger)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _intervalMs = intervalMs;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _startTime = DateTime.UtcNow;
        _cts = new CancellationTokenSource();
        _callCount = 0;

        // Start background reporting task
        _reporterTask = Task.Run(ReporterLoop);
    }

    /// <summary>
    /// Increments the call counter.
    /// </summary>
    public void IncrementCallCount()
    {
        Interlocked.Increment(ref _callCount);
    }

    /// <summary>
    /// Sends a health report immediately.
    /// </summary>
    public async Task SendHealthReportAsync()
    {
        if (_disposed)
            return;

        try
        {
            var report = HealthReportMessage.FromCurrentProcess(_callCount, _startTime);
            var message = IpcMessage.FromHealthReport(report);

            await _channel.SendMessageAsync(message, _cts.Token).ConfigureAwait(false);

            _logger.LogDebug(
                "Health report sent: Memory={MemoryMB:F1}MB, GDI={GDI}, Handles={Handles}, Calls={Calls}",
                report.WorkingSetMB,
                report.GdiObjects,
                report.HandleCount,
                report.CallCount);
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send health report");
        }
    }

    private async Task ReporterLoop()
    {
        _logger.LogInformation(
            "Health reporter started (interval: {IntervalMs}ms)",
            _intervalMs);

        try
        {
            while (!_cts.Token.IsCancellationRequested)
            {
                await Task.Delay(_intervalMs, _cts.Token).ConfigureAwait(false);
                await SendHealthReportAsync().ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected during shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health reporter loop failed");
        }

        _logger.LogInformation("Health reporter stopped");
    }

    /// <summary>
    /// Disposes the health reporter.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _cts?.Cancel();
        
        try
        {
            _reporterTask?.Wait(1000);
        }
        catch
        {
            // Ignore
        }

        _cts?.Dispose();
    }
}