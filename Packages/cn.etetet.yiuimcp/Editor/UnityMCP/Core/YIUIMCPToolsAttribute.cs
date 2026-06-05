using System;

namespace YIUIFramework.Editor.MCP
{
    [AttributeUsage(AttributeTargets.Class)]
    public class YIUIMCPToolsAttribute : Attribute
    {
        public string Name;

        public string Description;

        public YIUIMCPToolsAttribute(string name, string description = "")
        {
            Name = name;
            Description = description;
        }
    }
}