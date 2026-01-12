using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Azathrix.PackFlow
{
    /// <summary>
    /// 管道注册器 - 用于注册构建管道
    /// </summary>
    public static class PipelineRegistry
    {
        private static readonly List<IBuildPipeline> _pipelines = new();

        public static IReadOnlyList<IBuildPipeline> Pipelines => _pipelines;
        public static bool HasPipeline => _pipelines.Count > 0;

        public static void Register(IBuildPipeline pipeline)
        {
            if (_pipelines.All(p => p.Name != pipeline.Name))
                _pipelines.Add(pipeline);
        }

        public static void Unregister(string name)
        {
            _pipelines.RemoveAll(p => p.Name == name);
        }

        public static IBuildPipeline Get(string name)
        {
            return _pipelines.FirstOrDefault(p => p.Name == name);
        }

        public static IEnumerable<IBuildPipeline> GetEnabled()
        {
            return _pipelines.Where(p => p.Enabled);
        }
    }

    /// <summary>
    /// 统一构建窗口
    /// </summary>
    public class BuildWindow : EditorWindow
    {
        private enum Tab { Asset, App }

        private Tab _currentTab;
        private Vector2 _scrollPos;

        // 资源构建
        private BuildContext _lastAssetContext;
        private bool _isAssetBuilding;

        // 管道和步骤的折叠状态
        private readonly Dictionary<string, bool> _pipelineFoldouts = new();
        private readonly Dictionary<string, bool> _stepFoldouts = new();

        // 应用构建
        private AppBuildSettings _appSettings;
        private bool _isAppBuilding;
        private bool _showVersionSettings = true;
        private bool _showAndroidSettings;
        private bool _showWindowsSettings;
        private bool _showBuildSettings;

        [MenuItem("Azathrix/PackFlow/构建窗口", priority = 1000)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildWindow>("PackFlow 构建");
            window.minSize = new Vector2(450, 500);
        }

        private void OnEnable()
        {
            _appSettings = AppBuildSettings.instance;
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);

            // 标签页
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Toggle(_currentTab == Tab.Asset, "资源构建", "Button", GUILayout.Height(25)))
                _currentTab = Tab.Asset;
            if (GUILayout.Toggle(_currentTab == Tab.App, "应用构建", "Button", GUILayout.Height(25)))
                _currentTab = Tab.App;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            _scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

            switch (_currentTab)
            {
                case Tab.Asset:
                    DrawAssetBuildTab();
                    break;
                case Tab.App:
                    DrawAppBuildTab();
                    break;
            }

            EditorGUILayout.EndScrollView();
        }

        #region 资源构建

        private void DrawAssetBuildTab()
        {
            if (!PipelineRegistry.HasPipeline)
            {
                EditorGUILayout.HelpBox("没有注册的资源构建管道。", MessageType.Info);
                return;
            }

            // 平铺显示所有管道
            foreach (var pipeline in PipelineRegistry.Pipelines)
            {
                DrawPipelinePanel(pipeline);
                EditorGUILayout.Space(5);
            }

            EditorGUILayout.Space(10);

            // 构建按钮
            DrawAssetBuildButtons();

            // 日志
            if (_lastAssetContext != null && _lastAssetContext.Logs.Count > 0)
            {
                EditorGUILayout.Space(10);
                DrawAssetLogs();
            }
        }

        private void DrawPipelinePanel(IBuildPipeline pipeline)
        {
            var pipelineKey = pipeline.Name;
            if (!_pipelineFoldouts.ContainsKey(pipelineKey))
                _pipelineFoldouts[pipelineKey] = true;

            // 禁用时灰色显示
            var originalColor = GUI.color;
            if (!pipeline.Enabled)
                GUI.color = new Color(0.7f, 0.7f, 0.7f);

            // 管道面板头部
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();

            // 启用勾选框（始终可用）
            GUI.color = originalColor;
            pipeline.Enabled = EditorGUILayout.Toggle(pipeline.Enabled, GUILayout.Width(20));
            if (!pipeline.Enabled)
                GUI.color = new Color(0.7f, 0.7f, 0.7f);

            // 折叠箭头和名称
            _pipelineFoldouts[pipelineKey] = EditorGUILayout.Foldout(
                _pipelineFoldouts[pipelineKey],
                pipeline.Name,
                true,
                EditorStyles.foldoutHeader);

            EditorGUILayout.EndHorizontal();

            // 描述
            if (!string.IsNullOrEmpty(pipeline.Description))
                EditorGUILayout.LabelField(pipeline.Description, EditorStyles.miniLabel);

            // 展开内容
            if (_pipelineFoldouts[pipelineKey])
            {
                EditorGUILayout.Space(5);

                // 管道自定义配置
                GUI.enabled = pipeline.Enabled;
                pipeline.DrawConfigGUI();
                GUI.enabled = true;

                EditorGUILayout.Space(5);

                // 构建步骤列表
                EditorGUILayout.LabelField("构建步骤", EditorStyles.boldLabel);

                var steps = pipeline.Steps.OrderBy(s => s.Order).ToList();
                foreach (var step in steps)
                {
                    DrawStepPanel(pipeline, step);
                }

                if (steps.Count == 0)
                    EditorGUILayout.LabelField("无构建步骤", EditorStyles.miniLabel);
            }

            EditorGUILayout.EndVertical();
            GUI.color = originalColor;
        }

        private void DrawStepPanel(IBuildPipeline pipeline, IBuildStep step)
        {
            var stepKey = $"{pipeline.Name}_{step.Name}";
            if (!_stepFoldouts.ContainsKey(stepKey))
                _stepFoldouts[stepKey] = false;

            // 禁用时灰色显示
            var originalColor = GUI.color;
            if (!step.Enabled)
                GUI.color = new Color(0.7f, 0.7f, 0.7f);

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.BeginHorizontal();

            // 启用勾选框（始终可用）
            GUI.color = originalColor;
            step.Enabled = EditorGUILayout.Toggle(step.Enabled, GUILayout.Width(20));
            if (!step.Enabled)
                GUI.color = new Color(0.7f, 0.7f, 0.7f);

            // 步骤名称和顺序
            var stepLabel = $"[{step.Order}] {step.Name}";

            if (step.HasConfigGUI)
            {
                _stepFoldouts[stepKey] = EditorGUILayout.Foldout(_stepFoldouts[stepKey], stepLabel, true);
            }
            else
            {
                EditorGUILayout.LabelField(stepLabel);
            }

            EditorGUILayout.EndHorizontal();

            // 步骤配置
            if (step.HasConfigGUI && _stepFoldouts[stepKey])
            {
                GUI.enabled = step.Enabled;
                EditorGUI.indentLevel++;
                step.DrawConfigGUI();
                EditorGUI.indentLevel--;
                GUI.enabled = true;
            }

            EditorGUILayout.EndVertical();
            GUI.color = originalColor;
        }

        private void DrawAssetBuildButtons()
        {
            var enabledPipelines = PipelineRegistry.GetEnabled().ToList();
            if (enabledPipelines.Count == 0)
            {
                EditorGUILayout.HelpBox("没有启用的构建管道", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginHorizontal();

            var isBusy = _isAssetBuilding || UploadStep.IsUploading;
            GUI.enabled = !isBusy;

            GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
            if (GUILayout.Button(isBusy ? "处理中..." : "打包并上传", GUILayout.Height(35)))
                DoAssetBuild(true);

            GUI.backgroundColor = Color.white;
            if (GUILayout.Button("仅打包", GUILayout.Height(35)))
                DoAssetBuild(false);

            GUI.backgroundColor = new Color(0.9f, 0.7f, 0.3f);
            if (GUILayout.Button("仅上传", GUILayout.Height(35)))
                DoUploadOnly();

            GUI.backgroundColor = Color.white;
            GUI.enabled = true;

            EditorGUILayout.EndHorizontal();

            // 显示将要执行的管道
            var pipelineNames = string.Join(", ", enabledPipelines.Select(p => p.Name));
            EditorGUILayout.LabelField($"将执行: {pipelineNames}", EditorStyles.miniLabel);
        }

        private void DrawAssetLogs()
        {
            EditorGUILayout.LabelField("构建日志", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box", GUILayout.Height(100));

            foreach (var log in _lastAssetContext.Logs.TakeLast(10))
            {
                var style = log.Contains("ERROR") ? EditorStyles.boldLabel : EditorStyles.miniLabel;
                if (log.Contains("ERROR"))
                    GUI.color = Color.red;
                EditorGUILayout.LabelField(log, style);
                GUI.color = Color.white;
            }

            EditorGUILayout.EndVertical();
        }

        private void DoAssetBuild(bool upload)
        {
            var enabledPipelines = PipelineRegistry.GetEnabled().ToList();
            if (enabledPipelines.Count == 0) return;

            _lastAssetContext = new BuildContext
            {
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                DoUpload = upload,
                UploadConfig = UploadConfigRegistry.instance.Current
            };

            _isAssetBuilding = true;

            try
            {
                // 执行所有启用的管道
                foreach (var pipeline in enabledPipelines)
                {
                    _lastAssetContext.Log($"=== 执行管道: {pipeline.Name} ===");

                    var result = pipeline.Execute(_lastAssetContext);

                    if (!result.Success)
                    {
                        EditorUtility.DisplayDialog("失败",
                            $"管道 [{pipeline.Name}] 构建失败于步骤: {result.FailedStep}\n{result.ErrorMessage}",
                            "确定");
                        return;
                    }

                    _lastAssetContext.Log($"管道 [{pipeline.Name}] 完成，耗时: {result.TotalTimeMs}ms");
                }

                // 如果不上传，显示完成提示；上传的话由 UploadStep 显示
                if (!upload)
                    _lastAssetContext.Log("所有资源构建成功！");
            }
            catch (Exception e)
            {
                _lastAssetContext.LogError($"构建异常: {e.Message}");
                EditorUtility.DisplayDialog("错误", $"构建异常: {e.Message}", "确定");
            }
            finally
            {
                _isAssetBuilding = false;
            }
        }

        private void DoUploadOnly()
        {
            var enabledPipelines = PipelineRegistry.GetEnabled().ToList();
            if (enabledPipelines.Count == 0) return;

            _lastAssetContext = new BuildContext
            {
                BuildTarget = EditorUserBuildSettings.activeBuildTarget,
                DoUpload = true,
                UploadConfig = UploadConfigRegistry.instance.Current
            };

            _isAssetBuilding = true;

            try
            {
                foreach (var pipeline in enabledPipelines)
                {
                    // 从管线获取上传目录
                    var outputDirs = pipeline.GetUploadDirectories(_lastAssetContext);
                    if (outputDirs == null || outputDirs.Count == 0)
                    {
                        _lastAssetContext.Log($"管道 [{pipeline.Name}] 没有需要上传的目录");
                        continue;
                    }

                    var uploadSteps = pipeline.Steps.Where(s => s.Enabled && s.Name.Contains("上传")).ToList();
                    if (uploadSteps.Count == 0)
                    {
                        _lastAssetContext.Log($"管道 [{pipeline.Name}] 没有上传步骤");
                        continue;
                    }

                    _lastAssetContext.Log($"=== 执行管道 [{pipeline.Name}] 上传 ===");
                    _lastAssetContext.Log($"将上传 {outputDirs.Count} 个资源包: {string.Join(", ", outputDirs.Select(d => System.IO.Path.GetFileName(d)))}");

                    foreach (var step in uploadSteps)
                    {
                        _lastAssetContext.Log($"执行步骤: {step.Name}");
                        if (!step.Execute(_lastAssetContext))
                        {
                            // UploadStep 已经显示了对话框
                            return;
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _lastAssetContext.LogError($"上传异常: {e.Message}");
                EditorUtility.DisplayDialog("错误", $"上传异常: {e.Message}", "确定");
            }
            finally
            {
                _isAssetBuilding = false;
            }
        }

        #endregion

        #region 应用构建

        private void DrawAppBuildTab()
        {
            // 版本设置
            _showVersionSettings = EditorGUILayout.Foldout(_showVersionSettings, "版本设置", true);
            if (_showVersionSettings)
            {
                EditorGUILayout.BeginVertical("box");

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("当前版本", _appSettings.FullVersion);
                if (GUILayout.Button("重置", GUILayout.Width(50)))
                {
                    _appSettings.buildNumber = 1;
                    _appSettings.Save();
                }
                EditorGUILayout.EndHorizontal();

                _appSettings.versionFormat = EditorGUILayout.TextField("版本格式", _appSettings.versionFormat);

                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Major", GUILayout.Width(40));
                _appSettings.majorVersion = EditorGUILayout.IntField(_appSettings.majorVersion, GUILayout.Width(50));
                EditorGUILayout.LabelField("Minor", GUILayout.Width(40));
                _appSettings.minorVersion = EditorGUILayout.IntField(_appSettings.minorVersion, GUILayout.Width(50));
                EditorGUILayout.LabelField("Patch", GUILayout.Width(40));
                _appSettings.patchVersion = EditorGUILayout.IntField(_appSettings.patchVersion, GUILayout.Width(50));
                EditorGUILayout.EndHorizontal();

                _appSettings.buildNumber = EditorGUILayout.IntField("Build Number", _appSettings.buildNumber);
                _appSettings.autoIncrementBuild = EditorGUILayout.Toggle("构建后自动递增", _appSettings.autoIncrementBuild);

                if (_appSettings.autoIncrementBuild)
                    _appSettings.autoIncrementType = (VersionIncrementType)EditorGUILayout.EnumPopup("递增类型", _appSettings.autoIncrementType);

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(10);

            // 平台特定设置
            var target = EditorUserBuildSettings.activeBuildTarget;

            if (target == BuildTarget.Android)
            {
                _showAndroidSettings = EditorGUILayout.Foldout(_showAndroidSettings, "Android 设置", true);
                if (_showAndroidSettings)
                {
                    EditorGUILayout.BeginVertical("box");
                    _appSettings.androidBuildType = (AndroidBuildType)EditorGUILayout.EnumPopup("构建类型", _appSettings.androidBuildType);
                    _appSettings.androidExportProject = EditorGUILayout.Toggle("导出工程", _appSettings.androidExportProject);

                    EditorGUILayout.Space(5);
                    EditorGUILayout.LabelField("签名配置", EditorStyles.miniLabel);

                    EditorGUILayout.BeginHorizontal();
                    _appSettings.androidKeystorePath = EditorGUILayout.TextField("Keystore", _appSettings.androidKeystorePath);
                    if (GUILayout.Button("...", GUILayout.Width(30)))
                    {
                        var path = EditorUtility.OpenFilePanel("选择 Keystore", "", "keystore,jks");
                        if (!string.IsNullOrEmpty(path))
                            _appSettings.androidKeystorePath = path;
                    }
                    EditorGUILayout.EndHorizontal();

                    _appSettings.androidKeystorePass = EditorGUILayout.PasswordField("Keystore 密码", _appSettings.androidKeystorePass);
                    _appSettings.androidKeyaliasName = EditorGUILayout.TextField("Key Alias", _appSettings.androidKeyaliasName);
                    _appSettings.androidKeyaliasPass = EditorGUILayout.PasswordField("Key 密码", _appSettings.androidKeyaliasPass);

                    EditorGUILayout.EndVertical();
                }
            }
            else if (target == BuildTarget.StandaloneWindows || target == BuildTarget.StandaloneWindows64)
            {
                _showWindowsSettings = EditorGUILayout.Foldout(_showWindowsSettings, "Windows 设置", true);
                if (_showWindowsSettings)
                {
                    EditorGUILayout.BeginVertical("box");
                    _appSettings.windowsCreateZip = EditorGUILayout.Toggle("构建后打包 ZIP", _appSettings.windowsCreateZip);
                    EditorGUILayout.EndVertical();
                }
            }

            EditorGUILayout.Space(10);

            // 通用构建设置
            _showBuildSettings = EditorGUILayout.Foldout(_showBuildSettings, "构建设置", true);
            if (_showBuildSettings)
            {
                EditorGUILayout.BeginVertical("box");
                _appSettings.developmentBuild = EditorGUILayout.Toggle("Development Build", _appSettings.developmentBuild);
                _appSettings.scriptDebugging = EditorGUILayout.Toggle("Script Debugging", _appSettings.scriptDebugging);
                _appSettings.customDefines = EditorGUILayout.TextField("自定义宏", _appSettings.customDefines);

                if (PipelineRegistry.HasPipeline)
                    _appSettings.buildAssetBeforeApp = EditorGUILayout.Toggle("构建前打包资源", _appSettings.buildAssetBeforeApp);

                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(10);

            // 构建按钮
            DrawAppBuildButtons(target);
        }

        private void DrawAppBuildButtons(BuildTarget target)
        {
            GUI.enabled = !_isAppBuilding;

            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.3f, 0.8f, 0.3f);

            var buttonText = target switch
            {
                BuildTarget.Android => _appSettings.androidExportProject ? "导出 Android 工程" : $"构建 {_appSettings.androidBuildType}",
                BuildTarget.StandaloneWindows or BuildTarget.StandaloneWindows64 => "构建 EXE",
                BuildTarget.StandaloneOSX => "构建 macOS",
                BuildTarget.iOS => "导出 iOS 工程",
                _ => "构建"
            };

            if (GUILayout.Button(_isAppBuilding ? "构建中..." : buttonText, GUILayout.Height(40)))
                DoAppBuild(target);

            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();

            GUI.enabled = true;

            // 显示输出路径
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField($"输出目录: Builds/{target}/", EditorStyles.miniLabel);
        }

        private void DoAppBuild(BuildTarget target)
        {
            _appSettings.Save();
            _isAppBuilding = true;

            try
            {
                // 构建前打包资源
                if (_appSettings.buildAssetBeforeApp && PipelineRegistry.HasPipeline)
                {
                    DoAssetBuild(false);
                    if (_lastAssetContext != null && !_lastAssetContext.Success)
                    {
                        EditorUtility.DisplayDialog("失败", "资源构建失败，已取消应用构建", "确定");
                        return;
                    }
                }

                var exportProject = target == BuildTarget.Android && _appSettings.androidExportProject;
                var result = AppBuilder.Build(target, exportProject);

                if (result.Success)
                {
                    var msg = $"应用构建成功！\n耗时: {result.TotalTimeMs}ms\n输出: {result.OutputPath}";
                    if (EditorUtility.DisplayDialog("完成", msg, "打开目录", "确定"))
                        EditorUtility.RevealInFinder(result.OutputPath);
                }
                else
                {
                    EditorUtility.DisplayDialog("失败", $"构建失败\n{result.ErrorMessage}", "确定");
                }
            }
            catch (Exception e)
            {
                EditorUtility.DisplayDialog("错误", $"构建异常: {e.Message}", "确定");
            }
            finally
            {
                _isAppBuilding = false;
            }
        }

        #endregion
    }
}
