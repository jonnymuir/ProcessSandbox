using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Xunit;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using MessagePack;
using ProcessSandbox.Worker;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;
using ProcessSandbox.Tests.TestImplementations;

namespace ProcessSandbox.Tests.Worker;

/// <summary>
/// Tests for the MethodInvoker class.
/// </summary>
public class MethodInvokerTests
{
    /// <summary>
    /// Invokes a simple method and verifies the result.
    /// </summary>
    [Fact]
    public void InvokeMethod_SimpleMethod_ReturnsSuccess()
    {
        // Arrange
        var target = new TestServiceImpl();
        var invoker = new ProcessSandbox.Worker.MethodInvoker(target, target.GetType());

        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "Echo", 5000)
        {
            ParameterTypeNames = [typeof(string).AssemblyQualifiedName!],
            SerializedParameters = [MessagePackSerializer.Serialize("test")]
        };

        // Act
        var result = invoker.InvokeMethod(invocation);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(invocation.CorrelationId, result.CorrelationId);
        
        var returnValue = MessagePackSerializer.Deserialize<string>(result.SerializedResult!);
        Assert.Equal("test", returnValue);
    }

    /// <summary>
    /// Invokes a void method and verifies success.
    /// </summary>
    [Fact]
    public void InvokeMethod_VoidMethod_ReturnsSuccess()
    {
        // Arrange
        var target = new TestServiceImpl();
        var invoker = new ProcessSandbox.Worker.MethodInvoker(target, target.GetType());

        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "DoNothing", 5000)
        {
            ParameterTypeNames = Array.Empty<string>(),
            SerializedParameters = Array.Empty<byte[]>()
        };

        // Act
        var result = invoker.InvokeMethod(invocation);

        // Assert
        Assert.True(result.Success);
        Assert.Null(result.SerializedResult);
    }

    /// <summary>
    /// Invokes a method with multiple parameters and verifies the result.
    /// </summary>
    [Fact]
    public void InvokeMethod_MethodWithMultipleParams_ReturnsSuccess()
    {
        // Arrange
        var target = new TestServiceImpl();
        var invoker = new ProcessSandbox.Worker.MethodInvoker(target, target.GetType());

        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "Add", 5000)
        {
            ParameterTypeNames =
            [
                typeof(int).AssemblyQualifiedName!,
                typeof(int).AssemblyQualifiedName!
            ],
            SerializedParameters =
            [
                MessagePackSerializer.Serialize(5),
                MessagePackSerializer.Serialize(3)
            ]
        };

        // Act
        var result = invoker.InvokeMethod(invocation);

        // Assert
        Assert.True(result.Success);
        var returnValue = MessagePackSerializer.Deserialize<int>(result.SerializedResult!);
        Assert.Equal(8, returnValue);
    }

    /// <summary>
    /// Invokes a method that throws an exception and verifies failure.
    /// </summary>
    [Fact]
    public void InvokeMethod_MethodThrowsException_ReturnsFailure()
    {
        // Arrange
        var target = new ThrowingServiceImpl();
        var invoker = new ProcessSandbox.Worker.MethodInvoker(target, target.GetType());

        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "Echo", 5000)
        {
            ParameterTypeNames = [typeof(string).AssemblyQualifiedName!],
            SerializedParameters = [MessagePackSerializer.Serialize("test")]
        };

        // Act
        var result = invoker.InvokeMethod(invocation);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("System.InvalidOperationException", result.ExceptionType);
        Assert.Contains("Echo failed", result.ExceptionMessage);
    }

    /// <summary>
    /// Invokes a non-existent method and verifies failure.
    /// </summary>
    [Fact]
    public void InvokeMethod_NonExistentMethod_ReturnsFailure()
    {
        // Arrange
        var target = new TestServiceImpl();
        var invoker = new ProcessSandbox.Worker.MethodInvoker(target, target.GetType());

        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "NonExistentMethod", 5000)
        {
            ParameterTypeNames = Array.Empty<string>(),
            SerializedParameters = Array.Empty<byte[]>()
        };

        // Act
        var result = invoker.InvokeMethod(invocation);

        // Assert
        Assert.False(result.Success);
        Assert.Contains("not found", result.ExceptionMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Invokes a method with a byte array parameter and verifies the result.
    /// </summary>
    [Fact]
    public void InvokeMethod_ByteArrayParameter_HandlesCorrectly()
    {
        // Arrange
        var target = new TestServiceImpl();
        var invoker = new ProcessSandbox.Worker.MethodInvoker(target, target.GetType());

        var inputBytes = new byte[] { 1, 2, 3, 4, 5 };
        var invocation = new MethodInvocationMessage(Guid.NewGuid(), "ProcessBytes", 5000)
        {
            ParameterTypeNames = [typeof(byte[]).AssemblyQualifiedName!],
            SerializedParameters = [MessagePackSerializer.Serialize(inputBytes)]
        };

        // Act
        var result = invoker.InvokeMethod(invocation);

        // Assert
        Assert.True(result.Success);
        var returnBytes = MessagePackSerializer.Deserialize<byte[]>(result.SerializedResult!);
        Assert.Equal(new byte[] { 2, 3, 4, 5, 6 }, returnBytes);
    }
}