using System;
using MessagePack;

namespace ProcessSandbox.Abstractions.Messages
{
    /// <summary>
    /// Represents a method invocation request sent from proxy to worker.
    /// </summary>
    [MessagePackObject]
    public class MethodInvocationMessage
    {
        /// <summary>
        /// Unique identifier for correlating requests and responses.
        /// </summary>
        [Key(0)]
        public Guid CorrelationId { get; set; }

        /// <summary>
        /// Name of the method to invoke.
        /// </summary>
        [Key(1)]
        public string MethodName { get; set; } = string.Empty;

        /// <summary>
        /// Full type names of the method parameters for overload resolution.
        /// </summary>
        [Key(2)]
        public string[] ParameterTypeNames { get; set; } = [];

        /// <summary>
        /// Serialized parameter values.
        /// </summary>
        [Key(3)]
        public byte[][] SerializedParameters { get; set; } = [];

        /// <summary>
        /// Timeout for the method execution in milliseconds.
        /// </summary>
        [Key(4)]
        public int TimeoutMilliseconds { get; set; }

        /// <summary>
        /// Creates a new method invocation message.
        /// </summary>
        public MethodInvocationMessage()
        {
            CorrelationId = Guid.NewGuid();
        }

        /// <summary>
        /// Creates a new method invocation message with specified correlation ID.
        /// </summary>
        /// <param name="correlationId">The correlation ID for this invocation.</param>
        /// <param name="methodName">The name of the method to invoke.</param>
        /// <param name="timeoutMilliseconds">The timeout in milliseconds.</param>
        public MethodInvocationMessage(Guid correlationId, string methodName, int timeoutMilliseconds)
        {
            CorrelationId = correlationId;
            MethodName = methodName ?? throw new ArgumentNullException(nameof(methodName));
            TimeoutMilliseconds = timeoutMilliseconds;
        }
    }
}