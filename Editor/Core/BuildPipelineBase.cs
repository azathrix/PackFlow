using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Azathrix.PackFlow
{
    /// <summary>
    /// 构建管道基类
    /// </summary>
    public abstract class BuildPipelineBase : IBuildPipeline
    {
        private readonly List<IBuildStep> _steps = new();
        private static List<(Type type, PipelineStepAttribute[] attrs)> _stepTypeCache;

        public abstract string Name { get; }
        public virtual string Description => "";
        public bool Enabled { get; set; } = true;

        public IReadOnlyList<IBuildStep> Steps => _steps;

        protected BuildPipelineBase()
        {
            AutoLoadSteps();
        }

        /// <summary>
        /// 自动加载适用于此Pipeline的Step
        /// </summary>
        private void AutoLoadSteps()
        {
            EnsureStepTypesCached();

            foreach (var (type, attrs) in _stepTypeCache)
            {
                // 检查是否有任何属性匹配此Pipeline
                if (!attrs.Any(attr => attr.IsMatch(Name)))
                    continue;

                try
                {
                    IBuildStep step;
                    var ctor = type.GetConstructor(new[] { typeof(IBuildPipeline) });
                    if (ctor != null)
                        step = (IBuildStep)ctor.Invoke(new object[] { this });
                    else
                        step = (IBuildStep)Activator.CreateInstance(type);

                    AddStep(step);
                }
                catch (Exception e)
                {
                    UnityEngine.Debug.LogError($"[PackFlow] 无法创建Step {type.Name}: {e.Message}");
                }
            }
        }

        /// <summary>
        /// 确保Step类型已缓存
        /// </summary>
        private static void EnsureStepTypesCached()
        {
            if (_stepTypeCache != null) return;

            _stepTypeCache = new List<(Type, PipelineStepAttribute[])>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (!typeof(IBuildStep).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                            continue;

                        var attrs = type.GetCustomAttributes<PipelineStepAttribute>().ToArray();
                        if (attrs.Length == 0)
                            continue;

                        _stepTypeCache.Add((type, attrs));
                    }
                }
                catch
                {
                    // 忽略无法加载的程序集
                }
            }
        }

        /// <summary>
        /// 清除Step类型缓存（用于热重载）
        /// </summary>
        public static void ClearStepCache()
        {
            _stepTypeCache = null;
        }

        public void AddStep(IBuildStep step)
        {
            // 避免重复添加同名Step
            if (_steps.Any(s => s.Name == step.Name))
                return;

            _steps.Add(step);
            _steps.Sort((a, b) => a.Order.CompareTo(b.Order));
        }

        public void RemoveStep(string stepName)
        {
            _steps.RemoveAll(s => s.Name == stepName);
        }

        public BuildResult Execute(BuildContext context)
        {
            return BuildPipelineRunner.Run(this, context);
        }

        public virtual void DrawConfigGUI()
        {
            // 子类重写以绘制配置界面
        }

        public virtual IReadOnlyList<string> GetUploadDirectories(BuildContext context)
        {
            // 子类重写以返回需要上传的目录列表
            return new List<string>();
        }

        public virtual UploadConfig GetUploadConfig()
        {
            // 默认使用全局配置
            return UploadConfigRegistry.instance.Current;
        }

        public virtual void DrawUploadConfigGUI()
        {
            // 默认使用全局配置选择器
            var registry = UploadConfigRegistry.instance;
            var names = registry.GetConfigNames();
            registry.SelectedIndex = UnityEditor.EditorGUILayout.Popup("服务器配置", registry.SelectedIndex, names);
        }
    }
}
