using System.Reflection;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Pool;

namespace ProcessSandbox.Proxy;

/// <summary>
/// Factory for creating process proxies.
/// </summary>
/// <typeparam name="TInterface"></typeparam>
public class ProcessProxyFactory<TInterface>:IDisposable where TInterface : class, IDisposable
{
    private readonly ProcessPool pool;
    private readonly ProcessPoolConfiguration config;
    private readonly ILogger<ProcessProxyDispatcher<TInterface>> logger;

    private ProcessProxyFactory(ProcessPoolConfiguration config, ILoggerFactory loggerFactory)
    {
        this.config = config;
        this.logger = loggerFactory.CreateLogger<ProcessProxyDispatcher<TInterface>>();
        this.pool = new ProcessPool(config, loggerFactory); 
    }

    private async Task InitializeAsync()
    {
        await pool.InitializeAsync();
    }

    /// <summary>
    /// Creates a new process proxy instance.
    /// </summary>
    /// <returns></returns>
    private async Task<TInterface> CreateProxyAsync(WorkerProcess worker)
    {
        var proxy = DispatchProxy.Create<TInterface, ProcessProxyDispatcher<TInterface>>();

        (proxy as ProcessProxyDispatcher<TInterface>)!.Initialize(worker, config, logger);

        return proxy;

    }

    /// <summary>
    /// Uses a process proxy to execute the given action.
    /// </summary>
    /// <param name="action"></param>
    /// <returns></returns>
    public async Task UseProxyAsync(Func<TInterface, Task> action)
    {
        for(int attempt = 1; attempt < 10; attempt++)
        {

            await pool._requestThrottle.WaitAsync();
            var worker = await pool.GetAvailableWorkerAsync();
            try
            {
                
                var proxy = await CreateProxyAsync(worker);
                await action(proxy);
                proxy.Dispose();
                
                // Check if worker should be recycled
                if (worker.ShouldRecycle())
                {
                    logger.LogInformation(
                        "Worker {WorkerId} needs recycling, starting replacement",
                        worker.WorkerId);

                    await pool.RecycleWorkerAsync(worker);
                }
                else
                {
                    // All ok - return worker to pool
                    pool.ReturnWorker(worker);
                }
                break;
            }
            catch (IpcException)
            {
                // Remove failed worker
                await pool.RemoveWorkerAsync(worker);

                // If this isn't the first call on the worker - then we need to throw the error because
                // the handling code can't assume that everything is ok.

                if (!worker.FirstInSequence)
                {
                    logger.LogError(
                        "IpcException on existing worker {WorkerId}, not retrying",
                        worker.WorkerId);
                    throw;
                }

                await Task.Delay(attempt * 10).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(
                    ex,
                    "Error executing methods on worker {WorkerId}",
                    worker.WorkerId);

                // Remove failed worker
                if (worker != null)
                {
                    await pool.RemoveWorkerAsync(worker);
                }

                throw;
            }
            finally
            {
                pool._requestThrottle.Release();
            }
        }   
    }

    /// <summary>
    /// Creates and initializes a new ProcessProxyFactory.
    /// </summary>
    /// <returns></returns>
    public static async Task<ProcessProxyFactory<TInterface>> CreateAsync(ProcessPoolConfiguration config, ILoggerFactory loggerFactory)
    {
        var factory = new ProcessProxyFactory<TInterface>(config, loggerFactory);
        await factory.InitializeAsync();
        return factory;
    }

    /// <summary>
    /// Disposes the ProcessProxyFactory and its resources.
    /// </summary>
    /// <exception cref="NotImplementedException"></exception>
    public void Dispose()
    {
        pool.Dispose();
    }
}