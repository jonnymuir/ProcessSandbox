using System;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandbox.Pool;

namespace ProcessSandbox.Proxy;

/// <summary>
/// Factory for creating proxies that execute methods in isolated worker processes.
/// </summary>
public static class ProcessProxy
{
    /// <summary>
    /// Creates a proxy instance that implements the specified interface.
    /// Method calls on the proxy are executed in isolated worker processes.
    /// </summary>
    /// <typeparam name="TInterface">The interface to implement.</typeparam>
    /// <param name="configuration">The process pool configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>A proxy instance implementing the interface.</returns>
    public static TInterface Create<TInterface>(
        ProcessPoolConfiguration configuration,
        ILoggerFactory? loggerFactory = null)
        where TInterface : class
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        if (!typeof(TInterface).IsInterface)
            throw new ArgumentException($"{typeof(TInterface).Name} must be an interface");

        loggerFactory ??= NullLoggerFactory.Instance;

        var pool = new ProcessPool(configuration, loggerFactory);
        pool.InitializeAsync().GetAwaiter().GetResult();

        var proxy = DispatchProxy.Create<TInterface, ProcessProxyDispatcher<TInterface>>();
        
        if (proxy is ProcessProxyDispatcher<TInterface> dispatcher)
        {
            dispatcher.Initialize(pool, configuration, loggerFactory.CreateLogger<ProcessProxyDispatcher<TInterface>>());
        }

        return proxy;
    }

    /// <summary>
    /// Creates a proxy instance asynchronously.
    /// </summary>
    /// <typeparam name="TInterface">The interface to implement.</typeparam>
    /// <param name="configuration">The process pool configuration.</param>
    /// <param name="loggerFactory">Optional logger factory.</param>
    /// <returns>A proxy instance implementing the interface.</returns>
    public static async Task<TInterface> CreateAsync<TInterface>(
        ProcessPoolConfiguration configuration,
        ILoggerFactory? loggerFactory = null)
        where TInterface : class
    {
        if (configuration == null)
            throw new ArgumentNullException(nameof(configuration));

        if (!typeof(TInterface).IsInterface)
            throw new ArgumentException($"{typeof(TInterface).Name} must be an interface");

        loggerFactory ??= NullLoggerFactory.Instance;

        if(configuration.ImplementationTypeName == null)
        {
            configuration.ImplementationTypeName = typeof(TInterface).FullName!;
        }

        var pool = new ProcessPool(configuration, loggerFactory);
        await pool.InitializeAsync();

        var proxy = DispatchProxy.Create<TInterface, ProcessProxyDispatcher<TInterface>>();
        
        if (proxy is ProcessProxyDispatcher<TInterface> dispatcher)
        {
            dispatcher.Initialize(pool, configuration, loggerFactory.CreateLogger<ProcessProxyDispatcher<TInterface>>());
        }

        return proxy;
    }
}

/// <summary>
/// DispatchProxy implementation that routes method calls to worker processes.
/// </summary>
/// <typeparam name="TInterface">The interface being proxied.</typeparam>
public class ProcessProxyDispatcher<TInterface> : DispatchProxy where TInterface : class
{
    private ProcessPool? _pool;
    private ProcessPoolConfiguration? _config;
    private ILogger? _logger;

