using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Reflection.Metadata;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandbox.Pool;

/// <summary>
/// Manages a pool of worker processes for executing methods in isolation.
/// </summary>
public class ProcessPool : IDisposable
{
    private readonly ProcessPoolConfiguration _config;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ProcessPool> _logger;
    private readonly ConcurrentBag<WorkerProcess> _availableWorkers;
    private readonly ConcurrentDictionary<string, WorkerProcess> _allWorkers;
    private readonly SemaphoreSlim _poolLock;
    private readonly CancellationTokenSource _disposalCts;
    private readonly SemaphoreSlim _requestThrottle;
    private readonly SemaphoreSlim _startupThrottle;
    private bool _disposed;

    const int maxAttempts = 10;

    /// <summary>
    /// Gets the current number of workers in the pool.
    /// </summary>
    public int WorkerCount => _allWorkers.Count;

    /// <summary>
    /// Gets the number of available (non-busy) workers.
    /// </summary>
    public int AvailableWorkerCount => _availableWorkers.Count(w => w.IsHealthy && !w.IsBusy);

    /// <summary>
    /// Initializes a new instance of the <see cref="ProcessPool"/> class.
    /// </summary>
    /// <param name="config">The pool configuration.</param>
    /// <param name="loggerFactory">The logger factory.</param>
    public ProcessPool(ProcessPoolConfiguration config, ILoggerFactory loggerFactory)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _config.Validate();

        _loggerFactory = loggerFactory ?? throw new ArgumentNullException(nameof(loggerFactory));
        _logger = loggerFactory.CreateLogger<ProcessPool>();

        _availableWorkers = new ConcurrentBag<WorkerProcess>();
        _allWorkers = new ConcurrentDictionary<string, WorkerProcess>();
        _poolLock = new SemaphoreSlim(1, 1);
        _disposalCts = new CancellationTokenSource();

        _requestThrottle = new SemaphoreSlim(_config.MaxPoolSize, _config.MaxPoolSize);

        _startupThrottle = new SemaphoreSlim(3, 3);

