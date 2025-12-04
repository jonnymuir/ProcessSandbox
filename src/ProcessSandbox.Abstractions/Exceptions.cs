using System;

namespace ProcessSandbox.Abstractions;

/// <summary>
/// Base exception for all ProcessSandbox errors.
/// </summary>
public class ProcessSandboxException : Exception
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public ProcessSandboxException(string message) : base(message)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    /// <param name="innerException"></param>
    public ProcessSandboxException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a worker process fails to start.
/// </summary>
public class WorkerStartupException : ProcessSandboxException
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public WorkerStartupException(string message) : base(message)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    /// <param name="innerException"></param>
    public WorkerStartupException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a worker process crashes or becomes unresponsive.
/// </summary>
public class WorkerCrashedException : ProcessSandboxException
{
    /// <summary>
    /// The exit code of the crashed process, if available.
    /// </summary>
    public int? ExitCode { get; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public WorkerCrashedException(string message) : base(message)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    /// <param name="exitCode"></param>
    public WorkerCrashedException(string message, int exitCode) : base(message)
    {
        ExitCode = exitCode;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    /// <param name="innerException"></param>
    public WorkerCrashedException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when a method call times out.
/// </summary>
/// <remarks>
/// 
/// </remarks>
/// <param name="methodName"></param>
/// <param name="timeout"></param>
public class MethodTimeoutException(string methodName, TimeSpan timeout) : ProcessSandboxException($"Method '{methodName}' timed out after {timeout.TotalSeconds:F1} seconds")
{
    /// <summary>
    /// The timeout duration that was exceeded.
    /// </summary>
    public TimeSpan Timeout { get; } = timeout;

    /// <summary>
    /// The name of the method that timed out.
    /// </summary>
    public string MethodName { get; } = methodName;
}

/// <summary>
/// Exception thrown when IPC communication fails.
/// </summary>
public class IpcException : ProcessSandboxException
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public IpcException(string message) : base(message)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    /// <param name="innerException"></param>
    public IpcException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when the process pool is exhausted and cannot handle more requests.
/// </summary>
/// <remarks>
/// 
/// </remarks>
/// <param name="maxPoolSize"></param>
public class PoolExhaustedException(int maxPoolSize) : ProcessSandboxException($"Process pool exhausted. All {maxPoolSize} workers are busy.")
{
    /// <summary>
    /// The maximum pool size that was reached.
    /// </summary>
    public int MaxPoolSize { get; } = maxPoolSize;
}

/// <summary>
/// Exception thrown when worker configuration is invalid.
/// </summary>
public class ConfigurationException : ProcessSandboxException
{
    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public ConfigurationException(string message) : base(message)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    /// <param name="innerException"></param>
    public ConfigurationException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when the target assembly or type cannot be loaded.
/// </summary>
public class AssemblyLoadException : ProcessSandboxException
{
    /// <summary>
    /// The assembly path that failed to load.
    /// </summary>
    public string? AssemblyPath { get; }

    /// <summary>
    /// The type name that failed to load.
    /// </summary>
    public string? TypeName { get; }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    public AssemblyLoadException(string message) : base(message)
    {
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    /// <param name="assemblyPath"></param>
    public AssemblyLoadException(string message, string assemblyPath) : base(message)
    {
        AssemblyPath = assemblyPath;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    /// <param name="assemblyPath"></param>
    /// <param name="typeName"></param>
    public AssemblyLoadException(string message, string assemblyPath, string typeName) 
        : base(message)
    {
        AssemblyPath = assemblyPath;
        TypeName = typeName;
    }

    /// <summary>
    /// 
    /// </summary>
    /// <param name="message"></param>
    /// <param name="innerException"></param>
    public AssemblyLoadException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}

/// <summary>
/// Exception thrown when method invocation fails in the worker process.
/// </summary>
/// <remarks>
/// 
/// </remarks>
/// <param name="remoteExceptionType"></param>
/// <param name="message"></param>
/// <param name="remoteStackTrace"></param>
public class RemoteInvocationException(
    string remoteExceptionType,
    string message,
    string? remoteStackTrace) : ProcessSandboxException($"Remote invocation failed: {remoteExceptionType}: {message}")
{
    /// <summary>
    /// The name of the remote exception type.
    /// </summary>
    public string RemoteExceptionType { get; } = remoteExceptionType;

    /// <summary>
    /// The remote stack trace.
    /// </summary>
    public string? RemoteStackTrace { get; } = remoteStackTrace;
}