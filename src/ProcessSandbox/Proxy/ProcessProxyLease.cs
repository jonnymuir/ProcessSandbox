using System.Reflection;
using ProcessSandbox.Pool;

namespace ProcessSandbox.Proxy;

/// <summary>
/// The process proxy
/// </summary>
/// <typeparam name="TInterface"></typeparam>
public class ProcessProxyLease<TInterface> : DispatchProxy
    where TInterface : class, IDisposable
{
    private ProcessPool? pool;
    private SemaphoreSlim? throttle;
    private WorkerProcess? worker;
    private TInterface? proxy;
    
    private bool _disposed;

    /// <summary>
    /// Dispose the lease
    /// </summary>
    private void OnDispose()
    {
        if (_disposed) return;
        proxy!.Dispose();
        
        try
        {
            if (worker!.ShouldRecycle())
            {
                // Note: In a real app, you'd want an async dispose or 
                // fire-and-forget the recycling to avoid blocking
                Task.Run(() => pool!.RecycleWorkerAsync(worker));
            }
            else
            {
                pool!.ReturnWorker(worker);
            }
        }
        finally
        {
            throttle!.Release();
            _disposed = true;
        }
    }

    /// <summary>
    /// Initialize the lease
    /// </summary>
    /// <param name="pool"></param>
    /// <param name="throttle"></param>
    /// <param name="worker"></param>
    /// <param name="proxy"></param>
    public ProcessProxyLease<TInterface> Initialize(ProcessPool pool, SemaphoreSlim throttle, WorkerProcess worker, TInterface proxy)
    {
        this.pool = pool;
        this.throttle = throttle;
        this.worker = worker;
        this.proxy = proxy;

        return this;
    }

    /// <summary>
    /// Pass the method to the leased proxy
    /// </summary>
    /// <param name="targetMethod"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (targetMethod == null) return null;

        // Redirect Dispose calls to our local Dispose logic
        if (targetMethod.Name == nameof(IDisposable.Dispose))
        {
            OnDispose();
            return null;
        }
        
        try
        {
            return targetMethod.Invoke(proxy, args);
        }
        catch(TargetInvocationException tex)
        {
            throw tex.InnerException ?? tex;
        }
    }
}