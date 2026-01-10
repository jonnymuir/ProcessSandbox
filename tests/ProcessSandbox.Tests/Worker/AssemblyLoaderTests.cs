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
        var assemblyPath = typeof(TestServiceImpl).Assembly.Location;
        var typeName = typeof(TestServiceImpl).FullName!;
        var loader = new AssemblyLoader(NullLogger<AssemblyLoader>.Instance,assemblyPath, typeName);

        // Act
        var instance = loader.CreateInstance();

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
        var assemblyPath = "nonexistent.dll";
        var typeName = "NonExistent.Type";

        // Act & Assert
        Assert.Throws<AssemblyLoadException>(
            () => new AssemblyLoader(NullLogger<AssemblyLoader>.Instance,assemblyPath, typeName));
    }

    /// <summary>
    /// Attempts to load a valid assembly but a non-existent type and expects an exception.
    /// </summary>
    [Fact]
    public void LoadAndCreateInstance_NonExistentType_ThrowsException()
    {
        // Arrange
        var assemblyPath = typeof(TestServiceImpl).Assembly.Location;
        var typeName = "NonExistent.Type";

        // Act & Assert
        Assert.Throws<AssemblyLoadException>(
            () => new AssemblyLoader(NullLogger<AssemblyLoader>.Instance,assemblyPath, typeName));
    }
}