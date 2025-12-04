using System;
using MessagePack;

namespace ProcessSandbox.Abstractions.Messages;

/// <summary>
/// Represents the result of a method invocation from worker to proxy.
/// </summary>
[MessagePackObject]
public class MethodResultMessage
{
    /// <summary>
    /// Correlation ID matching the original request.
    /// </summary>
    [Key(0)]
    public Guid CorrelationId { get; set; }

    /// <summary>
    /// Indicates whether the method executed successfully.
    /// </summary>
    [Key(1)]
    public bool Success { get; set; }

    /// <summary>
    /// Serialized return value if successful.
    /// </summary>
    [Key(2)]
    public byte[]? SerializedResult { get; set; }

    /// <summary>
    /// Full type name of the return value.
    /// </summary>
    [Key(3)]
    public string? ResultTypeName { get; set; }

    /// <summary>
    /// Exception type name if failed.
    /// </summary>
    [Key(4)]
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Exception message if failed.
    /// </summary>
    [Key(5)]
    public string? ExceptionMessage { get; set; }

    /// <summary>
    /// Stack trace if failed.
    /// </summary>
    [Key(6)]
    public string? StackTrace { get; set; }

    /// <summary>
    /// Creates a successful result message.
    /// </summary>
    /// <param name="correlationId">The correlation ID from the request.</param>
    /// <param name="serializedResult">The serialized result value.</param>
    /// <param name="resultTypeName">The type name of the result.</param>
    /// <returns>A successful method result message.</returns>
    public static MethodResultMessage CreateSuccess(
        Guid correlationId, 
        byte[]? serializedResult, 
        string? resultTypeName)
    {
        return new MethodResultMessage
        {
            CorrelationId = correlationId,
            Success = true,
            SerializedResult = serializedResult,
            ResultTypeName = resultTypeName
        };
    }

    /// <summary>
    /// Creates a failure result message.
    /// </summary>
    /// <param name="correlationId">The correlation ID from the request.</param>
    /// <param name="exception">The exception that occurred.</param>
    /// <returns>A failure method result message.</returns>
    public static MethodResultMessage CreateFailure(Guid correlationId, Exception exception)
    {
        if (exception == null)
            throw new ArgumentNullException(nameof(exception));

        return new MethodResultMessage
        {
            CorrelationId = correlationId,
            Success = false,
            ExceptionType = exception.GetType().FullName,
            ExceptionMessage = exception.Message,
            StackTrace = exception.StackTrace
        };
    }
}