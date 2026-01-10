using System;
using System.IO;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;

namespace ProcessSandbox.Worker;

/// <summary>
/// Loads assemblies and creates instances of specified types.
/// </summary>
public class AssemblyLoader : ILoader
{
    private readonly ILogger<AssemblyLoader> logger;
    private readonly string assemblyPath;
    private readonly string typeName;
    private readonly Type targetType;

    /// <summary>
    /// Loads an assembly and creates an instance of the specified type.
    /// </summary>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="AssemblyLoadException"></exception>
    public AssemblyLoader(ILogger<AssemblyLoader> logger, string assemblyPath, string typeName)
    {
        this.logger = logger;
        this.assemblyPath = assemblyPath;
        this.typeName = typeName;

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
        Type? type = null;
        
        try
        {
            // Try exact match first
            type = assembly.GetType(typeName, throwOnError: false);

            // If not found, try case-insensitive search
            if (type == null)
            {
                foreach (var assemblyType in assembly.GetTypes())
                {
                    if (string.Equals(assemblyType.FullName, typeName, StringComparison.OrdinalIgnoreCase))
                    {
                        type = assemblyType;
                        break;
                    }
                }
            }

            if (type == null)
            {
                throw new AssemblyLoadException(
                    $"Type '{typeName}' not found in assembly '{assembly.FullName}'",
                    assemblyPath,
                    typeName);
            }

            logger.LogInformation("Type found: {TypeName}", type.FullName);

            targetType = type;
        }
        catch (Exception ex) when (ex is not AssemblyLoadException)
        {
            throw new AssemblyLoadException(
                $"Failed to find type '{typeName}' in assembly {assemblyPath}",
                ex);
        }
    }

    /// <summary>
    /// Creates an instance
    /// </summary>
    /// <returns>An instance of the specified type.</returns>
    public object CreateInstance()
    {
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
    /// Gets the target type.
    /// </summary>
    /// <returns></returns>
    public Type GetTargetType()
    {
        return targetType;
    }   
}