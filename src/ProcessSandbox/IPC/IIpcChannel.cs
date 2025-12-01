using System;
using System.Threading;
using System.Threading.Tasks;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandbox.IPC
{
    /// <summary>
    /// Represents a bidirectional communication channel for IPC.
    /// </summary>
    public interface IIpcChannel : IDisposable
    {
        /// <summary>
        /// Gets a value indicating whether the channel is connected.
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets the unique identifier for this channel.
        /// </summary>
        string ChannelId { get; }

        /// <summary>
        /// Sends a message through the channel.
        /// </summary>
        /// <param name="message">The message to send.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task SendMessageAsync(IpcMessage message, CancellationToken cancellationToken = default);

        /// <summary>
        /// Receives a message from the channel.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The received message, or null if channel is closed.</returns>
        Task<IpcMessage?> ReceiveMessageAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes the channel gracefully.
        /// </summary>
        Task CloseAsync();

        /// <summary>
        /// Event raised when the channel is disconnected.
        /// </summary>
        event EventHandler<ChannelDisconnectedEventArgs>? Disconnected;
    }

    /// <summary>
    /// Event args for channel disconnection.
    /// </summary>
    /// <remarks>
    /// Creates new channel disconnected event args.
    /// </remarks>
    /// <param name="reason">The reason for disconnection.</param>
    /// <param name="exception">The exception that caused disconnection, if any.</param>
    /// <param name="expected">Whether the disconnection was expected.</param>
    public class ChannelDisconnectedEventArgs(string reason, Exception? exception = null, bool expected = false) : EventArgs
    {
        /// <summary>
        /// Gets the reason for disconnection.
        /// </summary>
        public string Reason { get; } = reason ?? throw new ArgumentNullException(nameof(reason));

        /// <summary>
        /// Gets the exception that caused disconnection, if any.
        /// </summary>
        public Exception? Exception { get; } = exception;

        /// <summary>
        /// Gets whether the disconnection was expected (graceful shutdown).
        /// </summary>
        public bool Expected { get; } = expected;
    }
}