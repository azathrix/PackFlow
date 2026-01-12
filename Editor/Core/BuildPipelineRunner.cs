using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Azathrix.PackFlow
{
    /// <summary>
    /// 构建管道执行器
    /// </summary>
    public static class BuildPipelineRunner
    {
        /// <summary>
        /// 执行构建管道
        /// </summary>
        public static BuildResult Run(IBuildPipeline pipeline, BuildContext context)
        {
            var result = new BuildResult { Success = true };
            var totalStopwatch = Stopwatch.StartNew();

            context.Log($"========== 开始执行管道: {pipeline.Name} ==========");

            var steps = pipeline.Steps.Where(s => s.Enabled).OrderBy(s => s.Order).ToList();
            context.Log($"共 {steps.Count} 个步骤");

            foreach (var step in steps)
            {
                context.Log($"--- 执行步骤: {step.Name} ---");
                var stepStopwatch = Stopwatch.StartNew();

                try
                {
                    var success = step.Execute(context);
                    stepStopwatch.Stop();

                    var stepResult = new StepResult
                    {
                        StepName = step.Name,
                        Success = success,
                        TimeMs = stepStopwatch.ElapsedMilliseconds
                    };
                    result.StepResults.Add(stepResult);

                    if (!success)
                    {
                        result.Success = false;
                        result.FailedStep = step.Name;
                        result.ErrorMessage = $"步骤 {step.Name} 执行失败";
                        context.LogError(result.ErrorMessage);
                        break;
                    }

                    context.Log($"步骤 {step.Name} 完成，耗时 {stepStopwatch.ElapsedMilliseconds}ms");
                }
                catch (Exception e)
                {
                    stepStopwatch.Stop();
                    result.Success = false;
                    result.FailedStep = step.Name;
                    result.ErrorMessage = e.Message;
                    result.StepResults.Add(new StepResult
                    {
                        StepName = step.Name,
                        Success = false,
                        TimeMs = stepStopwatch.ElapsedMilliseconds,
                        ErrorMessage = e.Message
                    });
                    context.LogError($"步骤 {step.Name} 异常: {e.Message}");
                    break;
                }
            }

            totalStopwatch.Stop();
            result.TotalTimeMs = totalStopwatch.ElapsedMilliseconds;

            if (result.Success)
            {
                context.Log($"========== 管道执行成功，总耗时 {result.TotalTimeMs}ms ==========");
            }
            else
            {
                context.LogError($"========== 管道执行失败于步骤 {result.FailedStep} ==========");
            }

            return result;
        }
    }
}
