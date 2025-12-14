using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandbox.IPC;
using System.Runtime.InteropServices;

namespace ProcessSandbox.Pool;

/// <summary>
/// Represents a single worker process in the pool.
/// </summary>
/// <remarks>
/// Initializes a new instance of the <see cref="WorkerProcess"/> class.
/// </remarks>
/// <param name="config">The process pool configuration.</param>
/// <param name="logger">The logger instance.</param>
public class WorkerProcess(ProcessPoolConfiguration config, ILogger<WorkerProcess> logger) : IDisposable
{
    private readonly string _workerId = Guid.NewGuid().ToString("N");
    private readonly DateTime _startTime = DateTime.UtcNow;

    private Process? _process;
    private RequestResponseChannel? _channel;
    private int _callCount;
    private bool _disposed;
    private int _recycleCount = 0;
    private readonly SemaphoreSlim _usageLock = new SemaphoreSlim(1, 1);

    private TaskCompletionSource<bool> _workerReadyTcs = null!;

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
    /// Event raised when the worker process exits or becomes unhealthy.
    /// </summary>
    public event EventHandler<WorkerFailedEventArgs>? Failed;

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

        logger.LogInformation("Starting worker process {WorkerId}", _workerId);

        try
        {
            // Generate unique pipe name
            var pipeName = PipeNameGenerator.Generate();

#if NET48
                // This code runs only when compiling for .NET Framework 4.8
                int currentProcessId = Process.GetCurrentProcess().Id;
#else
            // This code runs when compiling for .NET 8 (or any other modern .NET target)
            int currentProcessId = Environment.ProcessId;
#endif

            // Create worker configuration
            var workerConfig = new WorkerConfiguration
            {
                AssemblyPath = config.ImplementationAssemblyPath,
                TypeName = config.ImplementationTypeName,
                PipeName = pipeName,
                VerboseLogging = config.VerboseWorkerLogging,
                ParentProcessId = currentProcessId
            };

            // Serialize configuration to base64
            var configBytes = MessagePack.MessagePackSerializer.Serialize(workerConfig);
            var configBase64 = Convert.ToBase64String(configBytes);

            // This TCS will be completed when the worker sends its "Ready" signal.
            _workerReadyTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            string fileName;
            string arguments;
            if (config.UseDotNetFrameworkWorker)
            {
                fileName = Path.Combine(AppContext.BaseDirectory, "ProcessSandbox.Worker.Net48.exe");
                arguments = $"--config {configBase64}";
            }
            else
            {
                // Note: 'dotnet' must be available in the system's PATH
                fileName = "dotnet";
                arguments = $"{Path.Combine(AppContext.BaseDirectory, "ProcessSandbox.Worker.dll")} --config {configBase64}";
            }

            // Start the process
            var startInfo = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
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
                {
                    if (e.Data.Trim().Equals("PROCESS_SANDBOX_WORKER_READY", StringComparison.Ordinal))
                    {
                        _workerReadyTcs.TrySetResult(true);
                    }
                }
            };

            _process.ErrorDataReceived += (s, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    logger.LogError("[Worker {WorkerId}] {Error}", _workerId, e.Data);
                    Cleanup();
                }
            };

            if (!_process.Start())
                throw new WorkerStartupException("Failed to start worker process");

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            logger.LogInformation(
                "Worker process started: {WorkerId}, PID: {ProcessId}",
                _workerId,
                _process.Id);

            // --- Wait for the worker to signal it is listening on the pipe ---
            using var startupTimeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            startupTimeoutCts.CancelAfter(config.ProcessStartTimeout);

            // Wait for the worker process to report readiness
            // Wait for the worker process to report readiness
#if NET48
            // .NET Framework 4.8 doesn't have Task.WaitAsync.
            // Use the blocking Task.Wait and manage cancellation manually.
            try
            {
                // Blocking wait for the task to complete
                _workerReadyTcs.Task.Wait((int)config.ProcessStartTimeout.TotalMilliseconds, startupTimeoutCts.Token);
            }
            catch (AggregateException ex) when (ex.InnerException is TaskCanceledException)
            {
                // This catch handles the scenario where the startupTimeoutCts token is cancelled, 
                // or where the Task.Wait is cancelled. We rethrow as OperationCanceledException 
                // to match the modern flow.
                if (cancellationToken.IsCancellationRequested)
                {
                    throw new OperationCanceledException(cancellationToken);
                }
                throw new OperationCanceledException(ex.InnerException.Message, ex.InnerException);
            }
            catch (TimeoutException ex)
            {
                // Task.Wait with timeout can throw TimeoutException if the wait time expires.
                throw new OperationCanceledException("Connection timed out.", ex);
            }
#else
            // .NET 8.0 (and modern .NET) supports Task.WaitAsync
            await _workerReadyTcs.Task.WaitAsync(startupTimeoutCts.Token)
                .ConfigureAwait(false);
