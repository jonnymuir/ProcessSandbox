using System.Collections.Concurrent;
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
    /// Whether to enable verbose logging in the worker.
    /// </summary>
    [Key(3)]
    public bool VerboseLogging { get; set; }

    /// <summary>
    /// Process ID of the parent process (for monitoring).
    /// </summary>
    [Key(4)]
    public int ParentProcessId { get; set; }
    
    /// <summary>
    /// COM CLSID of the native COM object to load (if applicable).
    /// </summary>
    [Key(5)]
    public Guid ComClsid { get; set; }
    
    
    /// <summary>
    /// Extra COM dependencies to register in the worker process (comma separated "DLLPath:CLSID").
    /// </summary>
    [Key(6)]
    public string ExtraComDependencies { get; set; } = string.Empty;

    /// <summary>
    /// If true you get a new instance of each run, if false the same instance will be reused.
    /// </summary>
    [Key(7)]
    public bool NewInstancePerProxy {get; set; } = true;

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

        if (ParentProcessId <= 0)
            throw new ConfigurationException("ParentProcessId must be positive");
    }
}