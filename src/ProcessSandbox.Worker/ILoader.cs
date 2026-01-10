namespace ProcessSandbox.Worker;

/// <summary>
/// Interface for loaders that create instances of target types.
/// </summary>
public interface ILoader
{
    /// <summary>
    /// Creates an instance of the target type.
    /// </summary>
    /// <returns>The created instance.</returns>
    object CreateInstance();

    /// <summary>
    /// Gets the target type.
    /// </summary>
    /// <returns></returns>
    Type GetTargetType();
}   