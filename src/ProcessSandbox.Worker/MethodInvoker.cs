using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using ProcessSandbox.Abstractions;
using ProcessSandbox.Abstractions.Messages;

namespace ProcessSandbox.Worker;

/// <summary>
/// Invokes methods on target instances via reflection.
/// </summary>
/// <remarks>
/// Creates a new method invoker for the specified target instance.
/// </remarks>
/// <param name="targetInstance"></param>
/// <param name="targetType"></param>
/// <exception cref="ArgumentNullException"></exception>
public class MethodInvoker(object targetInstance, Type targetType)
{
    /// <summary>
    /// Invokes a method based on the invocation message.
    /// </summary>
    /// <param name="invocation">The method invocation details.</param>
    /// <returns>The method result message.</returns>
    public MethodResultMessage InvokeMethod(MethodInvocationMessage invocation)
    {
        try
        {
            // Resolve parameter types
            var parameterTypes = SerializationHelper.ResolveTypes(invocation.ParameterTypeNames);

            // Find the method
            var method = FindMethod(invocation.MethodName, parameterTypes);
            if (method == null)
            {
                var signature = $"{invocation.MethodName}({string.Join(", ", invocation.ParameterTypeNames)})";
                throw new MethodNotFoundException(
                    $"Method not found: {targetInstance.GetType().FullName}.{signature}");
            }

            // Deserialize parameters
            var parameters = SerializationHelper.DeserializeParameters(
                invocation.SerializedParameters,
                parameterTypes);

            // Invoke the method
            var result = method.Invoke(targetInstance, parameters);

            // Serialize result
            var serializedResult = SerializationHelper.SerializeReturnValue(result);
            var resultTypeName = method.ReturnType == typeof(void)
                ? null
                : SerializationHelper.GetTypeName(method.ReturnType);

            return MethodResultMessage.CreateSuccess(
                invocation.CorrelationId,
                serializedResult,
                resultTypeName);
        }
        catch (Exception ex)
        {
            // Unwrap TargetInvocationException to get the real exception
            var actualException = ex is TargetInvocationException tie && tie.InnerException != null
                ? tie.InnerException
                : ex;

            return MethodResultMessage.CreateFailure(invocation.CorrelationId, actualException);
        }
    }

    /// <summary>
    /// Finds a method by name and parameter types.
    /// </summary>
    private MethodInfo? FindMethod(string methodName, Type[] parameterTypes)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

        // Try exact match first
        var method = targetType.GetMethod(methodName, flags, null, parameterTypes, null);
        if (method != null)
            return method;

        // Try to find by name and parameter count (handles covariance/contravariance)
        var methods = targetType.GetMethods(flags)
            .Where(m => string.Equals(m.Name, methodName, StringComparison.OrdinalIgnoreCase))
            .Where(m => m.GetParameters().Length == parameterTypes.Length)
            .ToArray();

        if (methods.Length == 0)
            return null;

        if (methods.Length == 1)
            return methods[0];

        // Multiple overloads, try to find best match
        foreach (var candidate in methods)
        {
            var candidateParams = candidate.GetParameters();
            var match = true;

            for (int i = 0; i < parameterTypes.Length; i++)
            {
                if (!candidateParams[i].ParameterType.IsAssignableFrom(parameterTypes[i]))
                {
                    match = false;
                    break;
                }
            }

            if (match)
                return candidate;
        }

        return methods[0];
    }
}

/// <summary>
/// Exception thrown when a method cannot be found.
/// </summary>
public class MethodNotFoundException : ProcessSandboxException
{
    /// <summary>
    /// Creates a new MethodNotFoundException.
    /// </summary>
    /// <param name="message"></param>
    public MethodNotFoundException(string message) : base(message)
    {
    }
}