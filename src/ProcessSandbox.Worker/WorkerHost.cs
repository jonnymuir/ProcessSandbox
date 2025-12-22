using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandBox.Abstractions.IPC;

namespace ProcessSandbox.Worker;

/// <summary>
/// Hosts the worker process logic: loads assemblies, listens for requests, executes methods.
/// </summary>
/// <remarks>
/// Creates a new worker host.
/// </remarks>
/// <param name="config"></param>
/// <param name="loggerFactory"></param>
/// <exception cref="ArgumentNullException"></exception>
public class WorkerHost(WorkerConfiguration config, ILoggerFactory loggerFactory) : IDisposable
{
    private readonly ILogger<WorkerHost> _logger = loggerFactory.CreateLogger<WorkerHost>();
    private readonly CancellationTokenSource _shutdownCts = new CancellationTokenSource();

    private NamedPipeServerChannel? _channel;
    private MethodInvoker? _methodInvoker;
    private bool _disposed;

    /// <summary>
    /// Runs the worker host until shutdown is requested.
    /// </summary>
    public async Task RunAsync()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WorkerHost));
        _logger.LogInformation("Worker host starting...");

        try
        {
            object targetInstance;
            Type targetType;

            // ---------------------------------------------------------
            // 1. DETERMINISTIC LOADING STRATEGY
            // ---------------------------------------------------------

            // Check if this is a Native COM activation request
            // We can detect this if a CLSID is provided in config, OR by checking the file header.
            // Here we use a Try/Catch approach on AssemblyName.GetAssemblyName for robustness.
            bool isManagedAssembly = true;
            try
            {
                AssemblyName.GetAssemblyName(config.AssemblyPath);
            }
            catch (BadImageFormatException)
            {
                isManagedAssembly = false;
            }

            if (isManagedAssembly)
            {
                // -- STRATEGY A: MANAGED ASSEMBLY --
                _logger.LogInformation("Detected Managed Assembly. Loading via Reflection...");
                var loader = new AssemblyLoader(loggerFactory.CreateLogger<AssemblyLoader>());
                targetInstance = loader.LoadAndCreateInstance(config.AssemblyPath, config.TypeName);
                targetType = targetInstance.GetType();
            }
            else
            {
                // -- STRATEGY B: NATIVE DIRECT COM --
                _logger.LogInformation("Detected Native DLL. Attempting Direct COM Load...");

                Guid clsid;

                if (config.ComClsid != Guid.Empty)
                {
                    clsid = config.ComClsid;
                }
                else
                {
                    throw new ArgumentException("Native COM loading requires a CLSID. Pass it in config or append to TypeName (Type|GUID).");
                }

                targetType = ResolveInterfaceType(config.TypeName) 
                             ?? throw new Exception($"Could not find Interface Type '{config.TypeName}'");
                

                targetInstance = NativeComLoader.CreateInstance(config.AssemblyPath, clsid, targetType);
            }

            _logger.LogInformation("Target instance created. Type: {Type}", targetType.FullName);

            // ---------------------------------------------------------
            // 2. SETUP METHOD INVOKER
            // ---------------------------------------------------------

            // Important: We must pass 'targetType' explicitly. 
            // If targetInstance is a COM object, GetType() returns System.__ComObject, which breaks Reflection.
            // You might need to add a constructor to MethodInvoker that accepts the Type explicitly.
            _methodInvoker = new MethodInvoker(
                targetInstance,
                targetType,
                loggerFactory.CreateLogger<MethodInvoker>());

            // ---------------------------------------------------------
            // 3. START IPC
            // ---------------------------------------------------------
            _logger.LogInformation("Connecting to pipe: {PipeName}", config.PipeName);
            _channel = new NamedPipeServerChannel(config.PipeName);
            _channel.Disconnected += OnChannelDisconnected;

            _logger.LogInformation("Pipe server ready. Sending READY signal.");
            Console.WriteLine("PROCESS_SANDBOX_WORKER_READY");

            await _channel.WaitForConnectionAsync(_shutdownCts.Token).ConfigureAwait(false);
            _logger.LogInformation("Client connected");

            await MessageProcessingLoop().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Worker host shutdown requested");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in worker host");
            throw;
        }
        finally
        {
            await CleanupAsync().ConfigureAwait(false);
        }

        _logger.LogInformation("Worker host stopped");
    }

    /// <summary>
    /// Requests the worker to stop.
    /// </summary>
    public async Task StopAsync()
    {
        _logger.LogInformation("Stopping worker host...");
        _shutdownCts.Cancel();

        if (_channel != null)
        {
            await _channel.CloseAsync().ConfigureAwait(false);
        }
    }

    private async Task MessageProcessingLoop()
    {
        _logger.LogInformation("Message processing loop started");

        while (!_shutdownCts.Token.IsCancellationRequested && _channel!.IsConnected)
        {
            IpcMessage? message;

            try
            {
                message = await _channel.ReceiveMessageAsync(_shutdownCts.Token)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error receiving message");
                break;
            }

            if (message == null)
            {
                _logger.LogInformation("Channel closed by client");
                break;
            }

            // Process the message
            await ProcessMessageAsync(message).ConfigureAwait(false);
        }

        _logger.LogInformation("Message processing loop stopped");
    }

    private async Task ProcessMessageAsync(IpcMessage message)
    {
        switch (message.MessageType)
        {
            case MessageType.MethodInvocation:
                await HandleMethodInvocationAsync(message.GetMethodInvocation())
                    .ConfigureAwait(false);
                break;

            case MessageType.Shutdown:
                _logger.LogInformation("Shutdown message received");
                await StopAsync().ConfigureAwait(false);
                break;

            default:
                _logger.LogWarning("Unexpected message type: {MessageType}", message.MessageType);
                break;
        }
    }

    private async Task HandleMethodInvocationAsync(MethodInvocationMessage invocation)
    {
        MethodResultMessage result;

        try
        {
            // Invoke the method
            result = _methodInvoker!.InvokeMethod(invocation);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error invoking method: {Method}", invocation.MethodName);
            result = MethodResultMessage.CreateFailure(invocation.CorrelationId, ex);
        }

        // Send result back
        try
        {
            var resultMessage = IpcMessage.FromMethodResult(result);
            await _channel!.SendMessageAsync(resultMessage, _shutdownCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending method result");
            throw;
        }
    }

    private void OnChannelDisconnected(object? sender, ChannelDisconnectedEventArgs e)
    {
        if (e.Expected)
        {
            _logger.LogInformation("Channel disconnected: {Reason}", e.Reason);
        }
        else
        {
            _logger.LogWarning("Channel disconnected unexpectedly: {Reason}", e.Reason);
            if (e.Exception != null)
            {
                _logger.LogWarning(e.Exception, "Disconnection exception");
            }
        }

        // Trigger shutdown
        _shutdownCts.Cancel();
    }

    private async Task CleanupAsync()
    {
        _logger.LogInformation("Cleaning up resources...");

        if (_channel != null)
        {
            _channel.Disconnected -= OnChannelDisconnected;
            await _channel.CloseAsync().ConfigureAwait(false);
            _channel.Dispose();
            _channel = null;
        }

        _logger.LogInformation("Cleanup complete");
    }

    private Type? ResolveInterfaceType(string typeName)
    {
        // 1. Try standard resolution first (in case it's already loaded or in GAC)
        var type = Type.GetType(typeName);
        if (type != null) return type;

        _logger.LogInformation("Type '{Type}' not found in local context. Probing parent directories...", typeName);

        // 2. Guess the assembly name from the namespace (e.g. "Contracts.ICalculator" -> "Contracts.dll")
        // This is a heuristic; in production you might want explicit config for this.
        string assemblyName = typeName.Split('.')[0] + ".dll";

        // 3. Walk up the directory tree to find the DLL
        string currentDir = AppDomain.CurrentDomain.BaseDirectory;
        string? foundPath = null;

        for (int i = 0; i < 5; i++) // Check up to 5 levels up
        {
            string candidate = Path.Combine(currentDir, assemblyName);
            if (File.Exists(candidate))
            {
                foundPath = candidate;
                break;
            }

            var parent = Directory.GetParent(currentDir);
            if (parent == null) break;
            currentDir = parent.FullName;
        }

        if (foundPath != null)
        {
            _logger.LogInformation("Found dependency at: {Path}", foundPath);
            // Load the assembly into the current context
            var asm = Assembly.LoadFrom(foundPath);
            return asm.GetType(typeName);
        }

        return null;
    }

    /// <summary>
    /// Disposes the worker host.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;

        _shutdownCts?.Cancel();
        _shutdownCts?.Dispose();

        _channel?.Dispose();
    }
}