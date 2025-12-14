using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using MessagePack;
using ProcessSandbox.IPC;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandbox.Abstractions;

namespace ProcessSandbox.Tests.IPC;

/// <summary>
/// Integration tests for named pipe server and client channels.
/// </summary>
public class NamedPipeChannelIntegrationTests
{
    /// <summary>
    /// Tests that the server and client can connect successfully.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task ServerAndClient_Connect_Successfully()
    {
        // Arrange
        var pipeName = PipeNameGenerator.Generate();
        using var server = new NamedPipeServerChannel(pipeName);
        using var client = new NamedPipeClientChannel(pipeName);

        // Act
        var serverTask = server.WaitForConnectionAsync();
        await Task.Delay(100); // Give server time to start listening
        await client.ConnectAsync();
        await serverTask;

        // Assert
        Assert.True(server.IsConnected);
        Assert.True(client.IsConnected);
    }


    /// <summary>
    /// Tests that the server and client can exchange method invocation messages.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task ServerAndClient_ExchangeMethodInvocation_Successfully()
    {
        // Arrange
        var pipeName = PipeNameGenerator.Generate();
        using var server = new NamedPipeServerChannel(pipeName);
        using var client = new NamedPipeClientChannel(pipeName);

        var serverTask = server.WaitForConnectionAsync();
        await Task.Delay(100);
        await client.ConnectAsync();
        await serverTask;

        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "TestMethod", 1000)
        {
            ParameterTypeNames = ["System.String"],
            SerializedParameters = [MessagePackSerializer.Serialize("test")]
        };
        var message = IpcMessage.FromMethodInvocation(invocation);

        // Act
        var receiveTask = server.ReceiveMessageAsync();
        await client.SendMessageAsync(message);
        var receivedMessage = await receiveTask;

        // Assert
        Assert.NotNull(receivedMessage);
        Assert.Equal(MessageType.MethodInvocation, receivedMessage.MessageType);

        var receivedInvocation = receivedMessage.GetMethodInvocation();
        Assert.Equal(invocation.CorrelationId, receivedInvocation.CorrelationId);
        Assert.Equal(invocation.MethodName, receivedInvocation.MethodName);
    }

    /// <summary>
    /// Tests that client connection times out appropriately.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task Client_ConnectTimeout_ThrowsException()
    {
        // Arrange
        var pipeName = PipeNameGenerator.Generate();
        using var client = new NamedPipeClientChannel(pipeName);

        // Act & Assert
        await Assert.ThrowsAsync<IpcException>(
            () => client.ConnectAsync(timeoutMs: 500));
    }

    /// <summary>
    /// Tests that the server and client can close gracefully without errors.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task ServerAndClient_CloseGracefully_NoErrors()
    {
        // Arrange
        var pipeName = PipeNameGenerator.Generate();
        using var server = new NamedPipeServerChannel(pipeName);
        using var client = new NamedPipeClientChannel(pipeName);

        var serverTask = server.WaitForConnectionAsync();
        await Task.Delay(100);
        await client.ConnectAsync();
        await serverTask;

        // Act
        await client.CloseAsync();
        await server.CloseAsync();

        // Assert
        Assert.False(client.IsConnected);
        Assert.False(server.IsConnected);
    }
}