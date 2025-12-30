using System;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Worker;

// Worker process entry point
// Expected args: --config <base64-encoded-config>

internal class Program
{

    private static readonly List<ManualComRegistration> _registrations = new();

    private static void Main(string[] args)
    {
        // Capture the result using a TaskCompletionSource
        var exitCodeSource = new TaskCompletionSource<int>();

        Thread mainThread = new Thread(() =>
        {
            try
            {
                // Start the async logic and wait for the result
                int result = RunWorkerAsync().GetAwaiter().GetResult();
                exitCodeSource.SetResult(result);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Thread Fatal Error: {ex.Message}");
                exitCodeSource.SetResult(99);
            }
        });

        // Platform check for STA (only matters on Windows)
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            //mainThread.SetApartmentState(ApartmentState.STA);
        }

        mainThread.Start();
        mainThread.Join();

        async Task<int> RunWorkerAsync()
        {

            Console.WriteLine($"[Worker] Apartment State: {Thread.CurrentThread.GetApartmentState()}");

            if (args.Length == 0 || args[0] != "--config")
            {
                Console.Error.WriteLine("Usage: ProcessSandbox.Worker --config <base64-config>");
                return 1;
            }

            using var cts = new CancellationTokenSource();

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

                if (!string.IsNullOrWhiteSpace(config.ExtraComDependencies))
                {
                    foreach (var entry in config.ExtraComDependencies.Split(';'))
                    {
                        var parts = entry.Split('|');
                        if (parts.Length == 2)
                        {
                            string dllPath = parts[0];
                            if (Guid.TryParse(parts[1], out Guid clsid))
                            {
                                logger.LogInformation("In-Memory Registering: {Dll} [{Guid}]", dllPath, clsid);
                                var comReg = new ManualComRegistration();
                                comReg.RegisterDll(dllPath, clsid);
                                _registrations.Add(comReg);
                            }
                        }
                    }
                }

                // Create and run worker host
                using var workerHost = new WorkerHost(config, loggerFactory);

                // Monitor parent process
                _ = Task.Run(async () =>
                {
                    try
                    {
                        // Capture parent identity
                        using var parent = System.Diagnostics.Process.GetProcessById(config.ParentProcessId);
                        var parentStartTime = parent.StartTime;

                        while (!cts.Token.IsCancellationRequested)
                        {
                            parent.Refresh();

                            // If parent exited OR PID was recycled (StartTime changed)
                            if (parent.HasExited || parent.StartTime != parentStartTime)
                            {
                                logger.LogCritical("Parent process {Pid} lost. Emergency shutdown.", config.ParentProcessId);
                                Environment.Exit(0);
                            }

                            await Task.Delay(2000, cts.Token);
                        }
                    }
                    catch (Exception ex)
                    {
                        // ArgumentException means PID is already gone
                        logger.LogCritical("Parent process monitoring failed: {Message}. Exiting.", ex.Message);
                        Environment.Exit(0);
                    }
                }, cts.Token);

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
            finally
            {
                cts.Cancel();
            }

        }
    }
}