#endif
            // Connect to the worker via named pipe
            var client = new NamedPipeClientChannel(pipeName);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(config.ProcessStartTimeout);

            await client.ConnectAsync(
                (int)config.ProcessStartTimeout.TotalMilliseconds,
                timeoutCts.Token);

            _channel = new RequestResponseChannel(client, logger);
            _channel.Disconnected += OnChannelDisconnected;

            logger.LogInformation("Worker {WorkerId} connected and ready", _workerId);
        }
        catch (Exception ex) when (ex is OperationCanceledException && _process?.HasExited == false)
        {
            // Handle timeout on _workerReadyTcs.Task.WaitAsync as a startup failure
            var workerStartupEx = new WorkerStartupException(
                $"Worker {_workerId} failed to signal readiness within {config.ProcessStartTimeout.TotalSeconds} seconds.", ex);

            logger.LogError(workerStartupEx, "Failed to start worker {WorkerId} (Ready Signal Timeout)", _workerId);
            Cleanup();
            throw workerStartupEx;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to start worker {WorkerId}", _workerId);
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
        
        logger.LogDebug(
            "Invoking method {MethodName} on worker {WorkerId}",
            invocation.MethodName,
            _workerId);
            
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

            logger.LogDebug(
                "Method {MethodName} on worker {WorkerId} completed",
                invocation.MethodName,
                _workerId);

            return result;
        }
        finally
        {
            IsBusy = false;
            _usageLock.Release();
        }
    }

    /// <summary>
    /// Checks if the worker should be recycled based on configured thresholds.
    /// </summary>
    /// <returns>True if the worker should be recycled; otherwise, false.</returns>
    public bool ShouldRecycle()
    {
        _recycleCount++;
        if (_recycleCount < config.RecycleAfterCalls)
        {
            return false;
        }

        _recycleCount = 0;
        
        logger.LogDebug("Checking if worker {WorkerId} should recycle", _workerId);

        // Check call count threshold
        if (config.ProcessRecycleThreshold > 0 && _callCount >= config.ProcessRecycleThreshold)
        {
            logger.LogInformation(
                "Worker {WorkerId} should recycle: call count {CallCount} >= {Threshold}",
                _workerId,
                _callCount,
                config.ProcessRecycleThreshold);
            return true;
        }

        // Check lifetime
        var uptime = DateTime.UtcNow - _startTime;
        if (uptime >= config.MaxProcessLifetime)
        {
            logger.LogInformation(
                "Worker {WorkerId} should recycle: uptime {Uptime} >= {MaxLifetime}",
                _workerId,
                uptime,
                config.MaxProcessLifetime);
            return true;
        }

        // Passive health checks - query the process directly
        if (_process != null && !_process.HasExited)
        {
            try
            {
                _process.Refresh();

                // Check memory usage
                var workingSetMB = _process.WorkingSet64 / (1024.0 * 1024.0);

                if (workingSetMB > config.MaxMemoryMB)
                {
                    logger.LogInformation(
                        "Worker {WorkerId} should recycle: memory {Memory:F1}MB > {MaxMemory}MB",
                        _workerId,
                        workingSetMB,
                        config.MaxMemoryMB);
                    return true;
                }

                // Check GDI objects
                var gdiObjects = GetGdiObjectCount(_process);
                if (gdiObjects > config.MaxGdiHandles)
                {
                    logger.LogInformation(
                        "Worker {WorkerId} should recycle: GDI handles {GdiHandles} > {MaxGdiHandles}",
                        _workerId,
                        gdiObjects,
                        config.MaxGdiHandles);
                    return true;
                }

                // Check USER objects
                var userObjects = GetUserObjectCount(_process);
                if (userObjects > config.MaxUserHandles)
                {
                    logger.LogInformation(
                        "Worker {WorkerId} should recycle: USER handles {UserHandles} > {MaxUserHandles}",
                        _workerId,
                        userObjects,
                        config.MaxUserHandles);
                    return true;
                }

                // Check total handles
                if (_process.HandleCount > config.MaxTotalHandles)
                {
                    logger.LogInformation(
                        "Worker {WorkerId} should recycle: total handles {TotalHandles} > {MaxTotalHandles}",
                        _workerId,
                        _process.HandleCount,
                        config.MaxTotalHandles);
                    return true;
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Error checking worker {WorkerId} health, marking for recycle", _workerId);
                return true; // Process is probably dead or inaccessible
            }
        }

        return false;
    }

    private static int GetGdiObjectCount(Process process)
    {
        try
        {
            return NativeMethods.GetGuiResources(process.Handle, 0); // GR_GDIOBJECTS = 0
        }
        catch
        {
            return 0;
        }
    }

    private static int GetUserObjectCount(Process process)
    {
        try
        {
            return NativeMethods.GetGuiResources(process.Handle, 1); // GR_USEROBJECTS = 1
        }
        catch
        {
            return 0;
        }
    }

    /// <summary>
    /// Gracefully stops the worker process.
    /// </summary>
    /// <returns>A task that completes when the worker is stopped.</returns>
    public async Task StopAsync()
    {
        if (_disposed)
            return;

        logger.LogInformation("Stopping worker {WorkerId}, channel {ChannelId}", _workerId, _channel?.ChannelId);

        try
        {
            if (_channel != null)
            {
                await _channel.CloseAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error closing channel for worker {WorkerId}", _workerId);
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
            logger.LogWarning(ex, "Error killing worker process {WorkerId}", _workerId);
        }

        Cleanup();
    }

    private void OnProcessExited(object? sender, EventArgs e)
    {
        var exitCode = _process?.ExitCode ?? -1;
        logger.LogWarning(
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
        logger.LogWarning(
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
            logger.LogError(ex, "Error in Failed event handler");
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

        _workerReadyTcs?.TrySetCanceled();
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

/// <summary>
/// Native methods for retrieving GDI/USER object counts.
/// </summary>
internal static class NativeMethods
{
    /// <summary>
    /// Gets the count of GDI or USER objects for a process.
    /// </summary>
    /// <param name="hProcess">Handle to the process.</param>
    /// <param name="uiFlags">0 for GDI objects, 1 for USER objects.</param>
    /// <returns>The count of objects.</returns>
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    internal static extern int GetGuiResources(IntPtr hProcess, int uiFlags);
}