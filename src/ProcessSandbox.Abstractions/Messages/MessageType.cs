namespace ProcessSandbox.Abstractions.Messages;

/// <summary>
/// Defines the types of messages that can be sent over IPC.
/// </summary>
public enum MessageType : byte
{
    /// <summary>
    /// Method invocation request from proxy to worker.
    /// </summary>
    MethodInvocation = 1,

    /// <summary>
    /// Method result response from worker to proxy.
    /// </summary>
    MethodResult = 2,

    /// <summary>
    /// Health report from worker to proxy.
    /// </summary>
    HealthReport = 3,

    /// <summary>
    /// Shutdown command from proxy to worker.
    /// </summary>
    Shutdown = 4,

    /// <summary>
    /// Ping request to check if worker is responsive.
    /// </summary>
    Ping = 5,

    /// <summary>
    /// Pong response to ping request.
    /// </summary>
    Pong = 6
}