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
/// Tests for the AssemblyLoader class.
/// </summary>
public class AssemblyLoaderTests
{
    /// <summary>
    /// Loads a valid assembly and creates an instance successfully.
    /// </summary>
    [Fact]
    public void LoadAndCreateInstance_ValidAssembly_CreatesInstance()
    {
        // Arrange
        var loader = new AssemblyLoader(NullLogger<AssemblyLoader>.Instance);
        var assemblyPath = typeof(TestServiceImpl).Assembly.Location;
        var typeName = typeof(TestServiceImpl).FullName!;

        // Act
        var instance = loader.LoadAndCreateInstance(assemblyPath, typeName);

        // Assert
        Assert.NotNull(instance);
        Assert.IsType<TestServiceImpl>(instance);
    }

    /// <summary>
    /// Attempts to load a non-existent assembly or type and expects an exception.
    /// </summary>
    [Fact]
    public void LoadAndCreateInstance_NonExistentAssembly_ThrowsException()
    {
        // Arrange
        var loader = new AssemblyLoader(NullLogger<AssemblyLoader>.Instance);
        var assemblyPath = "nonexistent.dll";
        var typeName = "NonExistent.Type";

        // Act & Assert
        Assert.Throws<AssemblyLoadException>(
            () => loader.LoadAndCreateInstance(assemblyPath, typeName));
    }

    /// <summary>
    /// Attempts to load a valid assembly but a non-existent type and expects an exception.
    /// </summary>
    [Fact]
    public void LoadAndCreateInstance_NonExistentType_ThrowsException()
    {
        // Arrange
        var loader = new AssemblyLoader(NullLogger<AssemblyLoader>.Instance);
        var assemblyPath = typeof(TestServiceImpl).Assembly.Location;
        var typeName = "NonExistent.Type";

        // Act & Assert
        Assert.Throws<AssemblyLoadException>(
            () => loader.LoadAndCreateInstance(assemblyPath, typeName));
    }

    /// <summary>
    /// Loads a valid assembly and creates an instance with constructor arguments successfully.
    /// </summary>
    [Fact]
    public void LoadAndCreateInstance_WithConstructorArgs_CreatesInstance()
    {
        // Arrange
        var loader = new AssemblyLoader(NullLogger<AssemblyLoader>.Instance);
        var assemblyPath = typeof(SlowServiceImpl).Assembly.Location;
        var typeName = typeof(SlowServiceImpl).FullName!;
        var args = new object[] { 500 }; // delay in ms

        // Act
        var instance = loader.LoadAndCreateInstance(assemblyPath, typeName, args);

        // Assert
        Assert.NotNull(instance);
        Assert.IsType<SlowServiceImpl>(instance);
    }
}