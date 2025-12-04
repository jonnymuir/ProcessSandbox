using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandbox.IPC;

namespace ProcessSandbox.Pool;

/// <summary>
/// Represents a single worker process in the pool.
/// </summary>
public class WorkerProcess : IDisposable
{
    private readonly ProcessPoolConfiguration _config;
    private readonly ILogger<WorkerProcess> _logger;
    private readonly string _workerId;
    private readonly DateTime _startTime;
    
    private Process? _process;
    private RequestResponseChannel? _channel;
    private HealthReportMessage? _lastHealthReport;
    private int _callCount;
    private bool _disposed;
    private readonly SemaphoreSlim _usageLock;

    /// <summary>
    /// Gets the unique identifier for this worker.
    /// </summary>
    public string WorkerId => _workerId;

    /// <summary>
    /// Gets a value indicating whether the worker is running and connected.
    /// </summary>
    public bool IsHealthy => _process != null && !_process.HasExited && _channel?.IsConnected == true;

    /// <summary>
    /// Gets a value indicating whether the worker is currently processing a request.
    /// </summary>
    public bool IsBusy { get; private set; }

    /// <summary>
    /// Gets the process ID of the worker process.
    /// </summary>
    public int ProcessId => _process?.Id ?? 0;

    /// <summary>
    /// Gets the number of calls processed by this worker.
    /// </summary>
    public int CallCount => _callCount;

    /// <summary>
    /// Gets the time when this worker was started.
    /// </summary>
    public DateTime StartTime => _startTime;

    /// <summary>
    /// Gets the last health report received from the worker.
    /// </summary>
    public HealthReportMessage? LastHealthReport => _lastHealthReport;

    /// <summary>
    /// Event raised when the worker process exits or becomes unhealthy.
    /// </summary>
    public event EventHandler<WorkerFailedEventArgs>? Failed;

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerProcess"/> class.
    /// </summary>
    /// <param name="config">The process pool configuration.</param>
    /// <param name="logger">The logger instance.</param>
    public WorkerProcess(ProcessPoolConfiguration config, ILogger<WorkerProcess> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _workerId = Guid.NewGuid().ToString("N");
        _startTime = DateTime.UtcNow;
        _usageLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// Starts the worker process and establishes communication.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when the worker is ready.</returns>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerProcess));

        if (_process != null)
            throw new InvalidOperationException("Worker already started");

        _logger.LogInformation("Starting worker process {WorkerId}", _workerId);

        try
        {
            // Generate unique pipe name
            var pipeName = PipeNameGenerator.Generate();

            // Create worker configuration
            var workerConfig = new WorkerConfiguration
            {
                AssemblyPath = _config.ImplementationAssemblyPath,
                TypeName = _config.ImplementationTypeName,
                PipeName = pipeName,
                HealthReportIntervalMs = (int)_config.HealthCheckInterval.TotalMilliseconds,
                VerboseLogging = _config.VerboseWorkerLogging,
                ParentProcessId = Environment.ProcessId
            };

            // Serialize configuration to base64
            var configBytes = MessagePack.MessagePackSerializer.Serialize(workerConfig);
            var configBase64 = Convert.ToBase64String(configBytes);

            // Determine worker executable path
            var workerExePath = _config.WorkerExecutablePath 
                ?? GetDefaultWorkerExecutablePath();

            // Start the process
            var startInfo = new ProcessStartInfo
            {
                FileName = workerExePath,
                Arguments = $"--config {configBase64}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            _process = new Process { StartInfo = startInfo };
            _process.Exited += OnProcessExited;
            _process.EnableRaisingEvents = true;

            // Capture output for logging
            _process.OutputDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogDebug("[Worker {WorkerId}] {Output}", _workerId, e.Data);
            };

            _process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                    _logger.LogWarning("[Worker {WorkerId}] {Error}", _workerId, e.Data);
            };

            if (!_process.Start())
                throw new WorkerStartupException("Failed to start worker process");

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            _logger.LogInformation(
                "Worker process started: {WorkerId}, PID: {ProcessId}",
                _workerId,
                _process.Id);

            // Connect to the worker via named pipe
            var client = new NamedPipeClientChannel(pipeName);
            
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(_config.ProcessStartTimeout);

            await client.ConnectAsync(
                (int)_config.ProcessStartTimeout.TotalMilliseconds,
                timeoutCts.Token);

            _channel = new RequestResponseChannel(client);
            _channel.Disconnected += OnChannelDisconnected;

            _logger.LogInformation("Worker {WorkerId} connected and ready", _workerId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start worker {WorkerId}", _workerId);
            Cleanup();
            throw new WorkerStartupException($"Failed to start worker {_workerId}", ex);
        }
    }

