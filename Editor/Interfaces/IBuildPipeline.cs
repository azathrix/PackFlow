using System.Collections.Generic;
using Editor.Core;

namespace Editor.Interfaces
{
    /// <summary>
    /// 构建管道接口
    /// </summary>
    public interface IBuildPipeline
    {
        /// <summary>
        /// 管道名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 管道描述
        /// </summary>
        string Description { get; }

        /// <summary>
        /// 是否启用此管道
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>
        /// 获取所有构建步骤
        /// </summary>
        IReadOnlyList<IBuildStep> Steps { get; }

        /// <summary>
        /// 添加构建步骤
        /// </summary>
        void AddStep(IBuildStep step);

        /// <summary>
        /// 移除构建步骤
        /// </summary>
        void RemoveStep(string stepName);

        /// <summary>
        /// 执行构建管道
        /// </summary>
        BuildResult Execute(BuildContext context);

        /// <summary>
        /// 绘制配置界面
        /// </summary>
        void DrawConfigGUI();

        /// <summary>
        /// 获取需要上传的目录列表（用于仅上传功能）
        /// </summary>
        IReadOnlyList<string> GetUploadDirectories(BuildContext context);

        /// <summary>
        /// 获取此管线使用的上传配置
        /// </summary>
        UploadConfig GetUploadConfig();

        /// <summary>
        /// 绘制上传配置选择UI
        /// </summary>
        void DrawUploadConfigGUI();
    }
}
