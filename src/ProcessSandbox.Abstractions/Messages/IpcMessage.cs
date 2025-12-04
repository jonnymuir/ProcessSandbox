using System;
using MessagePack;

namespace ProcessSandbox.Abstractions.Messages;

/// <summary>
/// Wrapper for all IPC messages with type discriminator.
/// </summary>
[MessagePackObject]
public class IpcMessage
{
    /// <summary>
    /// Type of the message being sent.
    /// </summary>
    [Key(0)]
    public MessageType MessageType { get; set; }

    /// <summary>
    /// Serialized message payload.
    /// </summary>
    [Key(1)]
    public byte[] Payload { get; set; } = [];

    /// <summary>
    /// Timestamp when the message was created.
    /// </summary>
    [Key(2)]
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Creates a new IPC message.
    /// </summary>
    public IpcMessage()
    {
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates an IPC message with specified type and payload.
    /// </summary>
    /// <param name="messageType">The type of message.</param>
    /// <param name="payload">The serialized message payload.</param>
    public IpcMessage(MessageType messageType, byte[] payload)
    {
        MessageType = messageType;
        Payload = payload ?? throw new ArgumentNullException(nameof(payload));
        Timestamp = DateTime.UtcNow;
    }

    /// <summary>
    /// Creates an IPC message for a method invocation.
    /// </summary>
    /// <param name="invocation">The method invocation message.</param>
    /// <returns>A new IPC message.</returns>
    public static IpcMessage FromMethodInvocation(MethodInvocationMessage invocation)
    {
        var payload = MessagePackSerializer.Serialize(invocation);
        return new IpcMessage(MessageType.MethodInvocation, payload);
    }

    /// <summary>
    /// Creates an IPC message for a method result.
    /// </summary>
    /// <param name="result">The method result message.</param>
    /// <returns>A new IPC message.</returns>
    public static IpcMessage FromMethodResult(MethodResultMessage result)
    {
        var payload = MessagePackSerializer.Serialize(result);
        return new IpcMessage(MessageType.MethodResult, payload);
    }

    /// <summary>
    /// Creates an IPC message for a health report.
    /// </summary>
    /// <param name="report">The health report message.</param>
    /// <returns>A new IPC message.</returns>
    public static IpcMessage FromHealthReport(HealthReportMessage report)
    {
        var payload = MessagePackSerializer.Serialize(report);
        return new IpcMessage(MessageType.HealthReport, payload);
    }

    /// <summary>
    /// Creates a shutdown message.
    /// </summary>
    /// <returns>A new shutdown IPC message.</returns>
    public static IpcMessage CreateShutdown()
    {
        return new IpcMessage(MessageType.Shutdown, []);
    }

    /// <summary>
    /// Creates a ping message.
    /// </summary>
    /// <returns>A new ping IPC message.</returns>
    public static IpcMessage CreatePing()
    {
        return new IpcMessage(MessageType.Ping, []);
    }

    /// <summary>
    /// Creates a pong message.
    /// </summary>
    /// <returns>A new pong IPC message.</returns>
    public static IpcMessage CreatePong()
    {
        return new IpcMessage(MessageType.Pong, []);
    }

    /// <summary>
    /// Deserializes the payload as a method invocation message.
    /// </summary>
    /// <returns>The deserialized method invocation.</returns>
    public MethodInvocationMessage GetMethodInvocation()
    {
        if (MessageType != MessageType.MethodInvocation)
            throw new InvalidOperationException($"Message type is {MessageType}, not MethodInvocation");

        return MessagePackSerializer.Deserialize<MethodInvocationMessage>(Payload);
    }

    /// <summary>
    /// Deserializes the payload as a method result message.
    /// </summary>
    /// <returns>The deserialized method result.</returns>
    public MethodResultMessage GetMethodResult()
    {
        if (MessageType != MessageType.MethodResult)
            throw new InvalidOperationException($"Message type is {MessageType}, not MethodResult");

        return MessagePackSerializer.Deserialize<MethodResultMessage>(Payload);
    }

    /// <summary>
    /// Deserializes the payload as a health report message.
    /// </summary>
    /// <returns>The deserialized health report.</returns>
    public HealthReportMessage GetHealthReport()
    {
        if (MessageType != MessageType.HealthReport)
            throw new InvalidOperationException($"Message type is {MessageType}, not HealthReport");

        return MessagePackSerializer.Deserialize<HealthReportMessage>(Payload);
    }
}