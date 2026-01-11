using System;

namespace ProcessSandbox.Pool;

/// <summary>
/// Framework versions for the worker process.
/// </summary>
public enum DotNetVersion
{
    /// <summary>
    /// .NET 8.0
    /// </summary>
    Net8_0,
    /// <summary>
    /// .NET 10.0
    /// </summary>
    Net10_0,
    /// <summary>
    /// .NET Framework 4.8
    /// </summary>
    Net48,
    /// <summary>
    /// .NET Framework 4.8 32-bit
    /// </summary>
    Net48_32Bit
}   

/// <summary>
/// COM dependency information.
/// </summary>
public class ComDependency
{
    /// <summary>
    /// COM CLSID of the dependency.
    /// </summary>
    public Guid Clsid { get; set; }
    
    /// <summary>
    /// Full path  of the COM DLL.
    /// </summary>
    public string DllPath { get; set; } = string.Empty;
}

/// <summary>
/// Configuration options for the process pool.
/// </summary>
public class ProcessPoolConfiguration
{
    /// <summary>
    /// Gets or sets the minimum number of worker processes to keep in the pool.
    /// </summary>
    public int MinPoolSize { get; set; } = 1;

    /// <summary>
    /// Gets or sets the maximum number of worker processes allowed in the pool.
    /// </summary>
    public int MaxPoolSize { get; set; } = 5;

    /// <summary>
    /// Gets or sets the path to the worker executable.
    /// If not specified, uses the bundled worker for the current framework.
    /// </summary>
    public string? WorkerExecutablePath { get; set; }

    /// <summary>
    /// Gets or sets the DotNetVersion
    /// </summary>
    public DotNetVersion DotNetVersion { get; set; } = DotNetVersion.Net10_0;

    /// <summary>
    /// Gets or sets the path to the assembly containing the implementation.
    /// </summary>
    public string ImplementationAssemblyPath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the full type name of the implementation class.
    /// </summary>
    public string ImplementationTypeName { get; set; } = string.Empty;

    /// <summary>
    /// COM CLSID of the native COM object to load (if applicable).
    /// </summary>
    public Guid ComClsid { get; set; }

    /// <summary>
    /// Gets or sets extra COM dependencies to register in each worker process.
    /// </summary>
    public List<ComDependency> ExtraComDependencies { get; set; } = [];

    /// <summary>
    /// Gets or sets the maximum memory usage in megabytes before recycling a worker.
    /// </summary>
    public long MaxMemoryMB { get; set; } = 1024;

    /// <summary>
    /// Gets or sets the maximum number of GDI objects before recycling a worker.
    /// </summary>
    public int MaxGdiHandles { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the maximum number of USER objects before recycling a worker.
    /// </summary>
    public int MaxUserHandles { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the maximum total handle count before recycling a worker.
    /// </summary>
    public int MaxTotalHandles { get; set; } = 10000;

    /// <summary>
    /// Gets or sets the number of method calls before automatically recycling a worker.
    /// Set to 0 to disable call-count-based recycling.
    /// </summary>
    public int ProcessRecycleThreshold { get; set; } = 0;

    /// <summary>
    /// Gets or sets the maximum lifetime of a worker process before recycling.
    /// </summary>
    public TimeSpan MaxProcessLifetime { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// Gets or sets the timeout for method invocations.
    /// </summary>
    public TimeSpan MethodCallTimeout { get; set; } = TimeSpan.FromSeconds(300);

    /// <summary>
    /// Gets or sets the timeout for starting a worker process.
    /// </summary>
    public TimeSpan ProcessStartTimeout { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Gets or sets whether to enable verbose logging in worker processes.
    /// </summary>
    public bool VerboseWorkerLogging { get; set; } = false;

    /// <summary>
    /// Gets or sets the number of calls after which a worker should be check to be recycled.
    /// </summary>
    public int RecycleCheckCalls { get; set; } = 100;

    /// <summary>
    /// Gets or sets the number of seconds between recycle checks.
    /// </summary>
    public int RecycleCheckSeconds { get; set; } = 10;
    
    /// <summary>
    /// If true you get a new instance of each run, if false the same instance will be reused.
    /// </summary>
    public bool NewInstancePerProxy { get; set; } = true;



    /// <summary>
    /// Validates the configuration and throws if invalid.
    /// </summary>
    /// <exception cref="Abstractions.ConfigurationException">Thrown if configuration is invalid.</exception>
    public void Validate()
    {
        if (MinPoolSize < 0)
            throw new Abstractions.ConfigurationException("MinPoolSize must be >= 0");

        if (MaxPoolSize < 1)
            throw new Abstractions.ConfigurationException("MaxPoolSize must be >= 1");

        if (MinPoolSize > MaxPoolSize)
            throw new Abstractions.ConfigurationException("MinPoolSize cannot exceed MaxPoolSize");

        if (string.IsNullOrWhiteSpace(ImplementationAssemblyPath))
            throw new Abstractions.ConfigurationException("ImplementationAssemblyPath is required");

        if (MaxMemoryMB <= 0)
            throw new Abstractions.ConfigurationException("MaxMemoryMB must be positive");

        if (MaxGdiHandles <= 0)
            throw new Abstractions.ConfigurationException("MaxGdiHandles must be positive");

        if (MaxUserHandles <= 0)
            throw new Abstractions.ConfigurationException("MaxUserHandles must be positive");

        if (MaxTotalHandles <= 0)
            throw new Abstractions.ConfigurationException("MaxTotalHandles must be positive");

        if (ProcessRecycleThreshold < 0)
            throw new Abstractions.ConfigurationException("ProcessRecycleThreshold must be >= 0");

        if (MaxProcessLifetime <= TimeSpan.Zero)
            throw new Abstractions.ConfigurationException("MaxProcessLifetime must be positive");

        if (MethodCallTimeout <= TimeSpan.Zero)
            throw new Abstractions.ConfigurationException("MethodCallTimeout must be positive");

        if (ProcessStartTimeout <= TimeSpan.Zero)
            throw new Abstractions.ConfigurationException("ProcessStartTimeout must be positive");
    }
}