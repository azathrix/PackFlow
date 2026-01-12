using System;
using System.IO;
using Azathrix.Framework.Settings;
using UnityEditor;
using UnityEngine;

namespace Editor.Core
{
    /// <summary>
    /// 应用构建器
    /// </summary>
    public static class AppBuilder
    {
        public static BuildResult Build(BuildTarget target, bool exportProject = false)
        {
            var settings = AppBuildSettings.instance;
            var result = new BuildResult();
            var startTime = DateTime.Now;

            try
            {
                // 设置版本号
                PlayerSettings.bundleVersion = settings.Version;
                if (target == BuildTarget.Android)
                    PlayerSettings.Android.bundleVersionCode = AzathrixFrameworkSettings.Instance.buildNumber;

                // 构建选项
                var options = BuildOptions.None;
                if (settings.developmentBuild)
                    options |= BuildOptions.Development;
                if (settings.scriptDebugging)
                    options |= BuildOptions.AllowDebugging;

                // 获取场景
                var scenes = GetEnabledScenes();
                if (scenes.Length == 0)
                {
                    result.Success = false;
                    result.ErrorMessage = "没有启用的场景";
                    return result;
                }

                // 构建路径
                var outputPath = GetOutputPath(target, exportProject);
                var outputDir = Path.GetDirectoryName(outputPath);
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                // Android 特殊处理
                if (target == BuildTarget.Android)
                {
                    if (exportProject)
                        options |= BuildOptions.AcceptExternalModificationsToPlayer;

                    EditorUserBuildSettings.buildAppBundle = settings.androidBuildType == AndroidBuildType.AAB;

                    if (!string.IsNullOrEmpty(settings.androidKeystorePath))
                    {
                        PlayerSettings.Android.keystoreName = settings.androidKeystorePath;
                        PlayerSettings.Android.keystorePass = settings.androidKeystorePass;
                        PlayerSettings.Android.keyaliasName = settings.androidKeyaliasName;
                        PlayerSettings.Android.keyaliasPass = settings.androidKeyaliasPass;
                    }
                }

                // 执行构建
                var report = BuildPipeline.BuildPlayer(scenes, outputPath, target, options);

                if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    result.Success = true;
                    result.OutputPath = outputPath;

                    // 自动版本叠加
                    if (settings.autoIncrementBuild)
                        settings.IncrementVersion();
                }
                else
                {
                    result.Success = false;
                    result.ErrorMessage = $"构建失败: {report.summary.result}";
                    foreach (var step in report.steps)
                    {
                        foreach (var msg in step.messages)
                        {
                            if (msg.type == LogType.Error)
                                result.ErrorMessage += $"\n{msg.content}";
                        }
                    }
                }
            }
            catch (Exception e)
            {
                result.Success = false;
                result.ErrorMessage = e.Message;
            }

            result.TotalTimeMs = (long)(DateTime.Now - startTime).TotalMilliseconds;
            return result;
        }

        private static string[] GetEnabledScenes()
        {
            var scenes = EditorBuildSettings.scenes;
            var enabledScenes = new System.Collections.Generic.List<string>();
            foreach (var scene in scenes)
            {
                if (scene.enabled)
                    enabledScenes.Add(scene.path);
            }
            return enabledScenes.ToArray();
        }

        private static string GetOutputPath(BuildTarget target, bool exportProject)
        {
            var settings = AppBuildSettings.instance;
            var productName = PlayerSettings.productName;
            var version = settings.Version;
            var buildDir = $"Builds/{target}";

            switch (target)
            {
                case BuildTarget.Android:
                    if (exportProject)
                        return $"{buildDir}/{productName}_AndroidProject";
                    var ext = settings.androidBuildType == AndroidBuildType.AAB ? "aab" : "apk";
                    return $"{buildDir}/{productName}_{version}.{ext}";

                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return $"{buildDir}/{productName}_{version}/{productName}.exe";

                case BuildTarget.StandaloneOSX:
                    return $"{buildDir}/{productName}_{version}.app";

                case BuildTarget.iOS:
                    return $"{buildDir}/{productName}_iOS";

                default:
                    return $"{buildDir}/{productName}_{version}";
            }
        }
    }
}
