using System;

namespace UnityMCP
{
    /// <summary>
    /// Marks a static method as an MCP tool.
    /// Method signature must be: static object MethodName(JObject args)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class MCPToolAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }

        /// <summary>
        /// Timeout in milliseconds for this tool. Default is 30000 (30 seconds).
        /// Set to 0 for no timeout (use with caution).
        /// </summary>
        public int TimeoutMs { get; set; } = 30000;

        /// <summary>
        /// If true, this tool only reads data and can be cached/parallelized.
        /// If false, this tool modifies state and must run on main thread.
        /// </summary>
        public bool IsReadOnly { get; set; } = false;

        /// <summary>
        /// Category for organizing tools in the UI. e.g., "Assets", "Scene", "Debug"
        /// </summary>
        public string Category { get; set; } = "General";

        public MCPToolAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }
    }

    /// <summary>
    /// Defines a parameter for an MCP tool.
    /// Apply multiple times for multiple parameters.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public class MCPParamAttribute : Attribute
    {
        public string Name { get; }
        public string Type { get; }
        public string Description { get; }
        public bool Required { get; }

        public MCPParamAttribute(string name, string type, string description, bool required = true)
        {
            Name = name;
            Type = type;
            Description = description;
            Required = required;
        }
    }
}
