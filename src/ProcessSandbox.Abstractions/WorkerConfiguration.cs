using MessagePack;

namespace ProcessSandbox.Abstractions;

/// <summary>
/// Configuration passed to worker process on startup.
/// </summary>
[MessagePackObject]
public class WorkerConfiguration
{
    /// <summary>
    /// Path to the assembly containing the implementation.
    /// </summary>
    [Key(0)]
    public string AssemblyPath { get; set; } = string.Empty;

    /// <summary>
    /// Full type name of the implementation class.
    /// </summary>
    [Key(1)]
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Name of the named pipe for IPC communication.
    /// </summary>
    [Key(2)]
    public string PipeName { get; set; } = string.Empty;

    /// <summary>
    /// Interval in milliseconds for sending health reports.
    /// </summary>
    [Key(3)]
    public int HealthReportIntervalMs { get; set; } = 5000;

    /// <summary>
    /// Whether to enable verbose logging in the worker.
    /// </summary>
    [Key(4)]
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Process ID of the parent process (for monitoring).
    /// </summary>
    [Key(5)]
    public int ParentProcessId { get; set; }

    /// <summary>
    /// Validates the configuration.
    /// </summary>
    /// <exception cref="ConfigurationException">Thrown if configuration is invalid.</exception>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(AssemblyPath))
            throw new ConfigurationException("AssemblyPath is required");

        if (string.IsNullOrWhiteSpace(TypeName))
            throw new ConfigurationException("TypeName is required");

        if (string.IsNullOrWhiteSpace(PipeName))
            throw new ConfigurationException("PipeName is required");

        if (HealthReportIntervalMs <= 0)
            throw new ConfigurationException("HealthReportIntervalMs must be positive");

        if (ParentProcessId <= 0)
            throw new ConfigurationException("ParentProcessId must be positive");
    }
}