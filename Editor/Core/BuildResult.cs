using System.Collections.Generic;

namespace Azathrix.PackFlow
{
    /// <summary>
    /// 构建结果
    /// </summary>
    public class BuildResult
    {
        /// <summary>
        /// 是否成功
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// 错误信息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 失败的步骤名称
        /// </summary>
        public string FailedStep { get; set; }

        /// <summary>
        /// 执行的步骤列表
        /// </summary>
        public List<StepResult> StepResults { get; } = new();

        /// <summary>
        /// 总耗时（毫秒）
        /// </summary>
        public long TotalTimeMs { get; set; }

        /// <summary>
        /// 输出路径
        /// </summary>
        public string OutputPath { get; set; }

        public static BuildResult Succeed()
        {
            return new BuildResult { Success = true };
        }

        public static BuildResult Failed(string stepName, string error)
        {
            return new BuildResult
            {
                Success = false,
                FailedStep = stepName,
                ErrorMessage = error
            };
        }
    }

    /// <summary>
    /// 单个步骤的执行结果
    /// </summary>
    public class StepResult
    {
        public string StepName { get; set; }
        public bool Success { get; set; }
        public long TimeMs { get; set; }
        public string ErrorMessage { get; set; }
    }
}
