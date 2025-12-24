# ProcessSandbox

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](https://opensource.org/licenses/MIT)
[![.NET](https://img.shields.io/badge/.NET-10.0%20%7C%208.0%20%7C%204.8-512BD4)](https://dotnet.microsoft.com/)

Process isolation library for .NET that protects your application from legacy, unmanaged, or problematic code by running it in separate sandboxed processes.

## The Problem

You have legacy code that:
- Calls into unmanaged COM objects or native DLLs
- Has resource leaks (memory, handles, GDI objects)
- Is single-threaded but your app is multi-threaded
- Occasionally crashes or hangs
- You can't easily modify or replace

**ProcessSandbox solves this** by running the problematic code in isolated worker processes with automatic resource monitoring, lifecycle management, and transparent proxying.

## Jump straight to the live demo

<a href="https://com-sandbox-demo-app.azurewebsites.net" target="_blank" rel="noopener noreferrer">live demo</a>

![Calculator showing it running via the C Com Object](image-1.png)

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

See the [tutorial](Tutorial.md) for an easy to follow example of how ProcessSandbox.Runner works.

See the [com tutorial](TutorialCom.md) to see an example of how to call a 32 bit com object in an Azure App Service.

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

## Building

```bash
git clone https://github.com/yourusername/ProcessSandbox.git
cd ProcessSandbox
dotnet build
dotnet test
```

## Documentation

- [Design Document](./docs/DESIGN.md)

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

- [ ] Call com objects directly just via an interface contract. No need for the manifest or an intermediate c# wrapper. This would need to use the COM Binary Interface (the VTable) but skip the COM Infrastructure (the Registry and the Service Control Manager).
- [ ] Circuit breaker pattern
- [ ] Dynamic pool scaling
- [ ] Telemetry/metrics export
- [ ] Request priority queues

## Support

- 🐛 [Report issues](https://github.com/jonnymuir/ProcessSandbox/issues)
- 💬 [Discussions](https://github.com/jonnymuir/ProcessSandbox/discussions)
- 📧 Email: jonnypmuir@gmail.com

## Useful tips when building

If I'm having to make changes to ProcessSandbox and write an end to end solution which uses it from packages, I find it easier to test from a local nuget server rather than having to wait for nuget to publish new versions.

To do this set up a local nuget server e.g.

```bash
dotnet nuget add source ~/LocalNuGetFeed --name LocalTestFeed --at-position 1
```

Then to build all the packages and put them in the local feed you can do the following (from the root of ProcessSanbox - e.g. where the ProcessSandbox.sln is)
```bash
dotnet build --configuration Release
dotnet build src/ProcessSandbox.Worker/ProcessSandbox.Worker.csproj -c Release -f net48 -r win-x86
dotnet pack /p:ExcludeProjects="**/ProcessSandbox.Worker.csproj" --configuration Release --no-build --output push-ready-artifacts
dotnet pack src/ProcessSandbox.Worker/ProcessSandbox.Worker.nuspec --configuration Release --output push-ready-artifacts -p:NoWarn=NU5100
cp push-ready-artifacts/* ~/LocalNuGetFeed
```

Remember to clear the local cache when you want to use them

```bash
dotnet nuget locals all --clear
```

And remember to get rid of from the local package source if you really want to pick them up from nuget.

```bash
rm ~/LocalNuGetFeed/*
```

### Deploying the com tutorials to azure

In order to build to SimpleCom object, you need a c compiler. On a mac you can do

```bash
brew install mingw-w64
```

And then to build you need to
```bash
i686-w64-mingw32-gcc -shared -static -o publish/workers/net48/win-x86/SimpleCom.dll SimpleCom/SimpleCom.c -lole32 -loleaut32 -lpsapi -Wl,--add-stdcall-alias
```

Replace jonnymoo_rg_9172 with the name of your resource group and com-sandbox-demo-app with the name of you web app (from src/tutorials/ComSandboxDemo)

```bash
dotnet clean
rm -rf publish
rm site.zip
dotnet nuget locals all --clear
dotnet publish AzureSandboxHost/AzureSandboxHost.csproj -c Release -o ./publish
i686-w64-mingw32-gcc -shared -static -o publish/workers/net48/win-x86/SimpleCom.dll SimpleCom/SimpleCom.c -lole32 -loleaut32 -lpsapi -Wl,--add-stdcall-alias
cd publish
zip -r ../site.zip *
cd ..
az webapp deployment source config-zip --resource-group jonnymoo_rg_9172 --name com-sandbox-demo-app --src site.zip
```

Remember if you want to deploy SimpleComDelphi.dll - you can get it from the artifacts on the github build. You will have to manually go put it onto azure in site/wwwroot/workers/net48/win-x86