    /// <summary>
    /// Initializes the dispatcher with the process pool and configuration.
    /// </summary>
    /// <param name="pool">The process pool.</param>
    /// <param name="config">The configuration.</param>
    /// <param name="logger">The logger.</param>
    public void Initialize(ProcessPool pool, ProcessPoolConfiguration config, ILogger logger)
    {
        _pool = pool ?? throw new ArgumentNullException(nameof(pool));
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Invokes a method on the proxy by routing it to a worker process.
    /// </summary>
    /// <param name="targetMethod">The method being invoked.</param>
    /// <param name="args">The method arguments.</param>
    /// <returns>The method result.</returns>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (_pool == null || _config == null || _logger == null)
            throw new InvalidOperationException("Proxy not initialized");

        if (targetMethod == null)
            throw new ArgumentNullException(nameof(targetMethod));

        // Handle special methods
        if (targetMethod.Name == "ToString" && targetMethod.DeclaringType == typeof(object))
            return $"ProcessProxy<{typeof(TInterface).Name}>";

        if (targetMethod.Name == "GetHashCode" && targetMethod.DeclaringType == typeof(object))
            return GetHashCode();

        if (targetMethod.Name == "Equals" && targetMethod.DeclaringType == typeof(object))
            return Equals(args?[0]);

        // Handle Dispose if interface implements IDisposable
        if (targetMethod.Name == "Dispose" && typeof(IDisposable).IsAssignableFrom(typeof(TInterface)))
        {
            _pool.Dispose();
            return null;
        }

        // Create method invocation message
        var parameters = args ?? Array.Empty<object>();
        var parameterTypes = targetMethod.GetParameters();
        var parameterTypeNames = new string[parameterTypes.Length];
        
        for (int i = 0; i < parameterTypes.Length; i++)
        {
            parameterTypeNames[i] = SerializationHelper.GetTypeName(parameterTypes[i].ParameterType);
        }

        var invocation = new MethodInvocationMessage(
            Guid.NewGuid(),
            targetMethod.Name,
            (int)_config.MethodCallTimeout.TotalMilliseconds)
        {
            ParameterTypeNames = parameterTypeNames,
            SerializedParameters = SerializationHelper.SerializeParameters(parameters)
        };

        // Execute synchronously or asynchronously based on return type
        if (typeof(Task).IsAssignableFrom(targetMethod.ReturnType))
        {
            return InvokeAsyncMethod(invocation, targetMethod.ReturnType);
        }
        else
        {
            return InvokeSyncMethod(invocation, targetMethod.ReturnType);
        }
    }

    private object? InvokeSyncMethod(MethodInvocationMessage invocation, Type returnType)
    {
        _logger!.LogDebug("Invoking sync method {MethodName} with return type {ReturnType}", invocation.MethodName, returnType.FullName);
        var result = _pool!.ExecuteAsync(invocation).GetAwaiter().GetResult();
        _logger!.LogDebug("Received result for method {MethodName}", invocation.MethodName);
        if (!result.Success)
        {
            throw new RemoteInvocationException(
                result.ExceptionType ?? "Unknown",
                (result.ExceptionMessage ?? "Method failed")+", Remote Stack Trace: "+(result.StackTrace ?? "No stack trace"),
                result.StackTrace);
        }

        if (returnType == typeof(void))
            return null;

        return SerializationHelper.DeserializeReturnValue(result.SerializedResult, returnType);
    }

    private object InvokeAsyncMethod(MethodInvocationMessage invocation, Type returnType)
    {
        _logger!.LogDebug("Invoking async method {MethodName} with return type {ReturnType}", invocation.MethodName, returnType.FullName);
        // Handle Task (no result)
        if (returnType == typeof(Task))
        {
            return InvokeAsyncVoidMethod(invocation);
        }

        // Handle Task<T>
        if (returnType.IsGenericType && returnType.GetGenericTypeDefinition() == typeof(Task<>))
        {
            var resultType = returnType.GetGenericArguments()[0];
            var method = typeof(ProcessProxyDispatcher<TInterface>)
                .GetMethod(nameof(InvokeAsyncGenericMethod), BindingFlags.NonPublic | BindingFlags.Instance)!
                .MakeGenericMethod(resultType);

            return method.Invoke(this, new object[] { invocation })!;
        }

        throw new NotSupportedException($"Return type {returnType} is not supported");
    }

    private async Task InvokeAsyncVoidMethod(MethodInvocationMessage invocation)
    {
        var result = await _pool!.ExecuteAsync(invocation);

        if (!result.Success)
        {
            throw new RemoteInvocationException(
                result.ExceptionType ?? "Unknown",
                (result.ExceptionMessage ?? "Method failed")+", Remote Stack Trace: "+(result.StackTrace ?? "No stack trace"),
                result.StackTrace);
        }
    }

    private async Task<T> InvokeAsyncGenericMethod<T>(MethodInvocationMessage invocation)
    {
        _logger!.LogDebug("Invoking async generic method {MethodName} with return type {ReturnType}", invocation.MethodName, typeof(T).FullName);
        var result = await _pool!.ExecuteAsync(invocation);

        if (!result.Success)
        {
            throw new RemoteInvocationException(
                result.ExceptionType ?? "Unknown",
                (result.ExceptionMessage ?? "Method failed")+", Remote Stack Trace: "+(result.StackTrace ?? "No stack trace"),
                result.StackTrace);
        }
        _logger!.LogDebug("Deserializing result for method {MethodName}", invocation.MethodName);

        var returnResult = (T)SerializationHelper.DeserializeReturnValue(result.SerializedResult, typeof(T))!;

        _logger!.LogDebug("Deserialized result for method {MethodName}, returnResult {ReturnResult}", invocation.MethodName, returnResult);

        return returnResult;
    }
}