# ProcessSandbox

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-8.0%20%7C%204.8-512BD4)](https://dotnet.microsoft.com/)

Process isolation library for .NET that protects your application from legacy, unmanaged, or problematic code by running it in separate sandboxed processes.

## The Problem

You have legacy code that:
- Calls into unmanaged COM objects or native DLLs
- Has resource leaks (memory, handles, GDI objects)
- Is single-threaded but your app is multi-threaded
- Occasionally crashes or hangs
- You can't easily modify or replace

**ProcessSandbox solves this** by running the problematic code in isolated worker processes with automatic resource monitoring, lifecycle management, and transparent proxying.

## Features

- 🛡️ **Process Isolation**: Crashes and leaks don't affect your main application
- 🔄 **Automatic Recycling**: Workers recycled based on memory, handles, or call count
- 🎯 **Interface-Based Proxy**: Use your interfaces naturally, calls routed transparently
- ⚡ **High Performance**: Named pipes + MessagePack for fast IPC
- 🔧 **32/64-bit Support**: Run 32-bit workers from 64-bit apps (COM interop)
- 🚫 **No Orphans**: Job Objects ensure workers never leak
- 📊 **Resource Monitoring**: Track memory, GDI/USER handles, call counts
- 🧵 **Thread-Safe**: Multiple threads can call concurrently
- 🎚️ **Configurable**: Extensive options for pool size, limits, timeouts

## Quick Start

See the [tutorial](Tutorial.md) for an easy to follow example of how ProcessSandbox.Runner works

### Installation

```bash
dotnet add package ProcessSandbox.Runner
```

### Basic Usage

```csharp
using ProcessSandbox;

// 1. Define your interface
public interface ILegacyService
{
    string ProcessData(string input);
}

// 2. Create implementation (in separate assembly)
public class LegacyServiceImpl : ILegacyService
{
    public string ProcessData(string input)
    {
        // Your legacy/unmanaged code here
        return LegacyComObject.DoWork(input);
    }
}

// 3. Configure and create proxy
var config = new ProcessPoolConfiguration
{
    MaxPoolSize = 5,
    MaxMemoryMB = 1024,
    WorkerAssembly = "MyLegacy.dll",
    WorkerType = "LegacyServiceImpl"
};

using var proxy = ProcessProxy.Create<ILegacyService>(config);

// 4. Use it like any interface - calls run in worker process
string result = proxy.ProcessData("test data");
```

## Configuration

```csharp
var config = new ProcessPoolConfiguration
{
    // Pool sizing
    MinPoolSize = 1,              // Start with 1 worker
    MaxPoolSize = 5,              // Scale up to 5 workers
    
    // Worker process
    DotNetVersion = DotNetVersion.Net48_32Bit,        // Use a 32-bit framework dll for COM
    WorkerAssembly = "MyLegacy.dll",
    WorkerType = "MyLegacy.ServiceImpl",
    
    // Resource limits
    MaxMemoryMB = 1024,           // Recycle at 1GB
    MaxGdiHandles = 10000,        // GDI handle limit
    ProcessRecycleThreshold = 100,// Recycle after 100 calls
    
    // Timeouts
    MethodCallTimeout = TimeSpan.FromSeconds(90),
};
```

## Use Cases

### COM Interop (32-bit)
```csharp
var config = new ProcessPoolConfiguration
{
    DotNetVersion = DotNetVersion.Net48_32Bit,
    MaxMemoryMB = 1024,  // well within 32-bit limit
    MaxGdiHandles = 10000
};
```

### Native DLL Calls
```csharp
var config = new ProcessPoolConfiguration
{
    ProcessRecycleThreshold = 1000,
    MaxProcessLifetime = TimeSpan.FromHours(1)
};
```

### Single-Threaded Legacy Code
```csharp
var config = new ProcessPoolConfiguration
{
    MaxPoolSize = 10,  // Handle concurrent requests
    MethodCallTimeout = TimeSpan.FromMinutes(5)
};
```

## Architecture

```
Your App (.NET) → ProcessProxy<T> → [Worker Pool] → Worker Process
                                        ↓              ↓
                                   Named Pipes    Legacy DLL/COM
```

- **ProcessProxy**: Transparent proxy implementing your interface
- **Worker Pool**: Manages process lifecycle and resource monitoring
- **Worker Process**: Isolated process hosting your implementation
- **IPC**: Named pipes with MessagePack serialization

## Requirements

- Windows (Job Objects for orphan prevention)
- .NET 8.0 / 10.0 or .NET Framework 4.8
- Administrator rights not required

## Building

```bash
git clone https://github.com/yourusername/ProcessSandbox.git
cd ProcessSandbox
dotnet build
dotnet test
```

## Examples

See the [samples](./samples) directory:
- [COM Interop Example](./samples/ComInterop.Sample) - 32-bit COM object usage
- [Native DLL Example](./samples/NativeDll.Sample) - Unmanaged DLL calls
- [Resource Leak Example](./samples/ResourceLeak.Sample) - Handling leaky code

## Documentation

- [Design Document](./docs/DESIGN.md)
- [Configuration Guide](./docs/CONFIGURATION.md)
- [Troubleshooting](./docs/TROUBLESHOOTING.md)
- [API Reference](./docs/API.md)

## Performance

Typical overhead per call:
- Local calls: < 0.1ms
- Process proxy: 1-2ms (named pipes + serialization)

Perfect for scenarios where isolation benefits outweigh small latency cost.

## Contributing

Contributions welcome! Please read [CONTRIBUTING.md](./CONTRIBUTING.md) first.

## License

MIT License - see [LICENSE](./LICENSE) for details.

## Roadmap

- [ ] Linux/macOS support
- [ ] Circuit breaker pattern
- [ ] Dynamic pool scaling
- [ ] Telemetry/metrics export
- [ ] Request priority queues

## Support

- 🐛 [Report issues](https://github.com/jonnymuir/ProcessSandbox/issues)
- 💬 [Discussions](https://github.com/jonnymuir/ProcessSandbox/discussions)
- 📧 Email: jonnypmuir@gmail.com
