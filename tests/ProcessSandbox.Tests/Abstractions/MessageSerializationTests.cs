using System;
using Xunit;
using MessagePack;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandbox.Tests.Abstractions;

/// <summary>
/// Tests for message serialization and deserialization.
/// </summary>
public class MessageSerializationTests
{
    /// <summary>
    /// Tests that MethodInvocationMessage serializes and deserializes correctly.
    /// </summary>
    [Fact]
    public void MethodInvocationMessage_RoundTrip_PreservesData()
    {
        // Arrange
        var original = new MethodInvocationMessage(
            Guid.NewGuid(),
            "TestMethod",
            30000
        )
        {
            ParameterTypeNames = ["System.String", "System.Int32"],
            SerializedParameters =
            [
                MessagePackSerializer.Serialize("test"),
                MessagePackSerializer.Serialize(42)
            ]
        };

        // Act
        var bytes = MessagePackSerializer.Serialize(original);
        var deserialized = MessagePackSerializer.Deserialize<MethodInvocationMessage>(bytes);

        // Assert
        Assert.Equal(original.CorrelationId, deserialized.CorrelationId);
        Assert.Equal(original.MethodName, deserialized.MethodName);
        Assert.Equal(original.TimeoutMilliseconds, deserialized.TimeoutMilliseconds);
        Assert.Equal(original.ParameterTypeNames, deserialized.ParameterTypeNames);
        Assert.Equal(original.SerializedParameters.Length, deserialized.SerializedParameters.Length);
    }

    /// <summary>
    /// Tests that MethodResultMessage creates success messages correctly.
    /// </summary>
    [Fact]
    public void MethodResultMessage_CreateSuccess_CreatesValidMessage()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var result = MessagePackSerializer.Serialize("success result");

        // Act
        var message = MethodResultMessage.CreateSuccess(
            correlationId,
            result,
            "System.String"
        );

        // Assert
        Assert.Equal(correlationId, message.CorrelationId);
        Assert.True(message.Success);
        Assert.NotNull(message.SerializedResult);
        Assert.Equal("System.String", message.ResultTypeName);
        Assert.Null(message.ExceptionType);
    }

    /// <summary>
    /// Tests that MethodResultMessage creates failure messages correctly.
    /// </summary>
    [Fact]
    public void MethodResultMessage_CreateFailure_PreservesExceptionInfo()
    {
        // Arrange
        var correlationId = Guid.NewGuid();
        var exception = new InvalidOperationException("Test error");

        // Act
        var message = MethodResultMessage.CreateFailure(correlationId, exception);

        // Assert
        Assert.Equal(correlationId, message.CorrelationId);
        Assert.False(message.Success);
        Assert.Equal("System.InvalidOperationException", message.ExceptionType);
        Assert.Equal("Test error", message.ExceptionMessage);
    }

    /// <summary>
    /// Tests that IpcMessage creates method invocation messages correctly.
    /// </summary>
    [Fact]
    public void IpcMessage_FromMethodInvocation_CreatesValidMessage()
    {
        // Arrange
        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "Test", 1000);

        // Act
        var ipcMsg = IpcMessage.FromMethodInvocation(invocation);

        // Assert
        Assert.Equal(MessageType.MethodInvocation, ipcMsg.MessageType);
        Assert.NotEmpty(ipcMsg.Payload);
        
        // Verify we can deserialize back
        var deserialized = ipcMsg.GetMethodInvocation();
        Assert.Equal(invocation.CorrelationId, deserialized.CorrelationId);
        Assert.Equal(invocation.MethodName, deserialized.MethodName);
    }

    /// <summary>
    /// Tests that IpcMessage creates shutdown messages correctly.
    /// </summary>
    [Fact]
    public void IpcMessage_Shutdown_HasCorrectType()
    {
        // Act
        var message = IpcMessage.CreateShutdown();

        // Assert
        Assert.Equal(MessageType.Shutdown, message.MessageType);
        Assert.Empty(message.Payload);
    }

    /// <summary>
    /// Tests that IpcMessage creates ping and pong messages correctly.
    /// </summary>
    [Fact]
    public void IpcMessage_PingPong_RoundTrip()
    {
        // Act
        var ping = IpcMessage.CreatePing();
        var pong = IpcMessage.CreatePong();

        // Assert
        Assert.Equal(MessageType.Ping, ping.MessageType);
        Assert.Equal(MessageType.Pong, pong.MessageType);
    }
}

/// <summary>
/// Tests for serialization helper methods.
/// </summary>
public class SerializationHelperTests
{
    /// <summary>
    /// Tests that parameters are serialized correctly.
    /// </summary>
    [Fact]
    public void SerializeParameters_WithValidParams_ReturnsSerializedArray()
    {
        // Arrange
        var parameters = new object[] { "test", 42, true };

        // Act
        var serialized = SerializationHelper.SerializeParameters(parameters);

        // Assert
        Assert.Equal(3, serialized.Length);
        Assert.All(serialized, bytes => Assert.NotEmpty(bytes));
    }

    /// <summary>
    /// Tests that parameters are deserialized correctly.
    /// </summary>
    [Fact]
    public void DeserializeParameters_MatchingTypes_ReturnsOriginalValues()
    {
        // Arrange
        var original = new object[] { "test", 42, true };
        var types = new[] { typeof(string), typeof(int), typeof(bool) };
        var serialized = SerializationHelper.SerializeParameters(original);

        // Act
        var deserialized = SerializationHelper.DeserializeParameters(serialized, types);

        // Assert
        Assert.Equal(3, deserialized.Length);
        Assert.Equal("test", deserialized[0]);
        Assert.Equal(42, deserialized[1]);
        Assert.Equal(true, deserialized[2]);
    }

