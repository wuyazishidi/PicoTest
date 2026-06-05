using System;

namespace YIUIFramework.Editor.MCP
{
    [AttributeUsage(AttributeTargets.Class)]
    public class YIUIMCPFlowAttribute : Attribute
    {
        public string Name;

        public string Description;

        public YIUIMCPFlowAttribute(string name, string description = "")
        {
            Name = name;
            Description = description;
        }
    }
}