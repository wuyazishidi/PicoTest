using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Sirenix.OdinInspector;
using UnityEngine;

namespace YIUIFramework.Editor.MCP
{
    [HideLabel]
    [HideReferenceObjectPicker]
    public partial class YIUIMCPToolsModule
    {
        public enum EYIUIMCPType
        {
            [LabelText("流程工具")]
            Flow,

            [LabelText("单个工具")]
            Tool,
        }

        [BoxGroup("基础信息", centerLabel: true)]
        [HorizontalGroup("基础信息/Counts")]
        [ShowInInspector]
        [DisplayAsString]
        [LabelText("已注册的-流程工具-数量:")]
        public int FlowsCount => YIUIMCPToolsRegistry.FlowCount;

        [BoxGroup("基础信息", centerLabel: true)]
        [HorizontalGroup("基础信息/Counts")]
        [ShowInInspector]
        [DisplayAsString]
        [LabelText("已注册的-单个工具-数量")]
        public int ToolsCount => YIUIMCPToolsRegistry.ToolCount;

        public void Update()
        {
            YIUIMCPToolsRegistry.Initialize();
 
            m_AllUnityTools.Clear();
            foreach (var VARIABLE in YIUIMCPToolsRegistry.Tools.Values)
            {
                m_AllUnityTools.Add(new YIUIMCPUnityToolsData(VARIABLE, false));
            }

            foreach (var VARIABLE in YIUIMCPToolsRegistry.Flows.Values)
            {
                m_AllUnityTools.Add(new YIUIMCPUnityToolsData(VARIABLE, true));
            }

            m_AllUnityTools.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            OnSearchChanged();
        }

        [BoxGroup("所有配置/搜索 (支持正则)")]
        [HideLabel]
        [EnumToggleButtons]
        [PropertyOrder(1002)]
        [OnValueChanged(nameof(OnSearchChanged))]
        public EYIUIMCPType MCPType = EYIUIMCPType.Flow;

        private bool ShowFlow => MCPType == EYIUIMCPType.Flow;

        [BoxGroup("所有配置/搜索 (支持正则)")]
        [HideLabel]
        [OnValueChanged(nameof(OnSearchChanged))]
        [Delayed]
        [ShowInInspector]
        [PropertyOrder(1003)]
        private string Search = "";

        [BoxGroup("所有配置/搜索 (支持正则)")]
        [LabelText("改变执行超时")]
        [PropertyOrder(1004)]
        [OnValueChanged(nameof(OnSearchChanged))]
        public bool ChangeTimeoutMs = false;

        private readonly List<YIUIMCPUnityToolsData> m_AllUnityTools = new();

        [TableList(DrawScrollView = true, IsReadOnly = true, AlwaysExpanded = true)]
        [BoxGroup("所有配置", centerLabel: true)]
        [HideLabel]
        [ShowInInspector]
        [PropertyOrder(int.MaxValue)]
        [HideReferenceObjectPicker]
        private readonly List<YIUIMCPUnityToolsData> m_ShowAllUnityTools = new();

        protected void OnSearchChanged()
        {
            m_ShowAllUnityTools.Clear();
            if (string.IsNullOrEmpty(Search))
            {
                foreach (var item in m_AllUnityTools)
                {
                    if (ShowFlow)
                    {
                        if (item.IsFlow)
                        {
                            m_ShowAllUnityTools.Add(item);
                        }
                    }
                    else
                    {
                        if (!item.IsFlow)
                        {
                            m_ShowAllUnityTools.Add(item);
                        }
                    }
                }

                foreach (var item in m_ShowAllUnityTools)
                {
                    item.ChangeTimeoutMs(ChangeTimeoutMs);
                }

                return;
            }

            try
            {
                var regex = new Regex(Search, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                foreach (var item in m_AllUnityTools)
                {
                    if (item != null && !string.IsNullOrEmpty(item.Name) && regex.IsMatch(item.Name))
                    {
                        if (ShowFlow)
                        {
                            if (item.IsFlow)
                            {
                                m_ShowAllUnityTools.Add(item);
                            }
                        }
                        else
                        {
                            if (!item.IsFlow)
                            {
                                m_ShowAllUnityTools.Add(item);
                            }
                        }
                    }
                    else
                    {
                        if (item != null && !string.IsNullOrEmpty(item.Description) && regex.IsMatch(item.Description))
                        {
                            if (ShowFlow)
                            {
                                if (item.IsFlow)
                                {
                                    m_ShowAllUnityTools.Add(item);
                                }
                            }
                            else
                            {
                                if (!item.IsFlow)
                                {
                                    m_ShowAllUnityTools.Add(item);
                                }
                            }
                        }
                    }
                }
            }
            catch (ArgumentException ex)
            {
                Debug.LogError($"无效的正则表达式: {Search}. 将使用包含匹配。错误: {ex.Message}");
                foreach (var item in m_AllUnityTools)
                {
                    if (item != null && !string.IsNullOrEmpty(item.Name) && item.Name.IndexOf(Search, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        m_ShowAllUnityTools.Add(item);
                    }
                }

                return;
            }

            foreach (var item in m_ShowAllUnityTools)
            {
                item.ChangeTimeoutMs(ChangeTimeoutMs);
            }
        }
    }
}