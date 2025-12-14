using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using MessagePack;
using ProcessSandbox.IPC;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandbox.Abstractions;
using Microsoft.Extensions.Logging;

namespace ProcessSandbox.Tests.IPC;

/// <summary>
/// Tests for the RequestResponseChannel.
/// </summary>
public class RequestResponseChannelTests
{
    private readonly ILoggerFactory _loggerFactory;

    /// <summary>
    /// Initializes a new instance of the <see cref="RequestResponseChannelTests"/> class.
    /// </summary>
    public RequestResponseChannelTests()
    {
        _loggerFactory = LoggerFactory.Create(builder =>
        {
            // Minimum level for logging (e.g., Information, Debug, or Trace)
            builder.SetMinimumLevel(LogLevel.Debug);

            // Add the Debug provider
            builder.AddConsole();
        });

    }


    /// <summary>
    /// Tests that sending a request and receiving a response works correctly.
    /// </summary>
    [Fact]
    public async Task SendRequest_ReceivesResponse_Successfully()
    {
        // Arrange
        var pipeName = PipeNameGenerator.Generate();
        using var server = new NamedPipeServerChannel(pipeName);
        using var client = new NamedPipeClientChannel(pipeName);

        var serverTask = server.WaitForConnectionAsync();
        await Task.Delay(100);
        await client.ConnectAsync();
        await serverTask;

        using var requestResponseChannel = new RequestResponseChannel(client, _loggerFactory.CreateLogger<RequestResponseChannel>());

        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "TestMethod", 5000);

        // Start a task to receive and respond
        var responderTask = Task.Run(async () =>
        {
            var msg = await server.ReceiveMessageAsync();
            var inv = msg!.GetMethodInvocation();

            var result = MethodResultMessage.CreateSuccess(
                inv.CorrelationId,
                MessagePackSerializer.Serialize("result"),
                "System.String");

            await server.SendMessageAsync(IpcMessage.FromMethodResult(result));
        });

        // Act
        var resultMessage = await requestResponseChannel.SendRequestAsync(invocation);
        await responderTask;

        // Assert
        Assert.True(resultMessage.Success);
        Assert.Equal(invocation.CorrelationId, resultMessage.CorrelationId);
    }

    /// <summary>
    /// Tests that a request times out if no response is received.
    /// </summary>
    /// <returns></returns>
    [Fact]
    public async Task SendRequest_Timeout_ThrowsException()
    {
        // Arrange
        var pipeName = PipeNameGenerator.Generate();
        using var server = new NamedPipeServerChannel(pipeName);
        using var client = new NamedPipeClientChannel(pipeName);

        var serverTask = server.WaitForConnectionAsync();
        await Task.Delay(100);
        await client.ConnectAsync();
        await serverTask;

        using var requestResponseChannel = new RequestResponseChannel(client, _loggerFactory.CreateLogger<RequestResponseChannel>());

        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "SlowMethod", 500);

        // Don't send a response - let it timeout

        // Act & Assert
        await Assert.ThrowsAsync<MethodTimeoutException>(
            () => requestResponseChannel.SendRequestAsync(invocation));
    }

}