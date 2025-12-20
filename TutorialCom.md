# Tutorial - Calling a com object

In this guide, we will create a **32-bit COM Object** and a **Web API Host**. We will deploy this to Azure App Service (Free Tier) using **Registration-Free COM** (Side-by-Side) to bypass the need for Docker or Windows Registry access.

## Phase 1: Project Setup

We need a specific structure: a 32-bit COM library, a Wrapper (the Worker), and the Web API Host.

Run these commands in your VS Code terminal:

```bash
# 1. Create folder structure
mkdir ComSandboxDemo
cd ComSandboxDemo
dotnet new sln

# 2. Create the "Legacy" COM Object (The 32-bit Code)
dotnet new classlib -n LegacyComServer -f net8.0

# 3. Create the Contracts (Shared Interfaces)
dotnet new classlib -n Contracts -f netstandard2.0

# 4. Create the Web API Host (The Azure App)
dotnet new webapi -n AzureSandboxHost

# 5. Link projects
dotnet sln add LegacyComServer/LegacyComServer.csproj
dotnet sln add Contracts/Contracts.csproj
dotnet sln add AzureSandboxHost/AzureSandboxHost.csproj

# 6. References
dotnet add AzureSandboxHost/AzureSandboxHost.csproj reference Contracts/Contracts.csproj
dotnet add AzureSandboxHost/AzureSandboxHost.csproj package ProcessSandbox.Runner --prerelease

dotnet add Contracts/Contracts.csproj package ProcessSandbox.Abstractions --prerelease

# 7. IMPORTANT: We need the ComServer to be referenceable for build, 
# but at runtime, we will load it via COM, not .NET references.
dotnet add AzureSandboxHost/AzureSandboxHost.csproj reference LegacyComServer/LegacyComServer.csproj

```

### Phase 2: Create the 32-bit COM Object

We need to force this project to be **x86** and expose it as a COM object.

1. Open `LegacyComServer/LegacyComServer.csproj`.
2. Add `<EnableComHosting>true</EnableComHosting>` and `<PlatformTarget>x86</PlatformTarget>`.

It should look like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <PlatformTarget>x86</PlatformTarget>
    <EnableComHosting>true</EnableComHosting>
    <GenerateRuntimeConfigurationFiles>true</GenerateRuntimeConfigurationFiles>
    <CopyLocalLockFileAssemblies>true</CopyLocalLockFileAssemblies>
  </PropertyGroup>
</Project>

```

3. Create `LegacyComServer/Calculator.cs`. We will use a fixed GUID so we can find it later.

```csharp
using System.Runtime.InteropServices;

namespace LegacyComServer;

[ComVisible(true)]
[Guid("11111111-2222-3333-4444-555555555555")] // The "Registry" Key
[ProgId("Legacy.Calculator")]
public class Calculator
{
    public int Add(int a, int b)
    {
        // Prove we are in 32-bit mode
        if (IntPtr.Size != 4) throw new Exception("I am not running in 32-bit!");
        return a + b;
    }
}

```

### Phase 3: The "Magic" Manifest

Since we cannot run `regsvr32` on Azure App Service, we must use a **Manifest** to tell Windows where to find our COM object.

Create a new file `AzureSandboxHost/LegacyComServer.X.manifest` (Note: Put this in the Host project so it copies to output).

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity name="LegacyComServer.X" version="1.0.0.0" type="win32"/>
  <file name="LegacyComServer.comhost.dll">
    <comClass clsid="{11111111-2222-3333-4444-555555555555}" threadingModel="Both" progid="Legacy.Calculator" />
  </file>
</assembly>

```

*Tip: In VS Code, ensure this file is copied to the build output. Open `AzureSandboxHost.csproj` and add:*

```xml
<ItemGroup>
  <None Update="LegacyComServer.X.manifest">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>

```

### Phase 4: The Adapter (Worker Code)

We need a class that `ProcessSandbox` will load. This class will perform the COM Activation.

Create `LegacyComServer/ComAdapter.cs` (Keeping it in the same project for simplicity, though usually, this would be separate):

