using System;
using System.Text.RegularExpressions;

namespace Editor.Attributes
{
    /// <summary>
    /// 标记Step适用的Pipeline
    /// 支持正则表达式: [PipelineStep("*")] [PipelineStep("YooAsset|XAsset")]
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class PipelineStepAttribute : Attribute
    {
        /// <summary>
        /// 适用的Pipeline名称模式（支持正则）
        /// </summary>
        public string Pattern { get; }

        /// <summary>
        /// 是否适用于所有Pipeline
        /// </summary>
        public bool ApplyToAll => string.IsNullOrEmpty(Pattern) || Pattern == "*";

        /// <summary>
        /// 标记Step适用于指定Pipeline
        /// </summary>
        /// <param name="pattern">Pipeline名称模式，支持正则表达式。为空、null或"*"表示适用于所有</param>
        public PipelineStepAttribute(string pattern = null)
        {
            Pattern = pattern;
        }

        /// <summary>
        /// 检查是否匹配指定的Pipeline名称
        /// </summary>
        public bool IsMatch(string pipelineName)
        {
            if (ApplyToAll) return true;
            if (string.IsNullOrEmpty(pipelineName)) return false;

            try
            {
                return Regex.IsMatch(pipelineName, $"^({Pattern})$", RegexOptions.IgnoreCase);
            }
            catch
            {
                // 正则解析失败时回退到精确匹配
                return string.Equals(Pattern, pipelineName, StringComparison.OrdinalIgnoreCase);
            }
        }
    }
}
