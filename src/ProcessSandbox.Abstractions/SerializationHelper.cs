using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;

namespace ProcessSandbox.Abstractions;

/// <summary>
/// Helper class for serializing and deserializing method parameters and return values.
/// </summary>
public static class SerializationHelper
{
    /// <summary>
    /// Serializes method parameters.
    /// </summary>
    /// <param name="parameters">The parameters to serialize.</param>
    /// <returns>Array of serialized parameter bytes.</returns>
    public static byte[][] SerializeParameters(object?[] parameters)
    {
        if (parameters == null || parameters.Length == 0)
            return [];

        var result = new byte[parameters.Length][];
        for (int i = 0; i < parameters.Length; i++)
        {
            result[i] = MessagePackSerializer.Serialize(parameters[i]);
        }

        return result;
    }

    /// <summary>
    /// Deserializes method parameters.
    /// </summary>
    /// <param name="serializedParameters">The serialized parameter bytes.</param>
    /// <param name="parameterTypes">The types of the parameters.</param>
    /// <returns>Array of deserialized parameters.</returns>
    public static object?[] DeserializeParameters(byte[][] serializedParameters, Type[] parameterTypes)
    {
        if (serializedParameters == null || serializedParameters.Length == 0)
            return [];

        if (parameterTypes.Length != serializedParameters.Length)
            throw new ArgumentException("Parameter count mismatch");

        var result = new object?[serializedParameters.Length];
        for (int i = 0; i < serializedParameters.Length; i++)
        {
            result[i] = MessagePackSerializer.Deserialize(parameterTypes[i], serializedParameters[i]);
        }

        return result;
    }

    /// <summary>
    /// Serializes a return value.
    /// </summary>
    /// <param name="returnValue">The return value to serialize.</param>
    /// <returns>Serialized bytes, or null for void methods.</returns>
    public static byte[]? SerializeReturnValue(object? returnValue)
    {
        if (returnValue == null)
            return null;

        return MessagePackSerializer.Serialize(returnValue);
    }

    /// <summary>
    /// Deserializes a return value.
    /// </summary>
    /// <param name="serializedValue">The serialized value bytes.</param>
    /// <param name="returnType">The type of the return value.</param>
    /// <returns>The deserialized return value.</returns>
    public static object? DeserializeReturnValue(byte[]? serializedValue, Type returnType)
    {
        if (serializedValue == null || serializedValue.Length == 0)
            return null;

        return MessagePackSerializer.Deserialize(returnType, serializedValue);
    }

    /// <summary>
    /// Gets the full type names for an array of types.
    /// </summary>
    /// <param name="types">The types to get names for.</param>
    /// <returns>Array of full type names.</returns>
    public static string[] GetTypeNames(Type[] types)
    {
        if (types == null || types.Length == 0)
            return [];

        return types.Select(t => t.AssemblyQualifiedName ?? t.FullName ?? t.Name).ToArray();
    }

    /// <summary>
    /// Resolves types from their full type names.
    /// </summary>
    /// <param name="typeNames">The type names to resolve.</param>
    /// <returns>Array of resolved types.</returns>
    public static Type[] ResolveTypes(string[] typeNames)
    {
        if (typeNames == null || typeNames.Length == 0)
            return [];

        var types = new List<Type>();
        foreach (var typeName in typeNames)
        {
            var type = Type.GetType(typeName);
            if (type == null)
                throw new TypeLoadException($"Could not load type: {typeName}");

            types.Add(type);
        }

        return types.ToArray();
    }

    /// <summary>
    /// Gets the full type name for a single type.
    /// </summary>
    /// <param name="type">The type to get the name for.</param>
    /// <returns>The full type name.</returns>
    public static string GetTypeName(Type type)
    {
        if (type == null)
            throw new ArgumentNullException(nameof(type));

        // If it's a primitive/system type, just send the FullName (e.g., "System.Int32")
        // instead of the AssemblyQualifiedName which includes "Version=10.0.0.0"
        if (type.Assembly.FullName.Contains("System.Private.CoreLib") || type.Assembly.FullName.Contains("mscorlib"))
        {
            return type.FullName;
        }
        else
        {
            return type.AssemblyQualifiedName ?? type.FullName ?? type.Name;
        }
    }

    /// <summary>
    /// Resolves a single type from its full type name.
    /// </summary>
    /// <param name="typeName">The type name to resolve.</param>
    /// <returns>The resolved type.</returns>
    public static Type ResolveType(string typeName)
    {
        if (string.IsNullOrEmpty(typeName))
            throw new ArgumentNullException(nameof(typeName));

        var type = Type.GetType(typeName);
        if (type == null)
            throw new TypeLoadException($"Could not load type: {typeName}");

        return type;
    }
}