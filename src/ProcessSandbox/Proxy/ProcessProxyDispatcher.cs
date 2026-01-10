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
/// DispatchProxy implementation that routes method calls to worker processes.
/// </summary>
/// <typeparam name="TInterface">The interface being proxied.</typeparam>
public class ProcessProxyDispatcher<TInterface> : DispatchProxy where TInterface : class
{
    //private ProcessPool? _pool;
    private ProcessPoolConfiguration? config;
    private ILogger? logger;
    private WorkerProcess? worker = null;

    /// <summary>
    /// Initializes the dispatcher with the process pool and configuration.
    /// </summary>
    /// <param name="worker">The process worker.</param>
    /// <param name="config">The configuration.</param>
    /// <param name="logger">The logger.</param>
    public void Initialize(WorkerProcess worker, ProcessPoolConfiguration config, ILogger logger)
    {
        this.worker = worker;
        this.config = config;
        this.logger = logger;
    }

    /// <summary>
    /// Invokes a method on the proxy by routing it to a worker process.
    /// </summary>
    /// <param name="targetMethod">The method being invoked.</param>
    /// <param name="args">The method arguments.</param>
    /// <returns>The method result.</returns>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        if (worker == null ||config == null || logger == null)
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
            (int)config.MethodCallTimeout.TotalMilliseconds)
        {
            ParameterTypeNames = parameterTypeNames,
            SerializedParameters = SerializationHelper.SerializeParameters(parameters)
        };



        object? ret;

        // Execute synchronously or asynchronously based on return type
        if (typeof(Task).IsAssignableFrom(targetMethod.ReturnType))
        {
            ret = InvokeAsyncMethod(invocation, targetMethod.ReturnType);
        }
        else
        {
            ret = InvokeSyncMethod(invocation, targetMethod.ReturnType);
        }

        return ret;
    }

    private object? InvokeSyncMethod(MethodInvocationMessage invocation, Type returnType)
    {
        if (worker == null ||config == null || logger == null)
            throw new InvalidOperationException("Proxy not initialized");

        logger.LogDebug("Invoking sync method {MethodName} with return type {ReturnType}", invocation.MethodName, returnType.FullName);
        var result = worker.InvokeMethodAsync(invocation).GetAwaiter().GetResult();

        logger.LogDebug("Received result for method {MethodName}", invocation.MethodName);
        if (!result.Success)
        {
            throw new RemoteInvocationException(
                result.ExceptionType ?? "Unknown",
                (result.ExceptionMessage ?? "Method failed") + ", Remote Stack Trace: " + (result.StackTrace ?? "No stack trace"),
                result.StackTrace);
        }

        if (returnType == typeof(void))
            return null;

        return SerializationHelper.DeserializeReturnValue(result.SerializedResult, returnType);
    }

    private object InvokeAsyncMethod(MethodInvocationMessage invocation, Type returnType)
    {
        
        if (worker == null ||config == null || logger == null)
            throw new InvalidOperationException("Proxy not initialized");
        
        logger.LogDebug("Invoking async method {MethodName} with return type {ReturnType}", invocation.MethodName, returnType.FullName);
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

            return method.Invoke(this, [invocation])!;
        }

        throw new NotSupportedException($"Return type {returnType} is not supported");
    }

    private async Task InvokeAsyncVoidMethod(MethodInvocationMessage invocation)
    {
        if (worker == null ||config == null || logger == null)
            throw new InvalidOperationException("Proxy not initialized");
        
        var result = await worker.InvokeMethodAsync(invocation);

        if (!result.Success)
        {
            throw new RemoteInvocationException(
                result.ExceptionType ?? "Unknown",
                (result.ExceptionMessage ?? "Method failed") + ", Remote Stack Trace: " + (result.StackTrace ?? "No stack trace"),
                result.StackTrace);
        }
    }

    private async Task<T> InvokeAsyncGenericMethod<T>(MethodInvocationMessage invocation)
    {
        if (worker == null ||config == null || logger == null)
            throw new InvalidOperationException("Proxy not initialized");
        
        logger.LogDebug("Invoking async generic method {MethodName} with return type {ReturnType}", invocation.MethodName, typeof(T).FullName);
        var result = await worker.InvokeMethodAsync(invocation);

        if (!result.Success)
        {
            throw new RemoteInvocationException(
                result.ExceptionType ?? "Unknown",
                (result.ExceptionMessage ?? "Method failed") + ", Remote Stack Trace: " + (result.StackTrace ?? "No stack trace"),
                result.StackTrace);
        }
        logger.LogDebug("Deserializing result for method {MethodName}", invocation.MethodName);

        var returnResult = (T)SerializationHelper.DeserializeReturnValue(result.SerializedResult, typeof(T))!;

        logger.LogDebug("Deserialized result for method {MethodName}, returnResult {ReturnResult}", invocation.MethodName, returnResult);

        return returnResult;
    }
}