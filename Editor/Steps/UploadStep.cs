using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Azathrix.Framework.Settings;
using Azathrix.PackFlow.Editor.Attributes;
using Azathrix.PackFlow.Editor.Core;
using Azathrix.PackFlow.Editor.Interfaces;
using UnityEditor;

namespace Azathrix.PackFlow.Editor.Steps
{
    /// <summary>
    /// 通用上传步骤
    /// </summary>
    [PipelineStep] // 适用于所有Pipeline
    public class UploadStep : IBuildStep
    {
        private readonly IBuildPipeline _pipeline;
        private bool _enabled = true;

        // 静态状态，用于禁用按钮和异步监控
        public static bool IsUploading { get; private set; }
        private static Process _uploadProcess;

        public string Name => "上传资源";
        public int Order => 300;
        public bool Enabled { get => _enabled; set => _enabled = value; }
        public bool HasConfigGUI => true;

        public UploadStep(IBuildPipeline pipeline)
        {
            _pipeline = pipeline;
        }

        public void DrawConfigGUI()
        {
            _pipeline.DrawUploadConfigGUI();
        }

        public bool Execute(PackFlowBuildContext context)
        {
            if (!context.DoUpload)
            {
                context.Log("跳过上传");
                return true;
            }

            if (IsUploading)
            {
                context.LogError("上传正在进行中...");
                return false;
            }

            var outputDirs = _pipeline.GetUploadDirectories(context);
            if (outputDirs == null || outputDirs.Count == 0)
            {
                context.LogError("找不到构建输出目录");
                return false;
            }

            var config = _pipeline.GetUploadConfig();
            if (config == null)
            {
                context.LogError("上传配置为空");
                return false;
            }

            var uploaderPath = GetUploaderPath();
            if (string.IsNullOrEmpty(uploaderPath))
            {
                context.LogError("找不到上传脚本");
                return false;
            }

            context.Log($"目标: {config.apiType} - {config.endpoint}");

            // 收集所有 package 名称
            var packageNames = new List<string>();
            foreach (var dir in outputDirs)
            {
                if (Directory.Exists(dir))
                    packageNames.Add(Path.GetFileName(dir));
            }

            if (packageNames.Count == 0)
            {
                context.LogError("没有有效的资源包目录");
                return false;
            }

            // 获取 bundle 根目录（从第一个目录推断）
            var firstDir = outputDirs[0];
            var platformDir = Path.GetDirectoryName(firstDir);
            var bundleRoot = Path.GetDirectoryName(platformDir);
            var platform = Path.GetFileName(platformDir);

            // 从框架配置获取版本号和项目ID
            var frameworkSettings = AzathrixFrameworkSettings.Instance;
            var version = frameworkSettings?.Version ?? "1.0.0";
            var projectId = frameworkSettings?.projectId ?? "";
            var apiType = config.apiType.ToString().ToLower();

            context.Log($"上传 {packageNames.Count} 个资源包: {string.Join(", ", packageNames)}");
            context.Log($"版本: {version}");

            // 构建 Python 参数
            var scriptDir = Path.GetDirectoryName(uploaderPath);
            var pyArgs = $"{string.Join(" ", packageNames)} " +
                         $"--api-type {apiType} " +
                         $"--upload-endpoint \"{config.endpoint}\" " +
                         $"--bundle-root \"{bundleRoot}\" " +
                         $"--platform \"{platform}\"";

            if (config.apiType != UploadApiType.LocalHttp)
            {
                pyArgs += $" --bucket \"{config.bucket}\"" +
                          $" --access-key \"{config.accessKey}\"" +
                          $" --secret-key \"{config.secretKey}\"";
            }

            if (!string.IsNullOrEmpty(projectId))
                pyArgs += $" --project-id \"{projectId}\"";

            if (!string.IsNullOrEmpty(version))
                pyArgs += $" --version \"{version}\"";

            // cmd 命令 - /C 表示执行完后关闭窗口
            var cmdArgs = $"/C python upload.py {pyArgs}";

            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                WorkingDirectory = scriptDir,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            IsUploading = true;
            context.Log("上传已在新窗口中启动，请查看 cmd 窗口...");

            _uploadProcess = new Process { StartInfo = startInfo };
            _uploadProcess.Start();

            // 注册 update 回调来监控进程
            EditorApplication.update += CheckUploadProcess;

            return true; // 启动成功，异步等待结果
        }

        private static void CheckUploadProcess()
        {
            if (_uploadProcess == null || !IsUploading)
            {
                EditorApplication.update -= CheckUploadProcess;
                return;
            }

            if (_uploadProcess.HasExited)
            {
                EditorApplication.update -= CheckUploadProcess;
                var exitCode = _uploadProcess.ExitCode;
                _uploadProcess = null;
                IsUploading = false;

                if (exitCode == 0)
                    EditorUtility.DisplayDialog("完成", "上传完成!", "确定");
                else
                    EditorUtility.DisplayDialog("错误", $"上传失败，退出码: {exitCode}", "确定");
            }
        }

        private string GetUploaderPath()
        {
            var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(UploadStep).Assembly);
            if (packageInfo != null)
                return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, "Tools~", "Uploader", "upload.py"));
            return null;
        }
    }
}
