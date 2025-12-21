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

var loggerFactory = LoggerFactory.Create(b => 
{
    b.AddConsole();
    b.SetMinimumLevel(LogLevel.Debug);
});

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
        var errorDetails = new
        {
            ex.Message,
            Type = ex.GetType().Name,
            ex.StackTrace,
            InnerException = ex.InnerException != null ? new {
                ex.InnerException.Message,
                ex.InnerException.StackTrace
            } : null
        };
        
        return Results.Json(errorDetails, statusCode: 500);
    }
});

app.Run();
