using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Worker;

// Worker process entry point
// Expected args: --config <base64-encoded-config>

if (args.Length == 0 || args[0] != "--config")
{
    Console.Error.WriteLine("Usage: ProcessSandbox.Worker --config <base64-config>");
    return 1;
}

try
{
    // Decode configuration
    var configBase64 = args[1];
    var configBytes = Convert.FromBase64String(configBase64);
    var config = MessagePack.MessagePackSerializer.Deserialize<WorkerConfiguration>(configBytes);
    
    // Validate configuration
    config.Validate();

    // Set up logging
    using var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder.AddConsole();
        builder.SetMinimumLevel(config.VerboseLogging ? LogLevel.Debug : LogLevel.Information);
    });

    var logger = loggerFactory.CreateLogger("Worker");
    logger.LogInformation("Worker process starting...");
    logger.LogInformation("Assembly: {Assembly}", config.AssemblyPath);
    logger.LogInformation("Type: {Type}", config.TypeName);
    logger.LogInformation("Pipe: {Pipe}", config.PipeName);

    // Create and run worker host
    using var workerHost = new WorkerHost(config, loggerFactory);
    
    // Monitor parent process
    var parentMonitor = Task.Run(async () =>
    {
        try
        {
            var parentProcess = System.Diagnostics.Process.GetProcessById(config.ParentProcessId);
            await parentProcess.WaitForExitAsync();
            logger.LogWarning("Parent process exited, shutting down worker");
            await workerHost.StopAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error monitoring parent process");
        }
    });

    // Run the worker (blocks until shutdown)
    await workerHost.RunAsync();
    
    logger.LogInformation("Worker process exiting normally");
    return 0;
}
catch (ConfigurationException ex)
{
    Console.Error.WriteLine($"Configuration error: {ex.Message}");
    return 2;
}
catch (AssemblyLoadException ex)
{
    Console.Error.WriteLine($"Assembly load error: {ex.Message}");
    return 3;
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Fatal error: {ex.Message}");
    Console.Error.WriteLine(ex.StackTrace);
    return 99;
}