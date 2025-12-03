using System;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandbox.IPC
{
    /// <summary>
    /// Named pipe client channel for connecting to worker processes.
    /// </summary>
    /// <remarks>
    /// Creates a new named pipe client channel.
    /// </remarks>
    /// <param name="pipeName">The name of the pipe to connect to.</param>
    /// <param name="serverName">The server name (use "." for local machine).</param>
    public class NamedPipeClientChannel(string pipeName, string serverName = ".") : IIpcChannel
    {
        private readonly SemaphoreSlim _sendLock = new(1, 1);
        private NamedPipeClientStream? _pipeClient;
        private bool _disposed;
        private volatile bool _isConnected;

        /// <summary>
        /// Gets whether the channel is connected.
        /// </summary>
        public bool IsConnected => _isConnected && _pipeClient?.IsConnected == true;

        /// <summary>
        /// Gets the channel ID.
        /// </summary>
        public string ChannelId { get; } = $"{serverName}\\{pipeName}";

        /// <summary>
        /// Event raised when the channel is disconnected.
        /// </summary>
        public event EventHandler<ChannelDisconnectedEventArgs>? Disconnected;

        /// <summary>
        /// Connects to the named pipe server.
        /// </summary>
        /// <param name="timeoutMs">Connection timeout in milliseconds.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async Task ConnectAsync(int timeoutMs = 10000, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(NamedPipeClientChannel));

            if (_isConnected)
                throw new InvalidOperationException("Already connected");

            _pipeClient = new NamedPipeClientStream(
                serverName,
                pipeName,
                PipeDirection.InOut,
                PipeOptions.Asynchronous);

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                cts.CancelAfter(timeoutMs);

                await _pipeClient.ConnectAsync(cts.Token).ConfigureAwait(false);
                _isConnected = true;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                _pipeClient?.Dispose();
                _pipeClient = null;
                throw new IpcException($"Connection timed out after {timeoutMs}ms");
            }
            catch (Exception ex)
            {
                _pipeClient?.Dispose();
                _pipeClient = null;
                throw new IpcException("Failed to connect to pipe", ex);
            }
        }

        /// <inheritdoc/>
        public async Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NamedPipeClientChannel));
            }

            if (!IsConnected)
            {
                throw new IpcException("Channel is not connected");
            }

            await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var messageBytes = MessagePackSerializer.Serialize(message, cancellationToken: cancellationToken);
                await MessageFraming.WriteMessageAsync(_pipeClient!, messageBytes, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                HandleDisconnection("Send failed", ex);
                throw new IpcException("Failed to send message", ex);
            }
            finally
            {
                _sendLock.Release();
            }
        }

        /// <inheritdoc/>
        public async Task<IpcMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(NamedPipeClientChannel));
            }

            if (!IsConnected)
            {
                throw new IpcException("Channel is not connected");
            }

            try
            {
                var messageBytes = await MessageFraming.ReadMessageAsync(_pipeClient!, cancellationToken)
                    .ConfigureAwait(false);

                if (messageBytes == null)
                {
                    // Stream ended gracefully
                    HandleDisconnection("Server disconnected", null, expected: true);
                    return null;
                }

                return MessagePackSerializer.Deserialize<IpcMessage>(messageBytes, cancellationToken: cancellationToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                HandleDisconnection("Receive failed", ex);
                throw new IpcException("Failed to receive message", ex);
            }
        }

        /// <inheritdoc/>
        public async Task CloseAsync()
        {
            if (_disposed)
                return;

            _isConnected = false;

            if (_pipeClient?.IsConnected == true)
            {
                try
                {
                    // Try to send a graceful shutdown message
                    var shutdownMsg = IpcMessage.CreateShutdown();
                    var bytes = MessagePackSerializer.Serialize(shutdownMsg);
                    await MessageFraming.WriteMessageAsync(_pipeClient, bytes, CancellationToken.None)
                        .ConfigureAwait(false);
                }
                catch
                {
                    // Ignore errors during shutdown
                }
            }

            HandleDisconnection("Channel closed", null, expected: true);
        }

        /// <summary>
        /// Tests if the pipe server is available by attempting a quick connection.
        /// </summary>
        /// <param name="pipeName">The name of the pipe.</param>
        /// <param name="serverName">The server name (use "." for local machine).</param>
        /// <param name="timeoutMs">Timeout for the test connection.</param>
        /// <returns>True if the pipe is available, false otherwise.</returns>
        public static bool IsPipeAvailable(string pipeName, string serverName = ".", int timeoutMs = 100)
        {
            try
            {
                using var testClient = new NamedPipeClientStream(
                    serverName,
                    pipeName,
                    PipeDirection.InOut,
                    PipeOptions.None);

                using var cts = new CancellationTokenSource(timeoutMs);
                testClient.Connect(timeoutMs);
                
                return testClient.IsConnected;
            }
            catch
            {
                return false;
            }
        }

        private void HandleDisconnection(string reason, Exception? exception, bool expected = false)
        {
            if (!_isConnected)
                return; // Already disconnected

            _isConnected = false;

            try
            {
                Disconnected?.Invoke(this, new ChannelDisconnectedEventArgs(reason, exception, expected));
            }
            catch
            {
                // Ignore errors in event handlers
            }
        }

        /// <summary>
        /// Disposes the channel.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            _isConnected = false;

            _pipeClient?.Dispose();
            _sendLock?.Dispose();
        }
    }
}