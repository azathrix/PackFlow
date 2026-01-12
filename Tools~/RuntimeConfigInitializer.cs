using System.Collections.Generic;
using System.IO;
using System.Linq;
using HybridCLR.Editor.Settings;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Editor
{
    [InitializeOnLoad]
    public static class RuntimeConfigInitializer
    {
        public const string ConfigPath = "Assets/Resources/RuntimeConfig.asset";

        static RuntimeConfigInitializer()
        {
            EditorApplication.delayCall += EnsureConfigExists;
        }

        private static void EnsureConfigExists()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            var config = AssetDatabase.LoadAssetAtPath<RuntimeConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<RuntimeConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
                AssetDatabase.SaveAssets();
                Debug.Log($"RuntimeConfig created at {ConfigPath}");
            }

            // 同步项目ID
            SyncProjectId(config);
        }

        public static RuntimeConfig GetConfig()
        {
            // 按优先级加载: Channel > GameCore > Default
            // 各自目录下的 Resources 会被 Unity 自动识别
            var config = AssetDatabase.LoadAssetAtPath<RuntimeConfig>("Assets/GameChannel/Runtime/Resources/RuntimeConfigChannel.asset")
                      ?? AssetDatabase.LoadAssetAtPath<RuntimeConfig>("Assets/GameCore/Runtime/Resources/RuntimeConfigGameCore.asset")
                      ?? AssetDatabase.LoadAssetAtPath<RuntimeConfig>(ConfigPath);
            return config;
        }

        /// <summary>
        /// 根据核心和渠道生成8位项目ID
        /// </summary>
        public static string GenerateProjectId(string core, string channel)
        {
            string input = $"{core}_{channel}";
            using var md5 = System.Security.Cryptography.MD5.Create();
            byte[] hash = md5.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
            return System.BitConverter.ToString(hash).Replace("-", "").Substring(0, 8).ToLower();
        }

        /// <summary>
        /// 同步项目ID到 Resources/ProjectId.txt
        /// </summary>
        public static void SyncProjectId(RuntimeConfig config)
        {
            var settings = GameSwitcher.GameSwitcherSettings.Load();
            if (settings == null || !settings.IsValid()) return;

            string projectId = GenerateProjectId(settings.currentCore, settings.currentChannel);
            string filePath = "Assets/Resources/ProjectId.txt";

            // 确保 Resources 目录存在
            if (!AssetDatabase.IsValidFolder("Assets/Resources"))
                AssetDatabase.CreateFolder("Assets", "Resources");

            // 读取现有内容
            string existingId = "";
            if (File.Exists(filePath))
                existingId = File.ReadAllText(filePath).Trim();

            // 只在变化时更新
            if (existingId != projectId)
            {
                File.WriteAllText(filePath, projectId);
                AssetDatabase.Refresh();
                Debug.Log($"[RuntimeConfig] 项目ID已更新: {projectId} ({settings.currentCore}_{settings.currentChannel})");
            }
        }

        /// <summary>
        /// 同步RuntimeConfig的hotUpdateDlls到HybridCLR设置
        /// </summary>
        public static void SyncHybridCLRAssemblies(RuntimeConfig config)
        {
            if (config == null) return;

            var hybridSettings = HybridCLRSettings.Instance;
            if (hybridSettings == null) return;

            var asmdefList = new List<AssemblyDefinitionAsset>();
            foreach (var dllName in config.hotUpdateDlls)
            {
                var assemblyName = dllName.Replace(".dll", "");
                var guids = AssetDatabase.FindAssets($"t:AssemblyDefinitionAsset {assemblyName}");

                foreach (var guid in guids)
                {
                    var path = AssetDatabase.GUIDToAssetPath(guid);
                    var asmdef = AssetDatabase.LoadAssetAtPath<AssemblyDefinitionAsset>(path);
                    if (asmdef != null && asmdef.name == assemblyName)
                    {
                        asmdefList.Add(asmdef);
                        break;
                    }
                }
            }

            hybridSettings.hotUpdateAssemblyDefinitions = asmdefList.ToArray();
            HybridCLRSettings.Save();
            Debug.Log($"[RuntimeConfig] 已同步HybridCLR程序集引用: {asmdefList.Count}个");
        }

        public static void SyncPythonConfig(RuntimeConfig config) => SyncPythonConfig(config, config.useRemote);

        public static void SyncPythonConfig(RuntimeConfig config, bool useRemote)
        {
            string projectPath = Application.dataPath.Replace("/Assets", "");
            string parentPath = Directory.GetParent(projectPath)?.FullName;

            if (string.IsNullOrEmpty(parentPath))
            {
                Debug.LogError("无法获取上级目录路径");
                return;
            }

            string configPath = Path.Combine(parentPath, "Tools", "BundleUpload", "config.py");

            if (!File.Exists(configPath))
            {
                Debug.LogWarning($"Python配置文件不存在: {configPath}");
                return;
            }

            var server = useRemote ? config.remoteServer : config.localServer;
            string platform = EditorUserBuildSettings.activeBuildTarget.ToString();

            string content = $@"# Bundle根目录
from pathlib import Path
BUNDLE_ROOT = Path(__file__).parent.parent.parent / ""GameShell"" / ""Bundles""

# MinIO配置
MINIO_ENDPOINT = ""{server.upLoadpoint}""
MINIO_ACCESS_KEY = ""{server.accessKey}""
MINIO_SECRET_KEY = ""{server.secretKey}""
MINIO_BUCKET = ""{server.bucket}""

# 平台
PLATFORM = ""{platform}""

# 版本保留数量
MAX_VERSION_COUNT = {config.maxVersionCount}
";

            File.WriteAllText(configPath, content);
            Debug.Log($"Python配置已同步到{(useRemote ? "远程" : "本地")}服务器: {server.upLoadpoint}");
        }
    }

    [CustomEditor(typeof(EntryPoint))]
    public class EntryPointEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor _configEditor;
        private RuntimeConfig _lastConfig;

        private void OnEnable()
        {
            var config = RuntimeConfigInitializer.GetConfig();
            if (config != null)
            {
                _configEditor = CreateEditor(config);
                _lastConfig = config;
            }
        }

        private void OnDisable()
        {
            if (_configEditor != null)
                DestroyImmediate(_configEditor);
        }

        public override void OnInspectorGUI()
        {
            var config = RuntimeConfigInitializer.GetConfig();
            if (config == null)
            {
                EditorGUILayout.HelpBox("RuntimeConfig not found!", MessageType.Error);
                return;
            }

            if (_configEditor == null || _configEditor.target != config)
            {
                if (_configEditor != null)
                    DestroyImmediate(_configEditor);
                _configEditor = CreateEditor(config);
                _lastConfig = config;
            }

            EditorGUI.BeginChangeCheck();
            _configEditor.OnInspectorGUI();
            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(config);
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("同步到HybridCLR", GUILayout.Height(25)))
            {
                RuntimeConfigInitializer.SyncHybridCLRAssemblies(config);
            }
        }
    }
}
