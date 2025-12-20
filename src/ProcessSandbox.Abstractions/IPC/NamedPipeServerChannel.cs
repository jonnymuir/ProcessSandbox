using System;
using System.IO.Pipes;
using System.Security.Principal;
using System.Threading;
using System.Threading.Tasks;
using MessagePack;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandBox.Abstractions.IPC;

/// <summary>
/// Named pipe server channel for worker processes.
/// </summary>
/// <remarks>
/// Creates a new named pipe server channel.
/// </remarks>
/// <param name="pipeName">The name of the pipe.</param>
public class NamedPipeServerChannel(string pipeName) : IIpcChannel
{
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private NamedPipeServerStream? _pipeServer;
    private bool _disposed;
    private volatile bool _isConnected;

    /// <summary>
    /// Gets whether the channel is connected.
    /// </summary>
    public bool IsConnected => _isConnected && _pipeServer?.IsConnected == true;
    /// <summary>
    /// Gets the channel ID.
    /// </summary>
    public string ChannelId { get { return pipeName; } }

    /// <summary>
    /// Event raised when the channel is disconnected.
    /// </summary>
    public event EventHandler<ChannelDisconnectedEventArgs>? Disconnected;

    /// <summary>
    /// Waits for a client to connect to the pipe.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task WaitForConnectionAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NamedPipeServerChannel));

        // Create the named pipe server
        _pipeServer = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1, // Max one connection per pipe
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            inBufferSize: 8192,
            outBufferSize: 8192);

        try
        {
            // Wait for client connection
            await _pipeServer.WaitForConnectionAsync(cancellationToken).ConfigureAwait(false);
            _isConnected = true;
        }
        catch (Exception ex)
        {
            _pipeServer?.Dispose();
            _pipeServer = null;
            throw new IpcException("Failed to wait for pipe connection", ex);
        }
    }

    /// <inheritdoc/>
    public async Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default)
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(NamedPipeServerChannel));

        if (!IsConnected)
            throw new IpcException("Channel is not connected");

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var messageBytes = MessagePackSerializer.Serialize(message, cancellationToken: cancellationToken);
            await MessageFraming.WriteMessageAsync(_pipeServer!, messageBytes, cancellationToken)
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
            throw new ObjectDisposedException(nameof(NamedPipeServerChannel));

        if (!IsConnected)
            throw new IpcException("Channel is not connected");

        try
        {
            var messageBytes = await MessageFraming.ReadMessageAsync(_pipeServer!, cancellationToken)
                .ConfigureAwait(false);

            if (messageBytes == null)
            {
                // Stream ended gracefully
                HandleDisconnection("Client disconnected", null, expected: true);
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

        if (_pipeServer?.IsConnected == true)
        {
            try
            {
                // Try to send a graceful shutdown message
                var shutdownMsg = IpcMessage.CreateShutdown();
                var bytes = MessagePackSerializer.Serialize(shutdownMsg);
                await MessageFraming.WriteMessageAsync(_pipeServer, bytes, CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch
            {
                // Ignore errors during shutdown
            }

            _pipeServer.Disconnect();
        }

        HandleDisconnection("Channel closed Server", null, expected: true);
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

        _pipeServer?.Dispose();
        _sendLock?.Dispose();
    }
}