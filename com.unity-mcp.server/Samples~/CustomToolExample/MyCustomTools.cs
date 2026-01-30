using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace CustomToolExample
{
    /// <summary>
    /// Example of creating custom MCP tools for your project.
    /// These tools will be auto-discovered by the MCP server.
    /// </summary>
    public static class MyCustomTools
    {
        /// <summary>
        /// Simple echo tool that returns the message you send.
        /// </summary>
        [UnityMCP.MCPTool("my_echo", "Echo a message back - example custom tool")]
        [UnityMCP.MCPParam("message", "string", "Message to echo back")]
        public static object MyEcho(JObject args)
        {
            var message = args["message"]?.ToString();
            if (string.IsNullOrEmpty(message))
            {
                return new { success = false, error = "message is required" };
            }

            return new
            {
                success = true,
                echo = message,
                timestamp = System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        /// <summary>
        /// Example tool that lists all tags defined in the project.
        /// </summary>
        [UnityMCP.MCPTool("list_tags", "List all tags defined in the Unity project")]
        public static object ListTags(JObject args)
        {
            var tags = UnityEditorInternal.InternalEditorUtility.tags;
            return new
            {
                success = true,
                count = tags.Length,
                tags
            };
        }

        /// <summary>
        /// Example tool that lists all layers defined in the project.
        /// </summary>
        [UnityMCP.MCPTool("list_layers", "List all layers defined in the Unity project")]
        public static object ListLayers(JObject args)
        {
            var layers = new System.Collections.Generic.List<object>();
            for (int i = 0; i < 32; i++)
            {
                var layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    layers.Add(new { index = i, name = layerName });
                }
            }

            return new
            {
                success = true,
                count = layers.Count,
                layers
            };
        }

        /// <summary>
        /// Example tool that gets project settings info.
        /// </summary>
        [UnityMCP.MCPTool("project_info", "Get basic project information")]
        public static object ProjectInfo(JObject args)
        {
            return new
            {
                success = true,
                projectName = Application.productName,
                companyName = Application.companyName,
                version = Application.version,
                unityVersion = Application.unityVersion,
                platform = Application.platform.ToString(),
                dataPath = Application.dataPath,
                isPlaying = Application.isPlaying
            };
        }
    }
}