        _logger.LogInformation(
            "Process pool created: Min={MinPoolSize}, Max={MaxPoolSize}",
            _config.MinPoolSize,
            _config.MaxPoolSize);
    }

    /// <summary>
    /// Initializes the pool by starting the minimum number of workers.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes when initialization is done.</returns>
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProcessPool));

        _logger.LogInformation("Initializing process pool with {Count} workers", _config.MinPoolSize);

        var tasks = new List<Task>();
        for (int i = 0; i < _config.MinPoolSize; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var worker = await CreateWorkerAsync(cancellationToken);

                await _poolLock.WaitAsync(cancellationToken);
                try
                {
                    _allWorkers.TryAdd(worker.WorkerId, worker);
                    ReturnWorker(worker);
                }
                finally
                {
                    _poolLock.Release();
                }
                return worker;
            }));
        }

        await Task.WhenAll(tasks);

        _logger.LogInformation("Process pool initialized with {Count} workers", WorkerCount);
    }

    /// <summary>
    /// Executes a method invocation on an available worker.
    /// </summary>
    /// <param name="invocation">The method invocation details.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <param name="attempt"></param>
    /// <returns>The method result.</returns>
    public async Task<MethodResultMessage> ExecuteAsync(
        MethodInvocationMessage invocation,
        CancellationToken cancellationToken = default,
        int attempt = 0)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProcessPool));

        if (invocation == null)
            throw new ArgumentNullException(nameof(invocation));

        await _requestThrottle.WaitAsync(cancellationToken);

        WorkerProcess? worker = null;

        try
        {
            // Get an available worker
            _logger.LogDebug(
                "Acquiring worker for method {Method}",
                invocation.MethodName);
            
            worker = await GetAvailableWorkerAsync(cancellationToken);

            _logger.LogDebug(
                "Acquired worker {WorkerId} for method {Method}",
                worker.WorkerId,
                invocation.MethodName);

            _logger.LogDebug(
                "Executing method {Method} on worker {WorkerId}",
                invocation.MethodName,
                worker.WorkerId);

            // Execute the method
            var result = await worker.InvokeMethodAsync(invocation, cancellationToken);

            // Check if worker should be recycled
            if (worker.ShouldRecycle())
            {
                _logger.LogInformation(
                    "Worker {WorkerId} needs recycling, starting replacement",
                    worker.WorkerId);

                await RecycleWorkerAsync(worker);
            }
            else
            {
                // Return worker to pool
                ReturnWorker(worker);
            }

            return result;
        }
        catch (IpcException)
        {
            // Remove failed worker
            if (worker != null)
            {
                await RemoveWorkerAsync(worker);
            }

            // Wait and try again
            if(attempt >= maxAttempts)
            {
                _logger.LogError(
                    "Max retry attempts reached for method {Method}",
                    invocation.MethodName);
                throw;
            }
            await Task.Delay(attempt*10).ConfigureAwait(false);
            return await ExecuteAsync(invocation, cancellationToken);
            
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error executing method {Method} on worker {WorkerId}",
                invocation.MethodName,
                worker?.WorkerId ?? "unknown");

            // Remove failed worker
            if (worker != null)
            {
                await RemoveWorkerAsync(worker);
            }

            throw;
        }
        finally
        {
            _requestThrottle.Release();
        }
    }

    private async Task<WorkerProcess> GetAvailableWorkerAsync(CancellationToken cancellationToken)
    {
        const int maxAttempts = 10;
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            // Try to get an available worker
            if (_availableWorkers.TryTake(out var worker))
            {
                _logger.LogDebug("Acquired worker {WorkerId} from pool", worker.WorkerId);
                if (worker.IsHealthy)
                {
                    return worker;
                }

                // Worker is not healthy, remove it
                await RemoveWorkerAsync(worker);
            }

            // No available workers, try to create a new one if under max pool size
            await _poolLock.WaitAsync(cancellationToken);
            try
            {
                if (_allWorkers.Count < _config.MaxPoolSize)
                {
                    var newWorker = await CreateWorkerAsync(cancellationToken);
                    ReturnWorker(newWorker);
                    return newWorker;
                }
            }
            finally
            {
                _poolLock.Release();
            }

            // Pool is at max capacity, wait a bit and retry
            attempt++;
            _logger.LogDebug("Pool at capacity, waiting for available worker (attempt {Attempt})", attempt);
            await Task.Delay(100 * attempt, cancellationToken);

        }

        throw new PoolExhaustedException(_config.MaxPoolSize);
    }

    private async Task<WorkerProcess> CreateWorkerAsync(CancellationToken cancellationToken)
    {
        await _startupThrottle.WaitAsync(cancellationToken);

        try
        {
            var worker = new WorkerProcess(_config, _loggerFactory.CreateLogger<WorkerProcess>());
            worker.Failed += OnWorkerFailed;

            await worker.StartAsync(cancellationToken);

            _allWorkers.TryAdd(worker.WorkerId, worker);

            return worker;
        }
        finally
        {
            // *** NEW: Release startup slot ***
            _startupThrottle.Release();
        }
    }

    private void ReturnWorker(WorkerProcess worker)
    {
        if (worker.IsHealthy)
        {
            _availableWorkers.Add(worker);
        }
        else
        {
            _ = Task.Run(() => RemoveWorkerAsync(worker), _disposalCts.Token);
        }
    }

    private async Task RecycleWorkerAsync(WorkerProcess worker)
    {
        _logger.LogInformation("Recycling worker {WorkerId}", worker.WorkerId);

        try
        {
            // Remove old worker
            await RemoveWorkerAsync(worker);

            // Create replacement if below min pool size
            await _poolLock.WaitAsync(_disposalCts.Token);
            try
            {
                if (_allWorkers.Count < _config.MinPoolSize)
                {
                    var newWorker = await CreateWorkerAsync(_disposalCts.Token);
                    ReturnWorker(newWorker);
                }
            }
            finally
            {
                _poolLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error recycling worker {WorkerId}", worker.WorkerId);
        }
    }

    private async Task RemoveWorkerAsync(WorkerProcess worker)
    {
        _logger.LogInformation("Removing worker {WorkerId}", worker.WorkerId);

        worker.Failed -= OnWorkerFailed;

        if (_allWorkers.TryRemove(worker.WorkerId, out _))
        {
            try
            {
                await worker.StopAsync();
                worker.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error stopping worker {WorkerId}", worker.WorkerId);
            }
        }
    }

    private void OnWorkerFailed(object? sender, WorkerFailedEventArgs e)
    {
        _logger.LogWarning(
            "Worker failed. WorkerId: {WorkerId} Reason: {Reason}",
            e.WorkerId,
            e.Reason);

        if (sender is WorkerProcess worker)
        {
            _ = Task.Run(() => RecycleWorkerAsync(worker), _disposalCts.Token);
        }
    }

    /// <summary>
    /// Gets statistics about the process pool.
    /// </summary>
    /// <returns>Pool statistics.</returns>
    public ProcessPoolStatistics GetStatistics()
    {
        var workers = _allWorkers.Values.ToArray();

        return new ProcessPoolStatistics
        {
            TotalWorkers = workers.Length,
            HealthyWorkers = workers.Count(w => w.IsHealthy),
            BusyWorkers = workers.Count(w => w.IsBusy),
            AvailableWorkers = workers.Count(w => w.IsHealthy && !w.IsBusy),
            TotalCalls = workers.Sum(w => w.CallCount),
            AverageMemoryMB = CalculateAverageMemory(workers)
        };
    }


    private double CalculateAverageMemory(WorkerProcess[] workers)
    {
        var memorySum = 0.0;
        var count = 0;

        foreach (var worker in workers)
        {
            try
            {
                if (worker.ProcessId > 0)
                {
                    var process = Process.GetProcessById(worker.ProcessId);
                    if (!process.HasExited)
                    {
                        memorySum += process.WorkingSet64 / (1024.0 * 1024.0);
                        count++;
                    }
                }
            }
            catch
            {
                // Worker might have exited, skip it
            }
        }

        return count > 0 ? memorySum / count : 0;
    }


    /// <summary>
    /// Disposes the process pool and all worker processes.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _logger.LogInformation("Disposing process pool");

        _disposalCts.Cancel();

        var workers = _allWorkers.Values.ToArray();
        foreach (var worker in workers)
        {
            try
            {
                worker.StopAsync().GetAwaiter().GetResult();
                worker.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error disposing worker {WorkerId}", worker.WorkerId);
            }
        }

        _allWorkers.Clear();
        _poolLock?.Dispose();
        _disposalCts?.Dispose();

        _logger.LogInformation("Process pool disposed");
    }
}

/// <summary>
/// Statistics about the process pool.
/// </summary>
public class ProcessPoolStatistics
{
    /// <summary>
    /// Gets or sets the total number of workers in the pool.
    /// </summary>
    public int TotalWorkers { get; set; }

    /// <summary>
    /// Gets or sets the number of healthy workers.
    /// </summary>
    public int HealthyWorkers { get; set; }

    /// <summary>
    /// Gets or sets the number of busy workers.
    /// </summary>
    public int BusyWorkers { get; set; }

    /// <summary>
    /// Gets or sets the number of available workers.
    /// </summary>
    public int AvailableWorkers { get; set; }

    /// <summary>
    /// Gets or sets the total number of calls processed.
    /// </summary>
    public int TotalCalls { get; set; }

    /// <summary>
    /// Gets or sets the average memory usage in megabytes.
    /// </summary>
    public double AverageMemoryMB { get; set; }
}