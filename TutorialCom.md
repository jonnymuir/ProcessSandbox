# Running a 32 bit Com Object

In this guide, we will write a modern **.NET 10 Web API**. I am coding on a Mac. This API will control a legacy **32-bit COM Object**. Since we cannot run 32-bit COM on macOS, we will deploy to **Azure App Service (Free Tier)** to watch it work.

## Phase 1: Project Setup (VS Code Terminal)

We will set up the structure. Note that we can *build* .NET Framework 4.8 libraries on a Mac using the .NET SDK, even if we can't run them.

```bash
# 1. Create the folder structure
mkdir ComSandboxDemo
cd ComSandboxDemo

# 2. Create the solution
dotnet new sln

# 3. Create the "Host" app (The Azure Web API)
dotnet new webapi -n AzureSandboxHost -f net10.0

# 4. Create the "Legacy Library" 
# We create it as standard 2.0 first to satisfy the Mac CLI
dotnet new classlib -n LegacyLibrary -f netstandard2.0

# 5. Create the Contracts (Shared Interface)
dotnet new classlib -n Contracts -f netstandard2.0

# 6. Link projects to solution
dotnet sln add AzureSandboxHost/AzureSandboxHost.csproj
dotnet sln add LegacyLibrary/LegacyLibrary.csproj
dotnet sln add Contracts/Contracts.csproj

# 7. Add References
dotnet add AzureSandboxHost/AzureSandboxHost.csproj reference Contracts/Contracts.csproj
dotnet add LegacyLibrary/LegacyLibrary.csproj reference Contracts/Contracts.csproj
dotnet add AzureSandboxHost/AzureSandboxHost.csproj reference LegacyLibrary/LegacyLibrary.csproj

# 8. Install ProcessSandbox packages
dotnet add AzureSandboxHost/AzureSandboxHost.csproj package ProcessSandbox.Runner --prerelease
dotnet add Contracts/Contracts.csproj package ProcessSandbox.Abstractions --prerelease

```

### Manually change Legacy Library to net48

Open `LegacyLibrary/LegacyLibrary.csproj` in VS Code.

Change `<TargetFramework>netstandard2.0</TargetFramework>` to `<TargetFramework>net48</TargetFramework>`.

It should look like this:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net48</TargetFramework> 
    <LangVersion>latest</LangVersion>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="..\Contracts\Contracts.csproj" />
  </ItemGroup>
</Project>
```

## Phase 2: The "Hidden" Manifest Strategy

The `ProcessSandbox` package unpacks its worker executable into a deep folder: `workers/net48/win-x86/ProcessSandbox.Worker.exe`.

For Registration-Free COM to work, our manifest **must** sit right next to that executable. We will use an MSBuild script to automate this copy.

1. Create `AzureSandboxHost/LegacyLibrary.X.manifest`:

```xml
<?xml version="1.0" encoding="UTF-8" standalone="yes"?>
<assembly xmlns="urn:schemas-microsoft-com:asm.v1" manifestVersion="1.0">
  <assemblyIdentity name="LegacyLibrary.X" version="1.0.0.0" type="win32"/>
  <file name="LegacyLibrary.dll">
    <comClass clsid="{11111111-2222-3333-4444-555555555555}" threadingModel="Both" progid="Legacy.Calculator" />
  </file>
</assembly>

```

2. **The Critical Step:** Open `AzureSandboxHost/AzureSandboxHost.csproj` and add this logic at the end (before `</Project>`). This ensures the manifest lands in the right spot during Build and Publish.

```xml
  <ItemGroup>
    <None Include="LegacyLibrary.X.manifest" />
  </ItemGroup>

  <Target Name="CopyManifestToWorkerFolder" AfterTargets="Build;Publish">
    <PropertyGroup>
      <ActualDestination Condition="'$(PublishDir)' != ''">$(PublishDir)</ActualDestination>
      <ActualDestination Condition="'$(PublishDir)' == ''">$(OutputPath)</ActualDestination>

      <WorkerPath>$(ActualDestination)workers/net48/win-x86</WorkerPath>
      <ManifestName>ProcessSandbox.Worker.exe.manifest</ManifestName>
    </PropertyGroup>

    <Message Text="Deploying COM Manifest to: $(WorkerPath)" Importance="high" />

    <MakeDir Directories="$(WorkerPath)" />

    <Copy SourceFiles="LegacyLibrary.X.manifest"
      DestinationFiles="$(WorkerPath)/$(ManifestName)"
      OverwriteReadOnlyFiles="true" />
  </Target>

```

## Phase 3: The Code (Contracts & Legacy)

**1. Contracts (`Contracts/ICalculator.cs`):**

```csharp
namespace Contracts;

/// <summary>
/// A simple calculator interface
/// </summary>
public interface ICalculator
{
    /// <summary>
    /// Adds two integers
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    int Add(int a, int b);
    /// <summary>
    /// Gets system information
    /// </summary>
    /// <returns></returns>
    string GetSystemInfo();
}

