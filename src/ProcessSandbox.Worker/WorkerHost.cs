using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandbox.IPC;

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
        if (_disposed)
            throw new ObjectDisposedException(nameof(WorkerHost));

        _logger.LogInformation("Worker host starting...");

        try
        {
            // Load assembly and create instance
            _logger.LogInformation("Loading target assembly...");
            var loader = new AssemblyLoader(loggerFactory.CreateLogger<AssemblyLoader>());
            var targetInstance = loader.LoadAndCreateInstance(
                config.AssemblyPath,
                config.TypeName);

            _logger.LogInformation("Target instance created: {Type}", targetInstance.GetType().FullName);

            // Create method invoker
            _methodInvoker = new MethodInvoker(
                targetInstance,
                loggerFactory.CreateLogger<MethodInvoker>());

            // Create and connect IPC channel
            _logger.LogInformation("Connecting to pipe: {PipeName}", config.PipeName);
            _channel = new NamedPipeServerChannel(config.PipeName);
            
            // Subscribe to disconnection events
            _channel.Disconnected += OnChannelDisconnected;

            // Wait for client connection
            await _channel.WaitForConnectionAsync(_shutdownCts.Token).ConfigureAwait(false);
            _logger.LogInformation("Client connected");

            // Process messages until shutdown
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

            case MessageType.Ping:
                await HandlePingAsync().ConfigureAwait(false);
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
        _logger.LogDebug(
            "Processing method invocation: {Method} (correlation: {CorrelationId})",
            invocation.MethodName,
            invocation.CorrelationId);

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

            _logger.LogDebug(
                "Method result sent: {Success} (correlation: {CorrelationId})",
                result.Success,
                result.CorrelationId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending method result");
            throw;
        }
    }

    private async Task HandlePingAsync()
    {
        _logger.LogDebug("Ping received, sending pong");

        try
        {
            var pong = IpcMessage.CreatePong();
            await _channel!.SendMessageAsync(pong, _shutdownCts.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending pong");
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