using System;
using System.Security.Cryptography;
using System.Text;

namespace ProcessSandbox.IPC
{
    /// <summary>
    /// Generates unique names for named pipes.
    /// </summary>
    public static class PipeNameGenerator
    {
        private const string DefaultPrefix = "ProcessSandbox";

        /// <summary>
        /// Generates a unique pipe name with the default prefix.
        /// </summary>
        /// <returns>A unique pipe name.</returns>
        public static string Generate()
        {
            return Generate(DefaultPrefix);
        }

        /// <summary>
        /// Generates a unique pipe name with the specified prefix.
        /// </summary>
        /// <param name="prefix">The prefix for the pipe name.</param>
        /// <returns>A unique pipe name.</returns>
        public static string Generate(string prefix)
        {
            if (string.IsNullOrWhiteSpace(prefix))
                prefix = DefaultPrefix;

            // Use a GUID for uniqueness
            var guid = Guid.NewGuid().ToString("N");
            
            // Sanitize prefix (remove invalid characters)
            var sanitizedPrefix = SanitizePipeName(prefix);

            return $"{sanitizedPrefix}_{guid}";
        }

        /// <summary>
        /// Generates a deterministic pipe name based on a key.
        /// Useful for reconnection scenarios.
        /// </summary>
        /// <param name="key">The key to base the name on.</param>
        /// <param name="prefix">The prefix for the pipe name.</param>
        /// <returns>A deterministic pipe name.</returns>
        public static string GenerateDeterministic(string key, string prefix = DefaultPrefix)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new ArgumentNullException(nameof(key));

            if (string.IsNullOrWhiteSpace(prefix))
                prefix = DefaultPrefix;

            // Create a hash of the key
            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(key));
            var hash = BitConverter.ToString(hashBytes).Replace("-", "").Substring(0, 16);

            var sanitizedPrefix = SanitizePipeName(prefix);
            return $"{sanitizedPrefix}_{hash}";
        }

        /// <summary>
        /// Validates if a pipe name is valid.
        /// </summary>
        /// <param name="pipeName">The pipe name to validate.</param>
        /// <returns>True if valid, false otherwise.</returns>
        public static bool IsValidPipeName(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
                return false;

            // Pipe names can't contain these characters
            var invalidChars = new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' };
            
            foreach (var c in invalidChars)
            {
                if (pipeName.Contains(c))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Sanitizes a pipe name by removing invalid characters.
        /// </summary>
        /// <param name="pipeName">The pipe name to sanitize.</param>
        /// <returns>A sanitized pipe name.</returns>
        private static string SanitizePipeName(string pipeName)
        {
            if (string.IsNullOrWhiteSpace(pipeName))
                return DefaultPrefix;

            var sb = new StringBuilder(pipeName.Length);
            var invalidChars = new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|', ' ' };

            foreach (var c in pipeName)
            {
                if (Array.IndexOf(invalidChars, c) == -1)
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }

            var result = sb.ToString();
            return string.IsNullOrWhiteSpace(result) ? DefaultPrefix : result;
        }
    }
}