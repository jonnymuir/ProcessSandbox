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
        /// <summary>
        /// Generates a unique pipe name with the specified prefix.
        /// </summary>
        /// <returns>A unique pipe name.</returns>
        public static string Generate()
        {
            return Guid.NewGuid().ToString("N");
            
        }
    } 
}