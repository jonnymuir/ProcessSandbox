# ProcessSandbox Design Document

## Overview

ProcessSandbox is a .NET library that provides process isolation for legacy, unmanaged, or problematic code. It creates a managed pool of worker processes that handle calls to legacy code, protecting the main application from crashes, resource leaks, and threading issues.

## Architecture

### Components

#### 1. ProcessSandbox (Main Library)
**Target Frameworks:** .NET 8.0, .NET Framework 4.8

**Key Classes:**
- `ProcessProxy<TInterface>`: Main entry point, implements TInterface and routes calls to workers
- `ProcessPool`: Manages worker process lifecycle
- `ProcessPoolConfiguration`: Configuration for pool behavior
- `ResourceMonitor`: Tracks resource usage of worker processes
- `IpcChannel`: Handles named pipe communication

#### 2. ProcessSandbox.Worker (Generic Host)
**Target Frameworks:** .NET 8.0, .NET Framework 4.8 (separate exes)

Generic executable that:
- Loads specified assembly and type at runtime
- Receives method invocation requests via IPC
- Executes methods and returns results
- Reports health metrics

#### 3. ProcessSandbox.Abstractions
**Target Framework:** .NET Standard 2.0

Shared contracts:
- `IMethodInvocation`: Serializable method call representation
- `IMethodResult`: Serializable result representation
- `IHealthReport`: Resource usage metrics

## Interface-Based Proxy Pattern

### Usage Example

```csharp
// Your legacy interface
public interface ILegacyComService
{
    string ProcessData(string input);
    byte[] ConvertFile(byte[] fileData);
}

// Your implementation (in separate assembly)
public class LegacyComServiceImpl : ILegacyComService
{
    public string ProcessData(string input)
    {
        // Calls into COM, native code, etc.
        return ComInterop.DoWork(input);
    }
    
    public byte[] ConvertFile(byte[] fileData)
    {
        // Single-threaded native conversion
        return NativeConverter.Convert(fileData);
    }
}

// In your application
var config = new ProcessPoolConfiguration
{
    MaxPoolSize = 5,
    MaxMemoryMB = 1024,
    MaxGdiHandles = 10000,
    ProcessRecycleThreshold = 100, // calls before recycle
    WorkerExecutable = "LegacyService.Worker.exe",
    WorkerAssembly = "LegacyService.dll",
    WorkerType = "LegacyComServiceImpl"
};

using var proxy = ProcessProxy.Create<ILegacyComService>(config);

// Use like a normal interface - calls are routed to worker process
string result = proxy.ProcessData("test");
```

## Process Pool Management

### Lifecycle

1. **Initialization**
   - Pre-start minimum number of worker processes
   - Attach to Job Object (prevents orphans)
   - Establish named pipe connections

2. **Operation**
   - Route calls to available workers (FIFO queue)
   - Monitor resource usage continuously
   - Recycle processes when thresholds exceeded

3. **Recycling**
   - Graceful shutdown with timeout
   - Force kill if necessary
   - Automatic replacement

4. **Shutdown**
   - Drain pending requests
   - Graceful worker shutdown
   - Job Object cleanup

### Resource Monitoring

Monitor per-process:
- Working Set (physical memory)
- Private Bytes (committed memory)
- GDI Objects count
- USER Objects count
- Handle count
- Uptime / call count

Recycle triggers:
- Memory threshold exceeded
- Handle threshold exceeded
- Call count threshold reached
- Unhandled exception in worker

## IPC Protocol

### Transport: Named Pipes
- One pipe per worker process
- Duplex communication
- Message framing with length prefix

### Message Format (MessagePack)

**Request:**
```csharp
public class MethodInvocationMessage
{
    public Guid CorrelationId { get; set; }
    public string MethodName { get; set; }
    public Type[] ParameterTypes { get; set; }
    public object[] Parameters { get; set; }
}
```

**Response:**
```csharp
public class MethodResultMessage
{
    public Guid CorrelationId { get; set; }
    public bool Success { get; set; }
    public object Result { get; set; }
    public string ExceptionType { get; set; }
    public string ExceptionMessage { get; set; }
    public string StackTrace { get; set; }
}
```

**Health Report:**
```csharp
public class HealthReportMessage
{
    public long WorkingSetBytes { get; set; }
    public long PrivateBytes { get; set; }
    public int GdiObjects { get; set; }
    public int UserObjects { get; set; }
    public int HandleCount { get; set; }
    public int CallCount { get; set; }
}
```

## Process Isolation Features

### 1. Job Objects (Windows)
Prevents orphaned processes:
```csharp
// Pseudo-code
var job = CreateJobObject();
SetInformationJobObject(job, JobObjectExtendedLimitInformation, 
    JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE);
AssignProcessToJobObject(job, workerProcess.Handle);
```

### 2. 32-bit Worker Support
Configuration allows specifying worker architecture:
```csharp
config.DotNetVersion = DotNetVersion.Net48_32Bit; // For COM interop
```

### 3. Resource Limits
- Memory limit enforcement
- Automatic recycle before hitting 2GB on 32-bit
- GDI/USER handle limits

## Configuration Options

```csharp
public class ProcessPoolConfiguration
{
    // Pool sizing
    public int MinPoolSize { get; set; } = 1;
    public int MaxPoolSize { get; set; } = 5;
    
    // Worker executable
    public string WorkerExecutable { get; set; }
    public DotNetVersion DotNetVersion { get; set; } = DotNetVersion.Net10_0;

    // Assembly loading
    public string WorkerAssembly { get; set; }
    public string WorkerType { get; set; }
    
    // Resource limits
    public long MaxMemoryMB { get; set; } = 1024;
    public int MaxGdiHandles { get; set; } = 10000;
    public int MaxUserHandles { get; set; } = 10000;
    public int MaxTotalHandles { get; set; } = 10000;
    
    // Recycling
    public int ProcessRecycleThreshold { get; set; } = 1000;
    public TimeSpan MaxProcessLifetime { get; set; } = TimeSpan.FromHours(1);
    
    // Timeouts
    public TimeSpan MethodCallTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan ProcessStartTimeout { get; set; } = TimeSpan.FromSeconds(10);
    
    // IPC
    public string PipeNamePrefix { get; set; } = "ProcessSandbox";
}
```

## Error Handling

### Worker Process Crashes
- Detect via process exit or broken pipe
- Remove from pool
- Start replacement worker
- Return exception to caller

### Timeouts
- Configurable per-method timeout
- Kill worker on timeout
- Replace with fresh worker

### Resource Exhaustion
- Proactive recycling before hard limits
- Graceful degradation (queue requests)

## Thread Safety

- ProcessPool is thread-safe
- Multiple threads can call proxy simultaneously
- Workers process one request at a time
- FIFO queue for fairness

## Logging & Diagnostics

Integration with `Microsoft.Extensions.Logging`:
- Process start/stop events
- Resource usage warnings
- IPC errors
- Performance metrics

## Future Enhancements

- Linux/macOS support (cgroups instead of Job Objects)
- Circuit breaker pattern
- Request priority queues
- Metrics/telemetry export
- Dynamic pool scaling
- Worker warm-up strategies

## Testing Strategy

1. **Unit Tests**
   - Message serialization
   - Resource monitoring
   - Configuration validation

2. **Integration Tests**
   - Full process lifecycle
   - Error scenarios
   - Resource limit enforcement
   - Concurrent calls

3. **Sample Applications**
   - COM interop example
   - Native DLL example
   - Memory leak simulation