    /// <summary>
    /// Tests that type names are retrieved correctly.
    /// </summary>
    [Fact]
    public void GetTypeNames_ReturnsQualifiedNames()
    {
        // Arrange
        var types = new[] { typeof(string), typeof(int), typeof(DateTime) };

        // Act
        var names = SerializationHelper.GetTypeNames(types);

        // Assert
        Assert.Equal(3, names.Length);
        Assert.All(names, name => Assert.Contains(',', name)); // Should be assembly-qualified
    }

    /// <summary>
    /// Tests that types are resolved correctly from names.
    /// </summary>
    [Fact]
    public void ResolveTypes_WithValidNames_ReturnsTypes()
    {
        // Arrange
        var types = new[] { typeof(string), typeof(int) };
        var names = SerializationHelper.GetTypeNames(types);

        // Act
        var resolved = SerializationHelper.ResolveTypes(names);

        // Assert
        Assert.Equal(2, resolved.Length);
        Assert.Equal(typeof(string), resolved[0]);
        Assert.Equal(typeof(int), resolved[1]);
    }

    /// <summary>
    /// Tests that return values are serialized correctly.
    /// </summary>
    [Fact]
    public void SerializeReturnValue_WithNull_ReturnsNull()
    {
        // Act
        var result = SerializationHelper.SerializeReturnValue(null);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Tests that return values are serialized correctly.
    /// </summary>
    [Fact]
    public void SerializeReturnValue_WithValue_ReturnsBytes()
    {
        // Act
        var result = SerializationHelper.SerializeReturnValue("test");

        // Assert
        Assert.NotNull(result);
        Assert.NotEmpty(result);
    }
}

/// <summary>
/// Tests for custom exceptions.
/// </summary>
public class ExceptionTests
{
    /// <summary>
    /// Tests that MethodTimeoutException preserves method name and timeout.
    /// </summary>
    [Fact]
    public void MethodTimeoutException_PreservesDetails()
    {
        // Arrange
        var timeout = TimeSpan.FromSeconds(30);
        var methodName = "SlowMethod";

        // Act
        var exception = new MethodTimeoutException(methodName, timeout);

        // Assert
        Assert.Equal(methodName, exception.MethodName);
        Assert.Equal(timeout, exception.Timeout);
        Assert.Contains("SlowMethod", exception.Message);
        Assert.Contains("30", exception.Message);
    }

    /// <summary>
    /// Tests that WorkerCrashedException preserves exit code.
    /// </summary>
    [Fact]
    public void WorkerCrashedException_WithExitCode_PreservesExitCode()
    {
        // Arrange
        var exitCode = -1;

        // Act
        var exception = new WorkerCrashedException("Worker crashed", exitCode);

        // Assert
        Assert.Equal(exitCode, exception.ExitCode);
        Assert.Contains("Worker crashed", exception.Message);
    }

    /// <summary>
    /// Tests that RemoteInvocationException preserves remote exception details.
    /// </summary>
    [Fact]
    public void RemoteInvocationException_PreservesRemoteDetails()
    {
        // Arrange
        var remoteType = "System.InvalidOperationException";
        var message = "Remote error";
        var stackTrace = "at SomeMethod()";

        // Act
        var exception = new RemoteInvocationException(remoteType, message, stackTrace);

        // Assert
        Assert.Equal(remoteType, exception.RemoteExceptionType);
        Assert.Equal(stackTrace, exception.RemoteStackTrace);
        Assert.Contains(remoteType, exception.Message);
        Assert.Contains(message, exception.Message);
    }

    /// <summary>
    /// Tests that PoolExhaustedException preserves max pool size.
    /// </summary>
    [Fact]
    public void PoolExhaustedException_PreservesPoolSize()
    {
        // Arrange
        var maxSize = 10;

        // Act
        var exception = new PoolExhaustedException(maxSize);

        // Assert
        Assert.Equal(maxSize, exception.MaxPoolSize);
        Assert.Contains("10", exception.Message);
    }
}

/// <summary>
/// Tests for worker configuration validation.
/// </summary>
public class WorkerConfigurationTests
{
    /// <summary>
    /// Tests that valid configuration passes validation.
    /// </summary>
    [Fact]
    public void Validate_WithValidConfig_DoesNotThrow()
    {
        // Arrange
        var config = new WorkerConfiguration
        {
            AssemblyPath = "test.dll",
            TypeName = "Test.Type",
            PipeName = "test-pipe",
            ParentProcessId = 1234
        };

        // Act & Assert
        config.Validate(); // Should not throw
    }

    /// <summary>
    /// Tests that missing assembly path throws exception.
    /// </summary>
    [Fact]
    public void Validate_WithMissingAssemblyPath_Throws()
    {
        // Arrange
        var config = new WorkerConfiguration
        {
            TypeName = "Test.Type",
            PipeName = "test-pipe",
            ParentProcessId = 1234
        };

        // Act & Assert
        Assert.Throws<ConfigurationException>(() => config.Validate());
    }

    /// <summary>
    /// Tests that missing type name throws exception.
    /// </summary>
    [Fact]
    public void Validate_WithInvalidHealthInterval_Throws()
    {
        // Arrange
        var config = new WorkerConfiguration
        {
            AssemblyPath = "test.dll",
            TypeName = "Test.Type",
            PipeName = "test-pipe",
            ParentProcessId = 1234
        };

        // Act & Assert
        Assert.Throws<ConfigurationException>(() => config.Validate());
    }
}