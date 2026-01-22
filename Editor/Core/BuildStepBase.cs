using Azathrix.PackFlow.Editor.Interfaces;

namespace Azathrix.PackFlow.Editor.Core
{
    /// <summary>
    /// 构建步骤基类
    /// </summary>
    public abstract class BuildStepBase : IBuildStep
    {
        public abstract string Name { get; }
        public virtual int Order => 0;
        public bool Enabled { get; set; } = true;
        public virtual bool HasConfigGUI => false;

        public virtual void DrawConfigGUI() { }

        public abstract bool Execute(PackFlowBuildContext context);
    }
}
