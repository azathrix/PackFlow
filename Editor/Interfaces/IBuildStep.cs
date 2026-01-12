using Editor.Core;

namespace Editor.Interfaces
{
    /// <summary>
    /// 构建步骤接口
    /// </summary>
    public interface IBuildStep
    {
        /// <summary>
        /// 步骤名称
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 执行顺序（数值越小越先执行）
        /// </summary>
        int Order { get; }

        /// <summary>
        /// 是否启用
        /// </summary>
        bool Enabled { get; set; }

        /// <summary>
        /// 是否有配置界面
        /// </summary>
        bool HasConfigGUI { get; }

        /// <summary>
        /// 绘制步骤配置界面
        /// </summary>
        void DrawConfigGUI();

        /// <summary>
        /// 执行构建步骤
        /// </summary>
        /// <param name="context">构建上下文</param>
        /// <returns>是否成功</returns>
        bool Execute(BuildContext context);
    }
}
