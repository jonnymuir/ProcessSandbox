using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;

namespace ProcessSandbox.Worker;

/// <summary>
/// Loads assemblies and creates instances of types dynamically.
/// </summary>
/// <remarks>
/// 
/// </remarks>
/// <param name="logger"></param>
/// <exception cref="ArgumentNullException"></exception>
public class AssemblyLoader(ILogger<AssemblyLoader> logger)
{
    /// <summary>
    /// Loads an assembly and creates an instance of the specified type.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file.</param>
    /// <param name="typeName">Full name of the type to instantiate.</param>
    /// <returns>An instance of the specified type.</returns>
    public object LoadAndCreateInstance(string assemblyPath, string typeName)
    {
        if (string.IsNullOrWhiteSpace(assemblyPath))
            throw new ArgumentNullException(nameof(assemblyPath));
        if (string.IsNullOrWhiteSpace(typeName))
            throw new ArgumentNullException(nameof(typeName));

        logger.LogInformation("Loading assembly: {AssemblyPath}", assemblyPath);

        // Resolve full path
        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            throw new AssemblyLoadException(
                $"Assembly file not found: {fullPath}",
                assemblyPath);
        }

        Assembly assembly;
        try
        {
            // Load the assembly
            assembly = Assembly.LoadFrom(fullPath);
            logger.LogInformation("Assembly loaded: {AssemblyName}", assembly.FullName);
        }
        catch (Exception ex)
        {
            throw new AssemblyLoadException(
                $"Failed to load assembly from {fullPath}",
                ex);
        }

        // Find the type
        Type? targetType = null;
        
        try
        {
            // Try exact match first
            targetType = assembly.GetType(typeName, throwOnError: false);

            // If not found, try case-insensitive search
            if (targetType == null)
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (string.Equals(type.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        targetType = type;
                        break;
                    }
                }
            }

            if (targetType == null)
            {
                throw new AssemblyLoadException(
                    $"Type '{typeName}' not found in assembly '{assembly.FullName}'",
                    assemblyPath,
                    typeName);
            }

            logger.LogInformation("Type found: {TypeName}", targetType.FullName);
        }
        catch (Exception ex) when (ex is not AssemblyLoadException)
        {
            throw new AssemblyLoadException(
                $"Failed to find type '{typeName}' in assembly {assemblyPath}",
                ex);
        }

        // Create instance
        object instance;
        try
        {
            instance = Activator.CreateInstance(targetType)
                ?? throw new AssemblyLoadException(
                    $"Activator.CreateInstance returned null for type {targetType.FullName}",
                    assemblyPath,
                    typeName);

            logger.LogInformation("Instance created successfully");
        }
        catch (Exception ex)
        {
            throw new AssemblyLoadException(
                $"Failed to create instance of type '{targetType.FullName} in {assemblyPath}'. " +
                $"Ensure the type has a public parameterless constructor.",
                ex);
        }

        return instance;
    }

    /// <summary>
    /// Loads an assembly and creates an instance with constructor parameters.
    /// </summary>
    /// <param name="assemblyPath">Path to the assembly file.</param>
    /// <param name="typeName">Full name of the type to instantiate.</param>
    /// <param name="constructorArgs">Constructor arguments.</param>
    /// <returns>An instance of the specified type.</returns>
    public object LoadAndCreateInstance(
        string assemblyPath, 
        string typeName, 
        object[] constructorArgs)
    {

        logger.LogInformation("Loading assembly with constructor args: {AssemblyPath}", assemblyPath);

        var fullPath = Path.GetFullPath(assemblyPath);
        if (!File.Exists(fullPath))
        {
            throw new AssemblyLoadException(
                $"Assembly file not found: {fullPath}",
                assemblyPath);
        }

        Assembly assembly;
        try
        {
            assembly = Assembly.LoadFrom(fullPath);
        }
        catch (Exception ex)
        {
            throw new AssemblyLoadException(
                $"Failed to load assembly from {fullPath}",
                ex);
        }

        Type? targetType = assembly.GetType(typeName, throwOnError: false);
        if (targetType == null)
        {
            throw new AssemblyLoadException(
                $"Type '{typeName}' not found in assembly '{assembly.FullName}'",
                assemblyPath,
                typeName);
        }

        try
        {
            var instance = Activator.CreateInstance(targetType, constructorArgs)
                ?? throw new AssemblyLoadException(
                    $"Activator.CreateInstance returned null for type {targetType.FullName}",
                    assemblyPath,
                    typeName);

            logger.LogInformation("Instance created with constructor args");
            return instance;
        }
        catch (Exception ex)
        {
            throw new AssemblyLoadException(
                $"Failed to create instance of type '{targetType.FullName}' with provided constructor arguments.",
                ex);
        }
    }
}