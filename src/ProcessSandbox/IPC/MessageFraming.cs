using System;
using System.Buffers;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ProcessSandbox.Abstractions;

namespace ProcessSandbox.IPC
{
    /// <summary>
    /// Provides length-prefixed message framing for stream-based communication.
    /// Format: [4-byte length][message bytes]
    /// </summary>
    public static class MessageFraming
    {
        private const int LengthPrefixSize = sizeof(int);
        private const int MaxMessageSize = 100 * 1024 * 1024; // 100 MB max message size

        /// <summary>
        /// Writes a length-prefixed message to a stream.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="message">The message bytes to write.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public static async Task WriteMessageAsync(
            Stream stream, 
            byte[] message, 
            CancellationToken cancellationToken = default)
        {
            // Write length prefix
            var lengthBytes = BitConverter.GetBytes(message.Length);
            await stream.WriteAsync(lengthBytes, cancellationToken)
                .ConfigureAwait(false);

            // Write message
            await stream.WriteAsync(message, cancellationToken)
                .ConfigureAwait(false);

            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Reads a length-prefixed message from a stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The message bytes, or null if stream ended.</returns>
        public static async Task<byte[]?> ReadMessageAsync(
            Stream stream, 
            CancellationToken cancellationToken = default)
        {
            // Read length prefix
            var lengthBuffer = new byte[LengthPrefixSize];
            var bytesRead = await ReadExactlyAsync(
                stream, 
                lengthBuffer, 
                0, 
                LengthPrefixSize, 
                cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead == 0)
                return null; // Stream ended gracefully

            if (bytesRead < LengthPrefixSize)
                throw new IpcException("Stream ended unexpectedly while reading length prefix");

            var messageLength = BitConverter.ToInt32(lengthBuffer, 0);

            // Validate message length
            if (messageLength < 0)
                throw new IpcException($"Invalid message length: {messageLength}");

            if (messageLength > MaxMessageSize)
                throw new IpcException(
                    $"Message too large: {messageLength} bytes (max: {MaxMessageSize})");

            if (messageLength == 0)
                return [];

            // Read message
            var messageBuffer = new byte[messageLength];
            bytesRead = await ReadExactlyAsync(
                stream, 
                messageBuffer, 
                0, 
                messageLength, 
                cancellationToken)
                .ConfigureAwait(false);

            if (bytesRead < messageLength)
                throw new IpcException(
                    $"Stream ended unexpectedly while reading message body " +
                    $"(expected {messageLength} bytes, got {bytesRead})");

            return messageBuffer;
        }

        /// <summary>
        /// Reads exactly the specified number of bytes from a stream.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <param name="buffer">The buffer to read into.</param>
        /// <param name="offset">The offset in the buffer to start writing.</param>
        /// <param name="count">The number of bytes to read.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The number of bytes read (may be less than count if stream ends).</returns>
        private static async Task<int> ReadExactlyAsync(
            Stream stream,
            byte[] buffer,
            int offset,
            int count,
            CancellationToken cancellationToken)
        {
            int totalRead = 0;

            while (totalRead < count)
            {
                int bytesRead = await stream.ReadAsync(
                    buffer, 
                    offset + totalRead, 
                    count - totalRead, 
                    cancellationToken)
                    .ConfigureAwait(false);

                if (bytesRead == 0)
                {
                    // Stream ended
                    return totalRead;
                }

                totalRead += bytesRead;
            }

            return totalRead;
        }

        /// <summary>
        /// Writes a length-prefixed message synchronously.
        /// </summary>
        /// <param name="stream">The stream to write to.</param>
        /// <param name="message">The message bytes to write.</param>
        public static void WriteMessage(Stream stream, byte[] message)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));
            if (message == null)
                throw new ArgumentNullException(nameof(message));

            // Write length prefix
            var lengthBytes = BitConverter.GetBytes(message.Length);
            stream.Write(lengthBytes, 0, lengthBytes.Length);

            // Write message
            stream.Write(message, 0, message.Length);
            stream.Flush();
        }

        /// <summary>
        /// Reads a length-prefixed message synchronously.
        /// </summary>
        /// <param name="stream">The stream to read from.</param>
        /// <returns>The message bytes, or null if stream ended.</returns>
        public static byte[]? ReadMessage(Stream stream)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // Read length prefix
            var lengthBuffer = new byte[LengthPrefixSize];
            var bytesRead = ReadExactly(stream, lengthBuffer, 0, LengthPrefixSize);

            if (bytesRead == 0)
                return null; // Stream ended gracefully

            if (bytesRead < LengthPrefixSize)
                throw new IpcException("Stream ended unexpectedly while reading length prefix");

            var messageLength = BitConverter.ToInt32(lengthBuffer, 0);

            // Validate message length
            if (messageLength < 0)
                throw new IpcException($"Invalid message length: {messageLength}");

            if (messageLength > MaxMessageSize)
                throw new IpcException(
                    $"Message too large: {messageLength} bytes (max: {MaxMessageSize})");

            if (messageLength == 0)
                return [];

            // Read message
            var messageBuffer = new byte[messageLength];
            bytesRead = ReadExactly(stream, messageBuffer, 0, messageLength);

            if (bytesRead < messageLength)
                throw new IpcException(
                    $"Stream ended unexpectedly while reading message body " +
                    $"(expected {messageLength} bytes, got {bytesRead})");

            return messageBuffer;
        }

        /// <summary>
        /// Reads exactly the specified number of bytes from a stream synchronously.
        /// </summary>
        private static int ReadExactly(Stream stream, byte[] buffer, int offset, int count)
        {
            int totalRead = 0;

            while (totalRead < count)
            {
                int bytesRead = stream.Read(buffer, offset + totalRead, count - totalRead);

                if (bytesRead == 0)
                {
                    // Stream ended
                    return totalRead;
                }

                totalRead += bytesRead;
            }

            return totalRead;
        }
    }
}