```

**2. Legacy Library (`LegacyLibrary/LegacyService.cs`):**

```csharp
using System;
using System.Runtime.InteropServices;
using Contracts;

namespace LegacyLibrary;

/// <summary>
/// The COM-visible Calculator class
/// </summary>
[ComVisible(true)]
[Guid("11111111-2222-3333-4444-555555555555")]
[ProgId("Legacy.Calculator")]
public class Calculator
{
    /// <summary>
    /// Adds two integers
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b) => a + b;
}

/// <summary>
/// A legacy service that uses the COM Calculator internally
/// </summary>
public class LegacyService : ICalculator
{
    /// <summary>
    /// Adds two integers using the COM Calculator
    /// </summary>
    /// <param name="a"></param>
    /// <param name="b"></param>
    /// <returns></returns>
    public int Add(int a, int b)
    {
        // Simple instantiation inside the 32-bit process
        var com = new Calculator();
        return com.Add(a, b);
    }

    /// <summary>
    /// Gets system information
    /// </summary>
    /// <returns></returns>
    public string GetSystemInfo()
    {
        return $"OS: {Environment.OSVersion} | 64Bit: {Environment.Is64BitProcess} | Ver: {Environment.Version}";
    }
}

```

## Phase 4: The Host (Azure Web API)

Open `AzureSandboxHost/Program.cs`.

```csharp
using Contracts;
using ProcessSandbox.Pool;
using ProcessSandbox.Proxy;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

// 1. Configure the Sandbox
var config = new ProcessPoolConfiguration
{
    // Ensure we use the 32-bit .NET 4.8 Worker
    DotNetVersion = DotNetVersion.Net48_32Bit, 
    MinPoolSize = 1,
    MaxPoolSize = 2,
    
    // We point to the DLL. ProcessSandbox handles the tricky pathing, 
    // and our MSBuild script handled the Manifest.
    ImplementationAssemblyPath = Path.Combine(AppContext.BaseDirectory, "LegacyLibrary.dll"),
    ImplementationTypeName = "LegacyLibrary.LegacyService"
};

var loggerFactory = LoggerFactory.Create(b => b.AddConsole());

// 2. Create the Proxy Factory
var proxy = await ProcessProxy.CreateAsync<ICalculator>(config, loggerFactory);

app.MapGet("/", async () => 
{
    try 
    {
        // This will FAIL locally on Mac (because Windows workers can't start),
        // but will SUCCEED when deployed to Azure.
        var result = proxy.Add(50, 50);
        var info = proxy.GetSystemInfo();

        return Results.Ok(new { Result = result, Info = info });
    }
    catch (Exception ex)
    {
        return Results.Problem($"Sandbox Error: {ex.Message}");
    }
});

app.Run();

```

### Phase 5: Deploy to Azure (Free Tier)

Now for the payoff. We will push this Mac-built code to a Windows server.

1. **Install Azure CLI** (if you haven't):
```bash
brew install azure-cli
az login

```


2. **Deploy from Terminal:**
We use `az webapp up` which handles creating the Resource Group and App Service plan automatically.
**Important:** We must specify `--os-type Windows` because the default for .NET 10/Core is often Linux, but we *need* Windows to run the Net48 worker.
```bash
# Run this in the /ComSandboxDemo folder
az webapp up --sku F1 --name my-unique-sandbox-app --os-type Windows --location westeurope

```

*(Replace `my-unique-sandbox-app` with a unique name).*

3. **Wait for Deployment:**
Azure will bundle your code, upload it, build it remotely (or use your local build), and start the site.

If this fails you may need publish and zip up the site manually and add it. Grab hold of the resource group name the previous step created

Clean and publish locally

```bash
# 1. Clean old artifacts
dotnet clean

# 2. Publish the Host (this also triggers the MSBuild script for the manifest)
dotnet publish AzureSandboxHost/AzureSandboxHost.csproj -c Release -o ./publish
```

Zip the output - you need to zip the contents of the publish folder, not the folder itself

```bash
cd publish
zip -r ../site.zip *
cd ..
```

Push to Azure

```bash
az webapp deployment source config-zip \
    --resource-group replace_with_resource_group_name \
    --name my-unique-sandbox-app \
    --src site.zip
```

Ensure the App Service itself allows 32-bit processes

```bash
az webapp config set \
    --resource-group replace_with_resource_group_name \
    --name my-unique-sandbox-app \
    --use-32bit-worker-process true
```

## What you will see

If you run `dotnet run` locally on your Mac, it will crash when you hit the endpoint because `ProcessSandbox` cannot find `ProcessSandbox.Worker.exe` (it doesn't exist on macOS).

However, navigate to your Azure URL (`https://my-unique-sandbox-app.azurewebsites.net`):

```json
{
  "result": 100,
  "info": "OS: Microsoft Windows NT 10.0.14393.0 | 64Bit: False | Ver: 4.8.4645.0"
}

```