```csharp
using System.Reflection;
using System.Runtime.InteropServices;
using ProcessSandbox.Abstractions;

namespace LegacyComServer;

// This is the class ProcessSandbox talks to
public class ComAdapter
{
    public int AddNumbers(int a, int b)
    {
        // 1. Activate COM object using the GUID we defined
        // Because of the manifest, Windows finds it without Registry lookup!
        var type = Type.GetTypeFromCLSID(Guid.Parse("11111111-2222-3333-4444-555555555555"));
        
        if (type == null) throw new Exception("COM Class not found in manifest!");

        dynamic calculator = Activator.CreateInstance(type);

        // 2. Call the method
        return calculator.Add(a, b);
    }
}

```

### Phase 5: The Azure Host API

Open `AzureSandboxHost/Program.cs` and replace with this web setup:

```csharp
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;
using LegacyComServer; // Reference for types, but loaded via Sandbox

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 1. Configure the 32-bit Sandbox
var config = new ProcessPoolConfiguration
{
    MinPoolSize = 1,
    MaxPoolSize = 2,
    
    // Point to the 32-bit DLL
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "LegacyComServer.dll"),
    ImplementationTypeName = "LegacyComServer.ComAdapter",
    
    // FORCE 32-BIT: This tells ProcessSandbox to use the 32-bit runner
    ExecutablePath = ProcessSandbox.Helpers.PathHelper.GetNetCoreWorkerPath(is32Bit: true)
};

// 2. Register the pool globally
var pool = new ProcessPool(config, app.Services.GetRequiredService<ILoggerFactory>());
await pool.StartAsync();

app.MapGet("/", async () => 
{
    try 
    {
        // 3. Acquire a worker
        using var slot = await pool.GetSlotAsync();
        
        // 4. Talk to the 32-bit COM object
        // Note: We use dynamic here for simplicity of the tutorial
        // In real life, use the Interface defined in Contracts
        dynamic proxy = slot.CreateProxy<ComAdapter>();
        
        int result = await proxy.AddNumbers(10, 20);
        
        return Results.Ok(new { 
            Status = "Success", 
            Calculation = $"10 + 20 = {result}",
            WorkerPid = slot.ProcessId,
            Mode = "32-bit COM via Registration-Free Activation"
        });
    }
    catch (Exception ex)
    {
        return Results.Problem(ex.Message);
    }
});

app.Run();

```

### Phase 6: Deployment to Azure Free Tier

To make this work on Azure without containers, we need to ensure the **Application Manifest** is respected.

1. **Publish the App:**
```bash
dotnet publish AzureSandboxHost -c Release -o ./publish

```


2. **The Secret Sauce:**
For RegFree COM to work, the *executable* (`ProcessSandbox.Worker.exe` or `dotnet.exe`) needs to know about the manifest.
Since `ProcessSandbox` launches a worker process, we need to rename our manifest to match the worker executable name, or simply ensure they are in the same folder.
*Manual Fix for Tutorial:* Copy `LegacyComServer.X.manifest` to `ProcessSandbox.Worker.x86.manifest` inside the publish folder.
```bash
# (Inside /publish folder)
cp LegacyComServer.X.manifest ProcessSandbox.Worker.x86.exe.manifest

```


*(Note: ProcessSandbox uses `ProcessSandbox.Worker.x86.exe` when `is32Bit: true` is requested).*
3. **Deploy to Azure:**
Use the Azure extension in VS Code or CLI:
```bash
az webapp up --sku F1 --name my-sandbox-demo --os-type Windows

```


4. **Azure Configuration (Crucial):**
Go to the Azure Portal -> Your App -> **Configuration** -> **General Settings**.
* **Platform:** 32 Bit (This is default for Free Tier, but good to check).



### What you will see

When you navigate to `https://my-sandbox-demo.azurewebsites.net`:

1. **Browser Output:**
```json
{
  "status": "Success",
  "calculation": "10 + 20 = 30",
  "workerPid": 4056,
  "mode": "32-bit COM via Registration-Free Activation"
}

```


2. **Behind the Scenes:**
* Your Web API (Host) received the request.
* It spun up a **32-bit** child process (`ProcessSandbox.Worker.exe`).
* That child process read the `.manifest` file.
* It loaded `LegacyComServer.comhost.dll` **without looking in the registry**.
* It executed the code and returned the result.



This video gives a deeper dive into how Registration-Free COM works, which is the core mechanic enabling this "Free Tier" architecture.
[Registration Free COM Explained](https://www.google.com/search?q=https://www.youtube.com/watch%3Fv%3DD-Xlsm8nI4o)