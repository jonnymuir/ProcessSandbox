using System;
using System.Security.Cryptography;
using System.Text;

namespace ProcessSandBox.Abstractions.IPC;

/// <summary>
/// Generates unique names for named pipes.
/// </summary>
public static class PipeNameGenerator
{
    /// <summary>
    /// Generates a unique pipe name
    /// </summary>
    /// <returns>A unique pipe name.</returns>
    public static string Generate()
    {
        return Guid.NewGuid().ToString("N");
        
    }
} 