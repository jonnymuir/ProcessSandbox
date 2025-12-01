using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using MessagePack;
using ProcessSandbox.IPC;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandbox.Tests.IPC;

/// <summary>
/// Tests for the PipeNameGenerator utility.
/// </summary>
public class PipeNameGeneratorTests
{
    /// <summary>
    /// Tests that Generate creates unique pipe names.
    /// </summary>
    [Fact]
    public void Generate_CreatesUniqueNames()
    {
        // Act
        var name1 = PipeNameGenerator.Generate();
        var name2 = PipeNameGenerator.Generate();

        // Assert
        Assert.NotEqual(name1, name2);
        Assert.StartsWith("ProcessSandbox_", name1);
        Assert.StartsWith("ProcessSandbox_", name2);
    }

    /// <summary>
    /// Tests that Generate uses the specified prefix.
    /// </summary>
    [Fact]
    public void Generate_WithPrefix_UsesPrefix()
    {
        // Act
        var name = PipeNameGenerator.Generate("MyApp");

        // Assert
        Assert.StartsWith("MyApp_", name);
    }

    /// <summary>
    /// Tests that GenerateDeterministic creates the same name for the same key.
    /// </summary>
    [Fact]
    public void GenerateDeterministic_SameKey_SameName()
    {
        // Act
        var name1 = PipeNameGenerator.GenerateDeterministic("test-key");
        var name2 = PipeNameGenerator.GenerateDeterministic("test-key");

        // Assert
        Assert.Equal(name1, name2);
    }

    /// <summary>
    /// Tests that GenerateDeterministic creates different names for different keys.
    /// </summary>
    [Fact]
    public void GenerateDeterministic_DifferentKeys_DifferentNames()
    {
        // Act
        var name1 = PipeNameGenerator.GenerateDeterministic("key1");
        var name2 = PipeNameGenerator.GenerateDeterministic("key2");

        // Assert
        Assert.NotEqual(name1, name2);
    }

    /// <summary>
    /// Tests that IsValidPipeName correctly identifies valid and invalid pipe names.
    /// </summary>
    [Fact]
    public void IsValidPipeName_ValidName_ReturnsTrue()
    {
        // Arrange
        var validName = "MyPipe_123";

        // Act
        var result = PipeNameGenerator.IsValidPipeName(validName);

        // Assert
        Assert.True(result);
    }

    /// <summary>
    /// Tests that IsValidPipeName correctly identifies invalid pipe names.
    /// </summary>
    [Fact]
    public void IsValidPipeName_InvalidCharacters_ReturnsFalse()
    {
        // Arrange
        var invalidNames = new[] { "My\\Pipe", "My/Pipe", "My:Pipe", "My*Pipe" };

        // Act & Assert
        foreach (var name in invalidNames)
        {
            Assert.False(PipeNameGenerator.IsValidPipeName(name));
        }
    }

    /// <summary>
    /// Tests that IsValidPipeName returns false for empty names.
    /// </summary>
    [Fact]
    public void IsValidPipeName_EmptyOrNull_ReturnsFalse()
    {
        // Act & Assert
        Assert.False(PipeNameGenerator.IsValidPipeName(""));
        Assert.False(PipeNameGenerator.IsValidPipeName("   "));
    }
}