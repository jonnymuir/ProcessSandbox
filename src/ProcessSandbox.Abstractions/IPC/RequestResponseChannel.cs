using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandBox.Abstractions.IPC;

/// <summary>
/// Wraps an IPC channel with a simplified single-request-response pattern.
/// Optimized for workers that handle exactly one invocation at a time.
/// </summary>
public class RequestResponseChannel : IDisposable
{
    private readonly IIpcChannel _channel;
    private readonly CancellationTokenSource _receiverCts;
    private readonly Task _receiverTask;
    private readonly ILogger _logger;

    // Single-flight state management
    private TaskCompletionSource<MethodResultMessage>? _currentRequest;
    private readonly object _requestLock = new();
    private bool _disposed;

    /// <summary>
    /// Gets whether the underlying channel is connected.
    /// </summary>
    public bool IsConnected => _channel.IsConnected;
    /// <summary>
    /// Gets the channel ID.
    /// </summary>
    public string ChannelId => _channel.ChannelId;

    /// <summary>
    /// Event raised when the channel is disconnected.
    /// </summary>
    public event EventHandler<ChannelDisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Creates a new RequestResponseChannel wrapping the given IPC channel.
    /// </summary>
    /// <param name="channel"></param>
    /// <param name="logger"></param>
    public RequestResponseChannel(IIpcChannel channel, ILogger logger)
    {
        _logger = logger;
        _channel = channel;
        _receiverCts = new CancellationTokenSource();

        _channel.Disconnected += OnChannelDisconnected;
        _receiverTask = Task.Run(ReceiverLoop);
    }

    /// <summary>
    /// Sends a method invocation and waits for the result.
    /// Enforces single-flight execution via internal locking.
    /// </summary>
    public async Task<MethodResultMessage> SendRequestAsync(
        MethodInvocationMessage invocation,
        CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(RequestResponseChannel));

        if (!IsConnected)
            throw new IpcException("Channel is not connected");

        var tcs = new TaskCompletionSource<MethodResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Ensure no other thread is currently using this channel instance
        lock (_requestLock)
        {
            if (_currentRequest != null)
            {
                throw new InvalidOperationException(
                    $"Concurrency violation: Channel {ChannelId} is already processing a request. " +
                    "Ensure WorkerProcess locking is functioning correctly.");
            }
            _currentRequest = tcs;
        }

        try
        {
            var message = IpcMessage.FromMethodInvocation(invocation);
            await _channel.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(invocation.TimeoutMilliseconds);

            // Wait for either the result, a timeout, or the receiver loop failing
            var delayTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
            var completedTask = await Task.WhenAny(tcs.Task, delayTask).ConfigureAwait(false);

            if (completedTask == delayTask)
            {
                throw new MethodTimeoutException(
                    invocation.MethodName,
                    TimeSpan.FromMilliseconds(invocation.TimeoutMilliseconds));
            }

            _logger.LogDebug(
                "Method {MethodName} on channel {ChannelId} completed",
                invocation.MethodName,
                ChannelId);
            return await tcs.Task;
        }
        finally
        {
            // Clear the request slot so the next call can proceed
            lock (_requestLock)
            {
                if (_currentRequest == tcs)
                {
                    _currentRequest = null;
                }
            }
        }
    }

    /// <summary>
    /// Closes the channel and stops the receiver loop.
    /// </summary>
    /// <returns></returns>
    public async Task CloseAsync()
    {
        if (_disposed) return;

        _logger.LogDebug("Closing RequestResponseChannel {ChannelId}", ChannelId);

        while(!_currentRequest?.Task.IsCompleted ?? false)
        {
            await Task.Delay(50).ConfigureAwait(false);
        }
        // Fail the active request immediately so the caller doesn't hang
        //FailPendingRequest(new IpcException("Channel is being closed by the host."));

        _receiverCts.Cancel();
        await _channel.CloseAsync().ConfigureAwait(false);

        try
        {
            await _receiverTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException) { /* Expected */ }
    }

    private async Task ReceiverLoop()
    {
        try
        {
            while (!_receiverCts.Token.IsCancellationRequested && IsConnected)
            {
                IpcMessage? message;
                try
                {
                    message = await _channel.ReceiveMessageAsync(_receiverCts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    FailPendingRequest(new IpcException("Receiver loop encountered an error.", ex));
                    break;
                }

                if (message == null)
                {
                    FailPendingRequest(new IpcException("Underlying IPC channel was closed (Stream ended)."));
                    break;
                }

                HandleReceivedMessage(message);
            }
        }
        finally
        {
            FailPendingRequest(new IpcException("Receiver loop terminated."));
        }
    }

    private void HandleReceivedMessage(IpcMessage message)
    {
        _logger.LogDebug("Received IPC message of type {MessageType} on channel {ChannelId}", message.MessageType, ChannelId);
        if (message.MessageType == MessageType.MethodResult)
        {
            var result = message.GetMethodResult();
            var tcs = Interlocked.Exchange(ref _currentRequest, null);

            if (tcs != null)
            {
                tcs.TrySetResult(result);
            }
            else
            {
                _logger.LogWarning("Received result for CorrelationId {Id} but no request was pending.", result.CorrelationId);
            }
        }
        else if (message.MessageType == MessageType.Shutdown)
        {
            _logger.LogInformation("Received shutdown message from remote channel.");
            _ = CloseAsync();
        }
    }

    private void FailPendingRequest(Exception exception)
    {
        // Capture the current request and null it out atomically
        var tcs = Interlocked.Exchange(ref _currentRequest, null);

        if (tcs != null && tcs.Task.IsCompleted == false)
        {
            _logger.LogDebug(
                "Failing pending IPC request. \nid: {id} \nReason: {Message} \nSource Exception Stack: {ExStack} \nTrigger Stack: {CurrentStack}",
                this.ChannelId,
                exception.Message,
                exception.StackTrace ?? "N/A",
                Environment.StackTrace);

            tcs.TrySetException(exception);
        }
    }

    private void OnChannelDisconnected(object? sender, ChannelDisconnectedEventArgs e)
    {
        var message = $"Channel disconnected. Reason: {e.Reason}";
        var ex = e.Exception != null ? new IpcException(message, e.Exception) : new IpcException(message);

        FailPendingRequest(ex);
        Disconnected?.Invoke(this, e);
    }

    /// <summary>
    /// Disposes the channel.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _channel.Disconnected -= OnChannelDisconnected;
        _receiverCts.Cancel();

        // Non-async cleanup
        FailPendingRequest(new ObjectDisposedException(nameof(RequestResponseChannel)));

        _receiverCts.Dispose();
        _channel.Dispose();
    }
}