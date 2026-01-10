using System.Collections.Concurrent;
using System.Diagnostics;
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
    /// <summary>
    /// Semaphore to throttle incoming requests to the pool.
    /// </summary>
    public readonly SemaphoreSlim _requestThrottle;
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
    /// <returns>A task that completes when initialization is done.</returns>
    public async Task InitializeAsync()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ProcessPool));

        _logger.LogInformation("Initializing process pool with {Count} workers", _config.MinPoolSize);

        var tasks = new List<Task>();
        for (int i = 0; i < _config.MinPoolSize; i++)
        {
            tasks.Add(Task.Run(async () =>
            {
                var worker = await CreateWorkerAsync();

                await _poolLock.WaitAsync();
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
    /// Gets an available worker from the pool, creating a new one if necessary.
    /// </summary>
    /// <returns></returns>
    /// <exception cref="PoolExhaustedException"></exception>
    public async Task<WorkerProcess> GetAvailableWorkerAsync()
    {
        const int maxAttempts = 10;
        var attempt = 0;

        while (attempt < maxAttempts)
        {
            // Try to get an available worker
            if (_availableWorkers.TryTake(out var worker))
            {
                if (worker.IsHealthy)
                {
                    _logger.LogDebug("Worker {WorkerId} from pool is available and healthy", worker.WorkerId);
                    return worker;
                }

                // Worker is not healthy, remove it
                _logger.LogDebug("Worker {WorkerId} from pool is available but not healthy. Removing it from pool", worker.WorkerId);
                await RemoveWorkerAsync(worker);
            }

            // No available workers, try to create a new one if under max pool size
            await _poolLock.WaitAsync();
            try
            {
                if (_allWorkers.Count < _config.MaxPoolSize)
                {
                    var newWorker = await CreateWorkerAsync();
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
            await Task.Delay(100 * attempt);

        }

        throw new PoolExhaustedException(_config.MaxPoolSize);
    }

    private async Task<WorkerProcess> CreateWorkerAsync()
    {
        await _startupThrottle.WaitAsync();

        try
        {
            var worker = new WorkerProcess(_config, _loggerFactory.CreateLogger<WorkerProcess>());
            worker.Failed += OnWorkerFailed;

            await worker.StartAsync();

            _allWorkers.TryAdd(worker.WorkerId, worker);

            return worker;
        }
        finally
        {
            // *** NEW: Release startup slot ***
            _startupThrottle.Release();
        }
    }

    /// <summary>
    /// Returns a worker to the pool.
    /// </summary>
    /// <param name="worker"></param>
    public void ReturnWorker(WorkerProcess worker)
    {
        _logger.LogDebug("Returning worker to the pool: {workerid}", worker.WorkerId);
        
        if (worker.IsHealthy)
        {
            worker.FirstInSequence = true;
            _availableWorkers.Add(worker);
        }
        else
        {
            _ = Task.Run(() => RemoveWorkerAsync(worker));
        }
    }

    /// <summary>
    /// Recycles a worker process by removing it and creating a replacement if needed.
    /// </summary>
    /// <param name="worker"></param>
    /// <returns></returns>
    public async Task RecycleWorkerAsync(WorkerProcess worker)
    {
        _logger.LogInformation("Recycling worker {WorkerId}", worker.WorkerId);

        try
        {
            // Remove old worker
            await RemoveWorkerAsync(worker);

            // Create replacement if below min pool size
            await _poolLock.WaitAsync();
            try
            {
                if (_allWorkers.Count < _config.MinPoolSize)
                {
                    var newWorker = await CreateWorkerAsync();
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

    /// <summary>
    /// Removes a worker from the pool and disposes it.
    /// </summary>
    /// <param name="worker"></param>
    /// <returns></returns>
    public async Task RemoveWorkerAsync(WorkerProcess worker)
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
            _ = Task.Run(() => RecycleWorkerAsync(worker));
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
    /// Finalizer to ensure resources are disposed - just incase you've forgotten to use using!
    /// </summary>
    ~ProcessPool() => Dispose();

    /// <summary>
    /// Disposes the process pool and all worker processes.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _logger.LogInformation("Disposing process pool");

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