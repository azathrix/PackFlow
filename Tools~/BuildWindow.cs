using UnityEditor;
using UnityEngine;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using HybridCLR.Editor.Commands;
using YooAsset;
using YooAsset.Editor;

namespace Editor
{
    public class BuildWindow : EditorWindow
    {
        private static RuntimeConfig Config => RuntimeConfigInitializer.GetConfig();

        private bool _buildMainPackage = true;
        private bool _buildPicturePackage = true;
        private bool _buildHotUpdatePackage = true;
        private bool _buildGameCorePackage = true;
        private bool _buildGameChannelPackage = true;

        private enum DllBuildMode { Auto, ForceAll, None }
        private DllBuildMode _dllBuildMode = DllBuildMode.Auto;
        private readonly string[] _dllBuildModeNames = { "自动编译", "强制编译所有", "不编译" };

        [System.Flags]
        private enum MissingFiles
        {
            None = 0,
            HotUpdateDlls = 1,
            AOTDlls = 2,
            AOTGenericRef = 4,
            LinkXml = 8
        }
        private MissingFiles _missingFiles = MissingFiles.None;

        private bool _useRemoteServer = true;

        private const string PrefKeyMain = "BuildWindow_MainPackage";
        private const string PrefKeyPicture = "BuildWindow_PicturePackage";
        private const string PrefKeyHotUpdate = "BuildWindow_HotUpdatePackage";
        private const string PrefKeyGameCore = "BuildWindow_GameCorePackage";
        private const string PrefKeyGameChannel = "BuildWindow_GameChannelPackage";
        private const string PrefKeyDllMode = "BuildWindow_DllBuildMode";
        private const string PrefKeyRemoteServer = "BuildWindow_UseRemoteServer";

        private static Process _uploadProcess;
        private static bool _isUploading;
        private static Process _gradleProcess;
        private static bool _isExportBuilding;
        private static string _exportPath;
        private static bool _exportBuildAAB;

        [MenuItem("Tools/打包窗口", priority = 99)]
        public static void ShowWindow()
        {
            var window = GetWindow<BuildWindow>("打包工具");
            window.minSize = new Vector2(300, 380);
        }

        private void OnEnable()
        {
            _buildMainPackage = EditorPrefs.GetBool(PrefKeyMain, true);
            _buildPicturePackage = EditorPrefs.GetBool(PrefKeyPicture, true);
            _buildHotUpdatePackage = EditorPrefs.GetBool(PrefKeyHotUpdate, true);
            _buildGameCorePackage = EditorPrefs.GetBool(PrefKeyGameCore, true);
            _buildGameChannelPackage = EditorPrefs.GetBool(PrefKeyGameChannel, true);
            _dllBuildMode = (DllBuildMode)EditorPrefs.GetInt(PrefKeyDllMode, 0);
            _useRemoteServer = EditorPrefs.GetBool(PrefKeyRemoteServer, true);
        }

        private void SavePrefs()
        {
            EditorPrefs.SetBool(PrefKeyMain, _buildMainPackage);
            EditorPrefs.SetBool(PrefKeyPicture, _buildPicturePackage);
            EditorPrefs.SetBool(PrefKeyHotUpdate, _buildHotUpdatePackage);
            EditorPrefs.SetBool(PrefKeyGameCore, _buildGameCorePackage);
            EditorPrefs.SetBool(PrefKeyGameChannel, _buildGameChannelPackage);
            EditorPrefs.SetInt(PrefKeyDllMode, (int)_dllBuildMode);
            EditorPrefs.SetBool(PrefKeyRemoteServer, _useRemoteServer);
        }