    /// <summary>
    /// Invokes a method on the worker process.
    /// </summary>
    /// <param name="invocation">The method invocation details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The method result.</returns>
    public async Task<MethodResultMessage> InvokeMethodAsync(
        MethodInvocationMessage invocation,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerProcess));

        if (!IsHealthy)
            throw new WorkerCrashedException("Worker is not healthy");

        await _usageLock.WaitAsync(cancellationToken);
        try
        {
            IsBusy = true;

            var result = await _channel!.SendRequestAsync(invocation, cancellationToken);

            Interlocked.Increment(ref _callCount);

            return result;
        }
        finally
        {
            IsBusy = false;
            _usageLock.Release();
        }
    }

    /// <summary>
    /// Updates the health report for this worker.
    /// </summary>
    /// <param name="report">The health report.</param>
    public void UpdateHealthReport(HealthReportMessage report)
    {
        _lastHealthReport = report;
    }

    /// <summary>
    /// Checks if the worker should be recycled based on configured thresholds.
    /// </summary>
    /// <returns>True if the worker should be recycled; otherwise, false.</returns>
    public bool ShouldRecycle()
    {
        // Check call count threshold
        if (_config.ProcessRecycleThreshold > 0 && _callCount >= _config.ProcessRecycleThreshold)
        {
            _logger.LogInformation(
                "Worker {WorkerId} should recycle: call count {CallCount} >= {Threshold}",
                _workerId,
                _callCount,
                _config.ProcessRecycleThreshold);
            return true;
        }

        // Check lifetime
        var uptime = DateTime.UtcNow - _startTime;
        if (uptime >= _config.MaxProcessLifetime)
        {
            _logger.LogInformation(
                "Worker {WorkerId} should recycle: uptime {Uptime} >= {MaxLifetime}",
                _workerId,
                uptime,
                _config.MaxProcessLifetime);
            return true;
        }

        // Check health report thresholds
        if (_lastHealthReport != null)
        {
            if (_lastHealthReport.WorkingSetMB > _config.MaxMemoryMB)
            {
                _logger.LogInformation(
                    "Worker {WorkerId} should recycle: memory {Memory}MB > {MaxMemory}MB",
                    _workerId,
                    _lastHealthReport.WorkingSetMB,
                    _config.MaxMemoryMB);
                return true;
            }

            if (_lastHealthReport.GdiObjects > _config.MaxGdiHandles)
            {
                _logger.LogInformation(
                    "Worker {WorkerId} should recycle: GDI handles {GdiHandles} > {MaxGdiHandles}",
                    _workerId,
                    _lastHealthReport.GdiObjects,
                    _config.MaxGdiHandles);
                return true;
            }

            if (_lastHealthReport.UserObjects > _config.MaxUserHandles)
            {
                _logger.LogInformation(
                    "Worker {WorkerId} should recycle: USER handles {UserHandles} > {MaxUserHandles}",
                    _workerId,
                    _lastHealthReport.UserObjects,
                    _config.MaxUserHandles);
                return true;
            }

            if (_lastHealthReport.HandleCount > _config.MaxTotalHandles)
            {
                _logger.LogInformation(
                    "Worker {WorkerId} should recycle: total handles {TotalHandles} > {MaxTotalHandles}",
                    _workerId,
                    _lastHealthReport.HandleCount,
                    _config.MaxTotalHandles);
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Gracefully stops the worker process.
    /// </summary>
    /// <returns>A task that completes when the worker is stopped.</returns>
    public async Task StopAsync()
    {
        if (_disposed)
            return;

        _logger.LogInformation("Stopping worker {WorkerId}", _workerId);

        try
        {
            if (_channel != null)
            {
                await _channel.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error closing channel for worker {WorkerId}", _workerId);
        }

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill();
                _process.WaitForExit(5000);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error killing worker process {WorkerId}", _workerId);
        }

        Cleanup();
    }

    private string GetDefaultWorkerExecutablePath()
    {
        // TODO: In production, this should locate the bundled worker executable
        // For now, assume it's in the same directory or a known location
        var exeName = _config.Use32BitWorker
            ? "ProcessSandbox.Worker.Net48.exe"
            : "ProcessSandbox.Worker.exe";

        return System.IO.Path.Combine(AppContext.BaseDirectory, exeName);
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = _process?.ExitCode ?? -1;
        _logger.LogWarning(
            "Worker process {WorkerId} exited with code {ExitCode}",
            _workerId,
            exitCode);

        OnFailed(new WorkerFailedEventArgs(
            _workerId,
            $"Process exited with code {exitCode}",
            null));
    }

    private void OnChannelDisconnected(object? sender, ChannelDisconnectedEventArgs e)
    {
        _logger.LogWarning(
            "Worker {WorkerId} channel disconnected: {Reason}",
            _workerId,
            e.Reason);

        OnFailed(new WorkerFailedEventArgs(_workerId, e.Reason, e.Exception));
    }

    private void OnFailed(WorkerFailedEventArgs e)
    {
        try
        {
            Failed?.Invoke(this, e);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in Failed event handler");
        }
    }

    private void Cleanup()
    {
        if (_channel != null)
        {
            _channel.Disconnected -= OnChannelDisconnected;
            _channel.Dispose();
            _channel = null;
        }

        if (_process != null)
        {
            _process.Exited -= OnProcessExited;
            _process.Dispose();
            _process = null;
        }
    }

    /// <summary>
    /// Disposes the worker process and releases resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        StopAsync().GetAwaiter().GetResult();
        _usageLock?.Dispose();
    }
}

/// <summary>
/// Event arguments for worker failure events.
/// </summary>
public class WorkerFailedEventArgs : EventArgs
{
    /// <summary>
    /// Gets the ID of the worker that failed.
    /// </summary>
    public string WorkerId { get; }

    /// <summary>
    /// Gets the reason for the failure.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Gets the exception that caused the failure, if any.
    /// </summary>
    public Exception? Exception { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WorkerFailedEventArgs"/> class.
    /// </summary>
    /// <param name="workerId">The worker ID.</param>
    /// <param name="reason">The failure reason.</param>
    /// <param name="exception">The exception, if any.</param>
    public WorkerFailedEventArgs(string workerId, string reason, Exception? exception)
    {
        WorkerId = workerId;
        Reason = reason;
        Exception = exception;
    }
}