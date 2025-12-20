using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using MessagePack;
using ProcessSandBox.Abstractions.IPC;
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
    }

}