        private void OnGUI()
        {
            EditorGUI.BeginChangeCheck();

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("资源包选项", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _buildMainPackage = EditorGUILayout.ToggleLeft($"{Config.mainPackageName} (主资源)", _buildMainPackage);
            _buildPicturePackage = EditorGUILayout.ToggleLeft($"{Config.picturePackageName} (图片)", _buildPicturePackage);
            _buildHotUpdatePackage = EditorGUILayout.ToggleLeft($"{Config.hotUpdatePackageName} (程序集)", _buildHotUpdatePackage);
            _buildGameCorePackage = EditorGUILayout.ToggleLeft($"{Config.gameCorePackageName} (游戏核心)", _buildGameCorePackage);
            _buildGameChannelPackage = EditorGUILayout.ToggleLeft($"{Config.gameChannelPackageName} (游戏渠道)", _buildGameChannelPackage);
            EditorGUILayout.EndVertical();

            if (_buildHotUpdatePackage)
            {
                EditorGUILayout.Space(5);
                EditorGUILayout.LabelField("DLL编译选项", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical("box");
                _dllBuildMode = (DllBuildMode)EditorGUILayout.Popup("编译模式", (int)_dllBuildMode, _dllBuildModeNames);

                if (_dllBuildMode == DllBuildMode.Auto)
                {
                    _missingFiles = CheckMissingFiles();
                    if (_missingFiles == MissingFiles.None)
                    {
                        EditorGUILayout.HelpBox("✓ 所有文件已存在，只编译热更DLL", MessageType.Info);
                    }
                    else
                    {
                        string missing = GetMissingFilesDescription();
                        bool needFullBuild = NeedFullBuild(_missingFiles);
                        string action = needFullBuild ? "将执行完整编译" : "将编译缺失部分";
                        EditorGUILayout.HelpBox($"缺失: {missing}\n{action}", MessageType.Warning);
                    }
                }
                else if (_dllBuildMode == DllBuildMode.ForceAll)
                {
                    EditorGUILayout.HelpBox("强制重新生成所有DLL（包括AOT）", MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox("不编译DLL，直接使用现有文件", MessageType.Info);
                }
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("服务器配置", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            _useRemoteServer = EditorGUILayout.ToggleLeft("使用远程服务器", _useRemoteServer);
            var server = _useRemoteServer ? Config.remoteServer : Config.localServer;

            // 显示项目ID
            var settings = GameSwitcher.GameSwitcherSettings.Load();
            string projectId = GenerateProjectId(settings.currentCore, settings.currentChannel);
            EditorGUILayout.LabelField($"  项目ID: {projectId} ({settings.currentCore}_{settings.currentChannel})", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"  上传: {server.upLoadpoint}/{server.bucket}", EditorStyles.miniLabel);
            EditorGUILayout.LabelField($"  下载: {server.DownLoadpoint}/{server.bucket}", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();

            if (EditorGUI.EndChangeCheck())
                SavePrefs();

            EditorGUILayout.Space(20);

            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.4f, 0.6f, 1f);
            GUI.enabled = !_isUploading;
            if (GUILayout.Button(_isUploading ? "上传中..." : "打包并上传", GUILayout.Height(35)))
            {
                DoBuild(true);
            }
            GUI.enabled = true;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isUploading;
            if (GUILayout.Button("仅打包", GUILayout.Height(30)))
            {
                DoBuild(false);
            }
            if (GUILayout.Button(_isUploading ? "上传中..." : "仅上传", GUILayout.Height(30)))
            {
                DoUpload();
            }
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            // APK/AAB 打包按钮
            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("安装包打包", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            string versionInfo = $"v{PlayerSettings.bundleVersion} | Code: {PlayerSettings.Android.bundleVersionCode}";
            EditorGUILayout.LabelField(versionInfo, EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = new Color(0.5f, 0.8f, 0.5f);
            if (GUILayout.Button("打包 APK", GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () => BuildPlayer(false);
            }
            GUI.backgroundColor = new Color(0.8f, 0.6f, 0.3f);
            if (GUILayout.Button("打包 AAB", GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () => BuildPlayer(true);
            }
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("导出工程打包", EditorStyles.miniLabel);
            EditorGUILayout.BeginHorizontal();
            GUI.enabled = !_isExportBuilding;
            GUI.backgroundColor = new Color(0.6f, 0.5f, 0.8f);
            if (GUILayout.Button(_isExportBuilding ? "打包中..." : "导出打包 APK", GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () => ExportAndBuild(false);
            }
            GUI.backgroundColor = new Color(0.8f, 0.5f, 0.6f);
            if (GUILayout.Button(_isExportBuilding ? "打包中..." : "导出打包 AAB", GUILayout.Height(30)))
            {
                EditorApplication.delayCall += () => ExportAndBuild(true);
            }
            GUI.enabled = true;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
        }

        private void ExportAndBuild(bool buildAAB)
        {
            if (_isExportBuilding)
            {
                UnityEngine.Debug.LogWarning("导出打包正在进行中...");
                return;
            }

            string projectPath = Application.dataPath.Replace("/Assets", "");
            string exportPath = Path.Combine(projectPath, "Build", "AndroidExport");

            try
            {
                // 转换Excel配置
                ExcelToConfig.Convert();

                // 合并manifest
                GameSwitcher.ManifestMerger.MergeManifest(GameSwitcher.GameSwitcherSettings.Load());

                // 动态应用项目配置
                ApplyProjectConfig();

                // 导出Android工程
                UnityEngine.Debug.Log("========== 导出Android工程 ==========");
                if (Directory.Exists(exportPath))
                    Directory.Delete(exportPath, true);
                Directory.CreateDirectory(exportPath);

                EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
                EditorUserBuildSettings.buildAppBundle = false;

                var buildOptions = new BuildPlayerOptions
                {
                    scenes = GetEnabledScenes(),
                    target = BuildTarget.Android,
                    locationPathName = exportPath,
                    options = BuildOptions.AcceptExternalModificationsToPlayer
                };

                var report = BuildPipeline.BuildPlayer(buildOptions);
                if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    throw new System.Exception($"导出失败: {report.summary.totalErrors} 个错误");
                }

                // 更新Gradle版本
                UpdateGradleWrapper(exportPath);

                // 异步启动Gradle打包
                UnityEngine.Debug.Log("========== 开始Gradle打包（异步） ==========");
                _exportPath = exportPath;
                _exportBuildAAB = buildAAB;
                StartGradleBuildAsync(exportPath, buildAAB);
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"导出打包失败: {e.Message}\n{e.StackTrace}");
                EditorUtility.DisplayDialog("错误", $"导出打包失败: {e.Message}", "确定");

                if (Directory.Exists(exportPath))
                    Directory.Delete(exportPath, true);
            }
        }

        private void StartGradleBuildAsync(string exportPath, bool buildAAB)
        {
            string task = buildAAB ? "bundleRelease" : "assembleRelease";

            // 优先使用导出工程的gradlew
            string gradlewPath = Path.Combine(exportPath, "gradlew.bat");
            string gradleCmd = File.Exists(gradlewPath) ? $"\"{gradlewPath}\"" : FindGradle();

            if (string.IsNullOrEmpty(gradleCmd))
            {
                UnityEngine.Debug.LogError("未找到Gradle，请设置GRADLE_HOME环境变量");
                return;
            }

            UnityEngine.Debug.Log($"使用Gradle: {gradleCmd}");

            string cmdArgs = $"/C \"{gradleCmd} {task} && (echo. & echo ===打包完成=== & pause) || (echo. & echo ===打包失败=== & pause & exit 1)\"";
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                WorkingDirectory = exportPath,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            _isExportBuilding = true;
            _gradleProcess = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _gradleProcess.Exited += OnGradleProcessExited;
            _gradleProcess.Start();

            UnityEngine.Debug.Log("Gradle打包已在新窗口中启动，请查看cmd窗口获取进度...");
        }

        private static void UpdateGradleWrapper(string exportPath)
        {
            string wrapperPath = Path.Combine(exportPath, "gradle", "wrapper", "gradle-wrapper.properties");
            if (!File.Exists(wrapperPath)) return;

            string content = File.ReadAllText(wrapperPath);
            content = System.Text.RegularExpressions.Regex.Replace(
                content,
                @"distributionUrl=.*",
                "distributionUrl=https\\://services.gradle.org/distributions/gradle-8.11.1-all.zip"
            );
            File.WriteAllText(wrapperPath, content);
            UnityEngine.Debug.Log("已更新Gradle版本到8.11.1");
        }

        private void OnGradleProcessExited(object sender, System.EventArgs e)
        {
            int exitCode = _gradleProcess?.ExitCode ?? -1;

            EditorApplication.delayCall += () =>
            {
                _isExportBuilding = false;
                string typeName = _exportBuildAAB ? "AAB" : "APK";

                if (exitCode == 0)
                {
                    string outputFile = CopyGradleOutput(_exportPath, _exportBuildAAB);

                    // 清理导出工程
                    UnityEngine.Debug.Log("========== 清理导出工程 ==========");
                    if (Directory.Exists(_exportPath))
                        Directory.Delete(_exportPath, true);

                    if (!string.IsNullOrEmpty(outputFile) && File.Exists(outputFile))
                    {
                        UnityEngine.Debug.Log($"========== {typeName}打包成功! ==========");
                        EditorUtility.DisplayDialog("完成", $"{typeName}打包成功!\n{Path.GetFileName(outputFile)}", "确定");
                        EditorUtility.RevealInFinder(outputFile);
                    }
                }
                else
                {
                    UnityEngine.Debug.LogError($"{typeName}打包失败，退出码: {exitCode}");
                    EditorUtility.DisplayDialog("错误", $"{typeName}打包失败，退出码: {exitCode}", "确定");

                    // 清理
                    if (Directory.Exists(_exportPath))
                        Directory.Delete(_exportPath, true);
                }

                _gradleProcess?.Dispose();
                _gradleProcess = null;
            };
        }

        private string CopyGradleOutput(string exportPath, bool isAAB)
        {
            string projectPath = Application.dataPath.Replace("/Assets", "");
            string outputDir = Path.Combine(projectPath, "Builds", "Android");
            if (!Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            string productName = SanitizeFileName(PlayerSettings.productName);
            string version = PlayerSettings.bundleVersion;
            int versionCode = PlayerSettings.Android.bundleVersionCode;
            string timestamp = System.DateTime.Now.ToString("yyyyMMdd_HHmm");

            string sourceFile, destFile;
            if (isAAB)
            {
                sourceFile = Path.Combine(exportPath, "launcher/build/outputs/bundle/release/launcher-release.aab");
                destFile = Path.Combine(outputDir, $"{productName}_v{version}_{versionCode}_{timestamp}.aab");
            }
            else
            {
                sourceFile = Path.Combine(exportPath, "launcher/build/outputs/apk/release/launcher-release.apk");
                destFile = Path.Combine(outputDir, $"{productName}_v{version}_{versionCode}_{timestamp}.apk");
            }

            if (File.Exists(sourceFile))
            {
                File.Copy(sourceFile, destFile, true);
                UnityEngine.Debug.Log($"输出文件: {destFile}");
                return destFile;
            }

            UnityEngine.Debug.LogWarning($"未找到输出文件: {sourceFile}");
            return null;
        }

        private static string FindGradle()
        {
            string[] possiblePaths = {
                @"D:\SDK\Gradle\gradle-8.11.1\bin\gradle.bat",
                @"C:\Program Files\Android\Android Studio\gradle\gradle-8.11.1\bin\gradle.bat",
            };

            foreach (string path in possiblePaths)
            {
                if (File.Exists(path))
                    return path;
            }

            string gradleHome = System.Environment.GetEnvironmentVariable("GRADLE_HOME");
            if (!string.IsNullOrEmpty(gradleHome))
            {
                string gradleBat = Path.Combine(gradleHome, "bin", "gradle.bat");
                if (File.Exists(gradleBat))
                    return gradleBat;
            }

            return null;
        }

        private void DoBuild(bool upload)
        {
            UnityEditorInternal.AssemblyDefinitionAsset[] originalHybridCLRAsmdefs = null;

            try
            {
                // 转换Excel配置
                ExcelToConfig.Convert();

                // 合并manifest
                GameSwitcher.ManifestMerger.MergeManifest(GameSwitcher.GameSwitcherSettings.Load());

                // 应用临时YooAsset配置（替换引用）
                GameSwitcher.YooAssetMerger.ApplyTempSetting();

                // 应用HybridCLR配置并保存原值
                originalHybridCLRAsmdefs = ApplyHybridCLRConfig();

                // 如果勾选了程序集包，根据模式编译DLL
                if (_buildHotUpdatePackage && _dllBuildMode != DllBuildMode.None)
                {
                    BuildDlls();
                    CopyDllHelper.CopyDllToProject();
                    UnityEngine.Debug.Log("DLL处理完成");
                }

                UnityEngine.Debug.Log("========== 打包YooAsset资源 ==========");
                BuildSelectedPackages();

                if (upload)
                {
                    UnityEngine.Debug.Log("========== 执行上传 ==========");
                    RunUploadAsync();
                }
                else
                {
                    UnityEngine.Debug.Log("========== 打包完成! ==========");
                    EditorUtility.DisplayDialog("完成", "打包完成!", "确定");
                }
            }
            catch (System.Exception e)
            {
                UnityEngine.Debug.LogError($"流程出错: {e.Message}\n{e.StackTrace}");
                EditorUtility.DisplayDialog("错误", $"失败: {e.Message}", "确定");
            }
            finally
            {
                // 恢复YooAsset配置
                GameSwitcher.YooAssetMerger.RestoreOriginalSetting();

                // 恢复HybridCLR配置
                RestoreHybridCLRConfig(originalHybridCLRAsmdefs);
            }
        }

        private void DoUpload()
        {
            if (_isUploading)
            {
                UnityEngine.Debug.LogWarning("上传正在进行中...");
                return;
            }

            UnityEngine.Debug.Log("========== 执行上传 ==========");
            RunUploadAsync();
        }

        private void BuildDlls()
        {
            if (_dllBuildMode == DllBuildMode.ForceAll)
            {
                UnityEngine.Debug.Log("========== 强制编译所有DLL ==========");
                PrebuildCommand.GenerateAll();
                return;
            }

            // Auto模式
            var missing = CheckMissingFiles();
            if (missing == MissingFiles.None)
            {
                UnityEngine.Debug.Log("========== 所有文件已存在，只编译热更DLL ==========");
                CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget);
            }
            else if (NeedFullBuild(missing))
            {
                UnityEngine.Debug.Log($"========== 缺失关键文件，执行完整编译 ==========");
                PrebuildCommand.GenerateAll();
            }
            else
            {
                // 只缺少热更DLL，只编译热更DLL
                UnityEngine.Debug.Log("========== 只编译热更DLL ==========");
                CompileDllCommand.CompileDll(EditorUserBuildSettings.activeBuildTarget);
            }
        }

        private MissingFiles CheckMissingFiles()
        {
            MissingFiles missing = MissingFiles.None;
            string platform = EditorUserBuildSettings.activeBuildTarget.ToString();
            string projectPath = Application.dataPath.Replace("/Assets", "");

            // 检查热更DLLs
            string hotUpdatePath = Path.Combine(projectPath, "HybridCLRData", "HotUpdateDlls", platform);
            if (!Directory.Exists(hotUpdatePath) || Directory.GetFiles(hotUpdatePath, "*.dll").Length == 0)
                missing |= MissingFiles.HotUpdateDlls;

            // 检查AOT DLLs
            string aotPath = Path.Combine(projectPath, "HybridCLRData", "AssembliesPostIl2CppStrip", platform);
            if (!Directory.Exists(aotPath) || Directory.GetFiles(aotPath, "*.dll").Length == 0)
                missing |= MissingFiles.AOTDlls;

            // 检查AOTGenericReferences.cs
            string aotRefPath = Path.Combine(Application.dataPath, "HybridCLRGenerate", "AOTGenericReferences.cs");
            if (!File.Exists(aotRefPath))
                missing |= MissingFiles.AOTGenericRef;

            // 检查link.xml
            string linkXmlPath = Path.Combine(Application.dataPath, "HybridCLRGenerate", "link.xml");
            if (!File.Exists(linkXmlPath))
                missing |= MissingFiles.LinkXml;

            return missing;
        }

        private bool NeedFullBuild(MissingFiles missing)
        {
            // 如果只缺少热更DLL，不需要完整编译
            return (missing & ~MissingFiles.HotUpdateDlls) != MissingFiles.None;
        }

        private string GetMissingFilesDescription()
        {
            var list = new System.Collections.Generic.List<string>();
            if ((_missingFiles & MissingFiles.HotUpdateDlls) != 0) list.Add("热更DLL");
            if ((_missingFiles & MissingFiles.AOTDlls) != 0) list.Add("AOT DLL");
            if ((_missingFiles & MissingFiles.AOTGenericRef) != 0) list.Add("AOTGenericRef");
            if ((_missingFiles & MissingFiles.LinkXml) != 0) list.Add("link.xml");
            return string.Join(", ", list);
        }

        private void BuildSelectedPackages()
        {
            if (_buildMainPackage)
                BuildPackage(Config.mainPackageName, "Builtin");
            if (_buildPicturePackage)
                BuildPackage(Config.picturePackageName, "Builtin");
            if (_buildHotUpdatePackage)
                BuildPackage(Config.hotUpdatePackageName, "");
            if (_buildGameCorePackage)
                BuildPackage(Config.gameCorePackageName, "");
            if (_buildGameChannelPackage)
                BuildPackage(Config.gameChannelPackageName, "");
        }

        private void BuildPackage(string packageName, string builtinCopyParams)
        {
            UnityEngine.Debug.Log($"--- 打包 {packageName} ---");

            string outputRoot = GetBuildOutputRoot();
            string buildinRoot = AssetBundleBuilderHelper.GetStreamingAssetsRoot();
            BuildTarget buildTarget = EditorUserBuildSettings.activeBuildTarget;
            string packageVersion = GetPackageVersion();

            // 只有MainPackage需要shader bundle
            bool needShaderBundle = packageName == Config.mainPackageName;
            string builtinShaderBundleName = "";
            if (needShaderBundle)
            {
                var uniqueBundleName = AssetBundleCollectorSettingData.Setting.UniqueBundleName;
                var packRuleResult = DefaultPackRule.CreateShadersPackRuleResult();
                builtinShaderBundleName = packRuleResult.GetBundleName(packageName, uniqueBundleName);
            }

            var buildParameters = new ScriptableBuildParameters
            {
                BuildOutputRoot = outputRoot,
                BuildinFileRoot = buildinRoot,
                BuildPipeline = EBuildPipeline.ScriptableBuildPipeline.ToString(),
                BuildBundleType = (int)EBuildBundleType.AssetBundle,
                BuildTarget = buildTarget,
                PackageName = packageName,
                PackageVersion = packageVersion,
                EnableSharePackRule = true,
                VerifyBuildingResult = true,
                FileNameStyle = EFileNameStyle.HashName,
                BuildinFileCopyOption = string.IsNullOrEmpty(builtinCopyParams)
                    ? EBuildinFileCopyOption.None
                    : EBuildinFileCopyOption.ClearAndCopyByTags,
                BuildinFileCopyParams = builtinCopyParams,
                CompressOption = ECompressOption.LZ4,
                ClearBuildCacheFiles = false,
                UseAssetDependencyDB = false,
                BuiltinShadersBundleName = builtinShaderBundleName
            };

            var pipeline = new ScriptableBuildPipeline();
            BuildResult buildResult = pipeline.Run(buildParameters, true);

            if (!buildResult.Success)
                throw new System.Exception($"{packageName} 打包失败: {buildResult.ErrorInfo}");

            UnityEngine.Debug.Log($"{packageName} 打包成功");
        }

        private void RunUploadAsync()
        {
            var packages = new System.Collections.Generic.List<string>();
            if (_buildMainPackage) packages.Add(Config.mainPackageName);
            if (_buildPicturePackage) packages.Add(Config.picturePackageName);
            if (_buildHotUpdatePackage) packages.Add(Config.hotUpdatePackageName);
            if (_buildGameCorePackage) packages.Add(Config.gameCorePackageName);
            if (_buildGameChannelPackage) packages.Add(Config.gameChannelPackageName);

            if (packages.Count == 0)
            {
                UnityEngine.Debug.LogWarning("没有选择要上传的包");
                return;
            }

            string projectPath = Application.dataPath.Replace("/Assets", "");
            string parentPath = Directory.GetParent(projectPath)?.FullName;
            string scriptPath = Path.Combine(parentPath, "Tools", "Uploader", "upload.py");

            if (!File.Exists(scriptPath))
            {
                UnityEngine.Debug.LogWarning($"未找到上传脚本: {scriptPath}");
                return;
            }

            var server = _useRemoteServer ? Config.remoteServer : Config.localServer;
            string platform = Config.platform;
            string apiType = server.apiType switch
            {
                UploadApiType.MinIO => "minio",
                UploadApiType.AwsS3 => "s3",
                UploadApiType.TencentCos => "cos",
                _ => "minio"
            };

            // 构建命令行参数
            var argList = new System.Collections.Generic.List<string>();
            argList.Add($"\"{scriptPath}\"");
            argList.AddRange(packages);
            argList.Add($"--api-type {apiType}");
            // 腾讯云COS使用cosRegion，其他使用upLoadpoint
            string endpoint = server.apiType == UploadApiType.TencentCos ? server.cosRegion : server.upLoadpoint;
            argList.Add($"--upload-endpoint \"{endpoint}\"");
            argList.Add($"--download-endpoint \"{server.DownLoadpoint}\"");
            argList.Add($"--bucket \"{server.bucket}\"");
            // 生成项目ID作为路径前缀
            var switcherSettings = GameSwitcher.GameSwitcherSettings.Load();
            string projId = GenerateProjectId(switcherSettings.currentCore, switcherSettings.currentChannel);
            argList.Add($"--project-id \"{projId}\"");
            argList.Add($"--version \"{server.version}\"");
            argList.Add($"--access-key \"{server.accessKey}\"");
            argList.Add($"--secret-key \"{server.secretKey}\"");
            argList.Add($"--platform \"{platform}\"");
            argList.Add($"--max-versions {Config.maxVersionCount}");
            argList.Add($"--bundle-root \"{GetBuildOutputRoot()}\"");

            UnityEngine.Debug.Log($"项目ID: {projId}");
            UnityEngine.Debug.Log($"accessKey: {server.accessKey}");
            UnityEngine.Debug.Log($"secretKey: {server.secretKey}");

            string args = string.Join(" ", argList);
            UnityEngine.Debug.Log($"上传包: {string.Join(", ", packages)} -> {apiType.ToUpper()}");

            string scriptDirectory = Path.GetDirectoryName(scriptPath);

            // 在cmd窗口中显示进度，使用/C确保退出码正确
            // 成功时显示完成并暂停，失败时显示失败并以错误码退出
            string cmdArgs = $"/C \"python {args} && (echo. & echo ===上传完成=== & pause) || (echo. & echo ===上传失败=== & pause & exit 1)\"";
            var startInfo = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = cmdArgs,
                WorkingDirectory = scriptDirectory,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            _isUploading = true;
            _uploadProcess = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _uploadProcess.Exited += OnUploadProcessExited;
            _uploadProcess.Start();

            UnityEngine.Debug.Log("上传已在新窗口中启动，请查看cmd窗口获取进度...");
        }

        private void OnUploadProcessExited(object sender, System.EventArgs e)
        {
            int exitCode = _uploadProcess?.ExitCode ?? -1;

            EditorApplication.delayCall += () =>
            {
                _isUploading = false;

                if (exitCode == 0)
                {
                    UnityEngine.Debug.Log("========== 上传完成! ==========");
                    EditorUtility.DisplayDialog("完成", "上传完成!", "确定");
                }
                else
                {
                    UnityEngine.Debug.LogError($"上传失败，退出码: {exitCode}");
                    EditorUtility.DisplayDialog("错误", $"上传失败，退出码: {exitCode}", "确定");
                }

                _uploadProcess?.Dispose();
                _uploadProcess = null;
            };
        }

        private void BuildPlayer(bool buildAAB)
        {
            // 转换Excel配置
            ExcelToConfig.Convert();

            // 合并manifest
            GameSwitcher.ManifestMerger.MergeManifest(GameSwitcher.GameSwitcherSettings.Load());

            // 保存原始PlayerSettings
            var originalProductName = PlayerSettings.productName;
            var originalBundleId = PlayerSettings.GetApplicationIdentifier(BuildTargetGroup.Android);
            var originalVersionCode = PlayerSettings.Android.bundleVersionCode;
            var originalIcons = PlayerSettings.GetIconsForTargetGroup(BuildTargetGroup.Android);
            var originalDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android);

            try
            {
                // 动态应用项目配置
                ApplyProjectConfig();

                string ext = buildAAB ? "aab" : "apk";
                string typeName = buildAAB ? "AAB" : "APK";

                // 生成文件名: 产品名_版本_bundleCode_时间.apk/aab
                string productName = SanitizeFileName(PlayerSettings.productName);
                string version = PlayerSettings.bundleVersion;
                int bundleCode = PlayerSettings.Android.bundleVersionCode;
                string time = System.DateTime.Now.ToString("yyyyMMdd_HHmm");
                string fileName = $"{productName}_v{version}_{bundleCode}_{time}.{ext}";

                // 输出目录
                string projectPath = Application.dataPath.Replace("/Assets", "");
                string outputDir = Path.Combine(projectPath, "Builds", "Android");
                if (!Directory.Exists(outputDir))
                    Directory.CreateDirectory(outputDir);

                string outputPath = Path.Combine(outputDir, fileName);

                // 设置AAB构建选项
                EditorUserBuildSettings.buildAppBundle = buildAAB;

                var buildOptions = new BuildPlayerOptions
                {
                    scenes = GetEnabledScenes(),
                    locationPathName = outputPath,
                    target = BuildTarget.Android,
                    options = BuildOptions.None
                };

                UnityEngine.Debug.Log($"========== 开始打包{typeName} ==========");
                UnityEngine.Debug.Log($"输出路径: {outputPath}");

                var report = BuildPipeline.BuildPlayer(buildOptions);

                if (report.summary.result == UnityEditor.Build.Reporting.BuildResult.Succeeded)
                {
                    UnityEngine.Debug.Log($"========== {typeName}打包成功! ==========");
                    UnityEngine.Debug.Log($"文件: {fileName}");
                    EditorUtility.DisplayDialog("完成", $"{typeName}打包成功!\n{fileName}", "确定");
                    EditorUtility.RevealInFinder(outputPath);
                }
                else
                {
                    UnityEngine.Debug.LogError($"{typeName}打包失败: {report.summary.totalErrors} 个错误");
                    EditorUtility.DisplayDialog("错误", $"{typeName}打包失败!", "确定");
                }
            }
            finally
            {
                // 恢复原始PlayerSettings
                PlayerSettings.productName = originalProductName;
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, originalBundleId);
                PlayerSettings.Android.bundleVersionCode = originalVersionCode;
                if (originalIcons != null && originalIcons.Length > 0)
                    PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, originalIcons);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, originalDefines);
                UnityEngine.Debug.Log("[BuildWindow] 已恢复PlayerSettings");
            }
        }

        private string[] GetEnabledScenes()
        {
            var scenes = new System.Collections.Generic.List<string>();
            foreach (var scene in EditorBuildSettings.scenes)
            {
                if (scene.enabled)
                    scenes.Add(scene.path);
            }
            return scenes.ToArray();
        }

        private static string GetPackageVersion()
        {
            int totalMinutes = System.DateTime.Now.Hour * 60 + System.DateTime.Now.Minute;
            return System.DateTime.Now.ToString("yyyy-MM-dd") + "-" + totalMinutes;
        }

        /// <summary>
        /// 获取打包输出根目录，格式: Bundles/{Core}_{Channel}
        /// </summary>
        private static string GetBuildOutputRoot()
        {
            var settings = GameSwitcher.GameSwitcherSettings.Load();
            string projectPath = Application.dataPath.Replace("/Assets", "");

            if (settings != null && settings.IsValid())
            {
                var path = Path.Combine(projectPath, "Bundles", $"{settings.currentCore}_{settings.currentChannel}");
                UnityEngine.Debug.Log($"[BuildWindow] 输出目录: {path}");
                return path;
            }

            // 未配置时使用默认路径
            return Path.Combine(projectPath, "Bundles");
        }

        private static string SanitizeFileName(string name)
        {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        /// <summary>
        /// 动态应用HybridCLR配置，返回原始值用于恢复
        /// </summary>
        private UnityEditorInternal.AssemblyDefinitionAsset[] ApplyHybridCLRConfig()
        {
            var hybridSettings = HybridCLR.Editor.Settings.HybridCLRSettings.Instance;
            if (hybridSettings == null) return null;

            // 保存原始值
            var original = hybridSettings.hotUpdateAssemblyDefinitions;

            // 按优先级获取配置: Channel > GameCore > Default
            RuntimeConfig config = null;
            config = AssetDatabase.LoadAssetAtPath<RuntimeConfig>("Assets/GameChannel/Runtime/Resources/RuntimeConfigChannel.asset");
            if (config == null)
                config = AssetDatabase.LoadAssetAtPath<RuntimeConfig>("Assets/GameCore/Runtime/Resources/RuntimeConfigGameCore.asset");
            if (config == null)
                config = AssetDatabase.LoadAssetAtPath<RuntimeConfig>("Assets/Resources/RuntimeConfig.asset");

            if (config == null) return original;

            // 根据dll名称查找对应的asmdef
            var asmdefList = new System.Collections.Generic.List<UnityEditorInternal.AssemblyDefinitionAsset>();
            foreach (var dllName in config.hotUpdateDlls)
            {
                var assemblyName = dllName.Replace(".dll", "");
                var guids = AssetDatabase.FindAssets($"t:AssemblyDefinitionAsset {assemblyName}");

                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var asmdef = AssetDatabase.LoadAssetAtPath<UnityEditorInternal.AssemblyDefinitionAsset>(path);
                    if (asmdef != null && asmdef.name == assemblyName)
                    {
                        asmdefList.Add(asmdef);
                        break;
                    }
                }
            }

            hybridSettings.hotUpdateAssemblyDefinitions = asmdefList.ToArray();
            UnityEngine.Debug.Log($"[BuildWindow] 已动态应用HybridCLR程序集引用: {asmdefList.Count}个");
            return original;
        }

        /// <summary>
        /// 恢复HybridCLR配置
        /// </summary>
        private void RestoreHybridCLRConfig(UnityEditorInternal.AssemblyDefinitionAsset[] original)
        {
            if (original == null) return;

            var hybridSettings = HybridCLR.Editor.Settings.HybridCLRSettings.Instance;
            if (hybridSettings == null) return;

            hybridSettings.hotUpdateAssemblyDefinitions = original;
            UnityEngine.Debug.Log("[BuildWindow] 已恢复HybridCLR配置");
        }

        /// <summary>
        /// 动态应用项目配置（不保存到文件）
        /// </summary>
        private void ApplyProjectConfig()
        {
            var settings = GameSwitcher.GameSwitcherSettings.Load();
            if (!settings.IsValid()) return;

            var corePath = settings.GetCurrentCoreAbsolutePath();
            var channelPath = settings.GetCurrentChannelAbsolutePath();

            var config = GameSwitcher.ProjectConfigApplier.LoadMergedConfig(corePath, channelPath);
            if (config == null) return;

            // 应用项目设置（只在内存中修改）
            if (!string.IsNullOrEmpty(config.productName))
                PlayerSettings.productName = config.productName;

            if (!string.IsNullOrEmpty(config.bundleIdentifier))
                PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, config.bundleIdentifier);

            if (config.versionCode > 0)
                PlayerSettings.Android.bundleVersionCode = config.versionCode;

            // 图标 - 使用旧版API设置默认图标
            if (!string.IsNullOrEmpty(config.icon))
            {
                // 尝试从渠道或核心路径加载图标
                var channelIconPath = Path.Combine(channelPath, config.icon);
                var coreIconPath = Path.Combine(corePath, config.icon);

                string iconPath = File.Exists(channelIconPath) ? channelIconPath :
                                  File.Exists(coreIconPath) ? coreIconPath : null;

                if (iconPath != null)
                {
                    // 复制到临时位置并加载
                    var tempPath = "Assets/Editor/GameSwitcher/Temp/build_icon.png";
                    var tempDir = Path.GetDirectoryName(tempPath);
                    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                    File.Copy(iconPath, tempPath, true);
                    AssetDatabase.Refresh();

                    var iconTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(tempPath);
                    if (iconTexture != null)
                    {
                        PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Android, new[] { iconTexture });
                    }
                }
            }

            // 启动图
            if (!string.IsNullOrEmpty(config.splashScreen))
            {
                var channelSplashPath = Path.Combine(channelPath, config.splashScreen);
                var coreSplashPath = Path.Combine(corePath, config.splashScreen);
                string splashPath = File.Exists(channelSplashPath) ? channelSplashPath :
                                    File.Exists(coreSplashPath) ? coreSplashPath : null;

                if (splashPath != null)
                {
                    var tempPath = "Assets/Editor/GameSwitcher/Temp/build_splash.png";
                    var tempDir = Path.GetDirectoryName(tempPath);
                    if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);
                    File.Copy(splashPath, tempPath, true);
                    AssetDatabase.Refresh();

                    var splashSprite = AssetDatabase.LoadAssetAtPath<Sprite>(tempPath);
                    if (splashSprite != null)
                    {
                        PlayerSettings.SplashScreen.background = splashSprite;
                    }
                }
            }

            // 宏定义
            if (config.defines != null && config.defines.Length > 0)
            {
                var currentDefines = PlayerSettings.GetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android);
                var currentList = new System.Collections.Generic.List<string>(currentDefines.Split(';'));
                currentList.RemoveAll(d => d.StartsWith("GAME_") || d.StartsWith("CHANNEL_") || d.StartsWith("CORE_"));
                currentList.AddRange(config.defines);
                var newDefines = string.Join(";", currentList);
                PlayerSettings.SetScriptingDefineSymbolsForGroup(BuildTargetGroup.Android, newDefines);
            }

            UnityEngine.Debug.Log($"[BuildWindow] 已动态应用项目配置: {config.productName}");
        }

        /// <summary>
        /// 根据核心和渠道生成8位项目ID
        /// </summary>
        private static string GenerateProjectId(string core, string channel)
        {
            string input = $"{core}_{channel}";
            using var md5 = System.Security.Cryptography.MD5.Create();
            byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return System.BitConverter.ToString(hash).Replace("-", "").Substring(0, 8).ToLower();
        }
    }
}
