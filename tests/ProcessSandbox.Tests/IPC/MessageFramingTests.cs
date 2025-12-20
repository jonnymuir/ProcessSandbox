using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using MessagePack;
using ProcessSandBox.Abstractions.IPC;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandbox.Tests.IPC;

/// <summary>
/// Tests for message framing utilities.
/// </summary>
public class MessageFramingTests
{
    /// <summary>
    /// Tests that writing and then reading a message preserves the data.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task WriteAndReadMessage_RoundTrip_PreservesData()
    {
        // Arrange
        using var stream = new MemoryStream();
        var originalData = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        await MessageFraming.WriteMessageAsync(stream, originalData);
        stream.Position = 0;
        var receivedData = await MessageFraming.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(receivedData);
        Assert.Equal(originalData, receivedData);
    }

    /// <summary>
    /// Tests that writing and reading an empty message works correctly.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task WriteAndReadMessage_EmptyMessage_HandlesCorrectly()
    {
        // Arrange
        using var stream = new MemoryStream();
        var originalData = Array.Empty<byte>();

        // Act
        await MessageFraming.WriteMessageAsync(stream, originalData);
        stream.Position = 0;
        var receivedData = await MessageFraming.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(receivedData);
        Assert.Empty(receivedData);
    }

    /// <summary>
    /// Tests that reading from an empty stream returns null.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task ReadMessage_EmptyStream_ReturnsNull()
    {
        // Arrange
        using var stream = new MemoryStream();

        // Act
        var result = await MessageFraming.ReadMessageAsync(stream);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that writing and reading a large message works correctly.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task WriteAndReadMessage_LargeMessage_HandlesCorrectly()
    {
        // Arrange
        using var stream = new MemoryStream();
        var originalData = new byte[1024 * 1024]; // 1 MB
        new Random(42).NextBytes(originalData);

        // Act
        await MessageFraming.WriteMessageAsync(stream, originalData);
        stream.Position = 0;
        var receivedData = await MessageFraming.ReadMessageAsync(stream);

        // Assert
        Assert.NotNull(receivedData);
        Assert.Equal(originalData.Length, receivedData.Length);
        Assert.Equal(originalData, receivedData);
    }

    /// <summary>
    /// Tests that writing and reading a message synchronously works correctly.
    /// </summary>
    [Fact]
    public void WriteAndReadMessage_Sync_RoundTrip()
    {
        // Arrange
        using var stream = new MemoryStream();
        var originalData = new byte[] { 10, 20, 30 };

        // Act
        MessageFraming.WriteMessage(stream, originalData);
        stream.Position = 0;
        var receivedData = MessageFraming.ReadMessage(stream);

        // Assert
        Assert.NotNull(receivedData);
        Assert.Equal(originalData, receivedData);
    }
}