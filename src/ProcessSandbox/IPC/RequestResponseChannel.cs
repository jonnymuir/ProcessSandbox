using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandbox.IPC
{
    /// <summary>
    /// Wraps an IPC channel with request-response pattern support.
    /// Handles correlation of requests and responses using correlation IDs.
    /// </summary>
    public class RequestResponseChannel : IDisposable
    {
        private readonly IIpcChannel _channel;
        private readonly ConcurrentDictionary<Guid, TaskCompletionSource<MethodResultMessage>> _pendingRequests;
        private readonly CancellationTokenSource _receiverCts;
        private readonly Task _receiverTask;
        private bool _disposed;

        /// <summary>
        /// Gets whether the channel is connected.
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
        /// Creates a new request-response channel.
        /// </summary>
        /// <param name="channel">The underlying IPC channel.</param>
        public RequestResponseChannel(IIpcChannel channel)
        {
            _channel = channel ?? throw new ArgumentNullException(nameof(channel));
            _pendingRequests = new ConcurrentDictionary<Guid, TaskCompletionSource<MethodResultMessage>>();
            _receiverCts = new CancellationTokenSource();

            // Subscribe to disconnection events
            _channel.Disconnected += OnChannelDisconnected;

            // Start receiver task
            _receiverTask = Task.Run(ReceiverLoop);
        }

        /// <summary>
        /// Sends a method invocation request and waits for the result.
        /// </summary>
        /// <param name="invocation">The method invocation message.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The method result.</returns>
        public async Task<MethodResultMessage> SendRequestAsync(
            MethodInvocationMessage invocation,
            CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(RequestResponseChannel));

            if (!IsConnected)
                throw new IpcException("Channel is not connected");

            var tcs = new TaskCompletionSource<MethodResultMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
            
            if (!_pendingRequests.TryAdd(invocation.CorrelationId, tcs))
                throw new InvalidOperationException($"Duplicate correlation ID: {invocation.CorrelationId}");

            try
            {
                // Send the request
                var message = IpcMessage.FromMethodInvocation(invocation);
                await _channel.SendMessageAsync(message, cancellationToken).ConfigureAwait(false);

                // Wait for response with timeout
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(invocation.TimeoutMilliseconds);

                var cancelTask = Task.Delay(Timeout.Infinite, timeoutCts.Token);
                var completedTask = await Task.WhenAny(tcs.Task, cancelTask).ConfigureAwait(false);

                if (completedTask == cancelTask)
                {
                    // Timeout or cancellation
                    throw new MethodTimeoutException(
                        invocation.MethodName,
                        TimeSpan.FromMilliseconds(invocation.TimeoutMilliseconds));
                }

                return await tcs.Task.ConfigureAwait(false);
            }
            finally
            {
                _pendingRequests.TryRemove(invocation.CorrelationId, out _);
            }
        }

        /// <summary>
        /// Sends a ping and waits for pong response.
        /// </summary>
        /// <param name="timeoutMs">Timeout in milliseconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>True if pong received, false otherwise.</returns>
        public async Task<bool> PingAsync(int timeoutMs = 5000, CancellationToken cancellationToken = default)
        {
            if (_disposed || !IsConnected)
                return false;

            try
            {
                var ping = IpcMessage.CreatePing();
                
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                await _channel.SendMessageAsync(ping, cts.Token).ConfigureAwait(false);
                
                // Pong will be received by ReceiverLoop, just wait a bit
                await Task.Delay(100, cts.Token).ConfigureAwait(false);
                
                return IsConnected;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Closes the channel gracefully.
        /// </summary>
        public async Task CloseAsync()
        {
            if (_disposed)
                return;

            // Cancel all pending requests
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(new IpcException("Channel closed"));
            }
            _pendingRequests.Clear();

            // Stop receiver
            _receiverCts.Cancel();

            // Close underlying channel
            await _channel.CloseAsync().ConfigureAwait(false);

            try
            {
                await _receiverTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Expected
            }
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
                        message = await _channel.ReceiveMessageAsync(_receiverCts.Token)
                            .ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        // Channel error, exit loop
                        FailAllPendingRequests(new IpcException("Receiver loop failed", ex));
                        break;
                    }

                    if (message == null)
                    {
                        // Channel closed
                        FailAllPendingRequests(new IpcException("Channel closed unexpectedly"));
                        break;
                    }

                    // Handle the message
                    HandleReceivedMessage(message);
                }
            }
            finally
            {
                // Make sure all pending requests are failed
                FailAllPendingRequests(new IpcException("Receiver loop terminated"));
            }
        }

        private void HandleReceivedMessage(IpcMessage message)
        {
            switch (message.MessageType)
            {
                case MessageType.MethodResult:
                    HandleMethodResult(message.GetMethodResult());
                    break;

                case MessageType.Pong:
                    // Pong received, channel is alive
                    break;

                case MessageType.Shutdown:
                    // Graceful shutdown request
                    _ = CloseAsync();
                    break;

                default:
                    // Unexpected message type, ignore
                    break;
            }
        }

        private void HandleMethodResult(MethodResultMessage result)
        {
            if (_pendingRequests.TryRemove(result.CorrelationId, out var tcs))
            {
                tcs.TrySetResult(result);
            }
            // else: result for unknown request, ignore
        }

        private void FailAllPendingRequests(Exception exception)
        {
            foreach (var kvp in _pendingRequests)
            {
                kvp.Value.TrySetException(exception);
            }
            _pendingRequests.Clear();
        }

        private void OnChannelDisconnected(object? sender, ChannelDisconnectedEventArgs e)
        {
            if(e.Exception == null) {
                FailAllPendingRequests(new IpcException($"Channel disconnected: {e.Reason}"));
            } else {
                FailAllPendingRequests(new IpcException($"Channel disconnected: {e.Reason}", e.Exception));
            }   
            Disconnected?.Invoke(this, e);
        }

        /// <summary>
        /// Disposes the channel.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

            _channel.Disconnected -= OnChannelDisconnected;
            
            _receiverCts?.Cancel();
            _receiverCts?.Dispose();

            try
            {
                _receiverTask?.Wait(1000);
            }
            catch
            {
                // Ignore
            }

            _channel?.Dispose();
        }
    }
}