using Azathrix.Framework.Tools;
using UnityEditor;
using UnityEngine;

namespace Azathrix.PackFlow
{
    /// <summary>
    /// 本地服务器控制窗口
    /// </summary>
    public class LocalServerWindow : EditorWindow
    {
        private static LocalHttpServer _server;
        private LocalServerSettings _settings;
        private static double _lastLogCheckTime;

        [MenuItem("Azathrix/PackFlow/本地服务器", priority = 1001)]
        public static void ShowWindow()
        {
            var window = GetWindow<LocalServerWindow>("本地服务器");
            window.minSize = new Vector2(400, 250);
        }

        private void OnEnable()
        {
            _settings = LocalServerSettings.instance;
            EnsureServerInstance();

            if (_server != null)
                _server.OnLog += OnServerLog;

            // 注册日志轮询
            EditorApplication.update -= PollServerLogs;
            EditorApplication.update += PollServerLogs;
        }

        private void OnDisable()
        {
            if (_server != null)
                _server.OnLog -= OnServerLog;
        }

        private static void EnsureServerInstance()
        {
            if (_server == null)
            {
                var settings = LocalServerSettings.instance;
                _server = new LocalHttpServer
                {
                    Port = settings.Port,
                    RootDirectory = settings.RootDirectory
                };
            }
        }

        private static void PollServerLogs()
        {
            // 每秒检查一次
            if (EditorApplication.timeSinceStartup - _lastLogCheckTime < 1.0)
                return;
            _lastLogCheckTime = EditorApplication.timeSinceStartup;

            if (_server == null || !_server.IsRunning)
                return;

            // 检查是否启用日志显示
            if (!LocalServerSettings.instance.ShowLogs)
                return;

            var newLogs = _server.ReadNewLogs();
            if (string.IsNullOrEmpty(newLogs))
                return;

            // 按行输出日志
            var lines = newLogs.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    Log.Info($"[LocalServer] {trimmed}");
            }
        }

        private void OnServerLog(string message)
        {
            Log.Info($"[LocalServer] {message}");
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(10);

            EnsureServerInstance();

            // 状态
            EditorGUILayout.BeginHorizontal();
            var isRunning = _server?.IsRunning ?? false;
            var statusColor = isRunning ? Color.green : Color.gray;
            GUI.color = statusColor;
            GUILayout.Label(isRunning ? "● 运行中" : "● 已停止", GUILayout.Width(80));
            GUI.color = Color.white;

            if (isRunning)
                GUILayout.Label($"http://localhost:{_settings.Port}/", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            // 配置
            EditorGUILayout.LabelField("服务器配置", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            EditorGUI.BeginDisabledGroup(isRunning);

            var newPort = EditorGUILayout.IntField("端口", _settings.Port);
            if (newPort != _settings.Port)
                _settings.Port = newPort;

            EditorGUILayout.BeginHorizontal();
            var newRoot = EditorGUILayout.TextField("根目录", _settings.RootDirectory);
            if (newRoot != _settings.RootDirectory)
                _settings.RootDirectory = newRoot;

            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var path = EditorUtility.OpenFolderPanel("选择根目录", _settings.RootDirectory, "");
                if (!string.IsNullOrEmpty(path))
                    _settings.RootDirectory = path;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUI.EndDisabledGroup();

            var newAutoStart = EditorGUILayout.Toggle("Unity启动时自动开启", _settings.AutoStartOnUnityOpen);
            if (newAutoStart != _settings.AutoStartOnUnityOpen)
                _settings.AutoStartOnUnityOpen = newAutoStart;

            var newShowLogs = EditorGUILayout.Toggle("显示操作日志", _settings.ShowLogs);
            if (newShowLogs != _settings.ShowLogs)
                _settings.ShowLogs = newShowLogs;

            EditorGUILayout.EndVertical();

            EditorGUILayout.Space(10);

            // 控制按钮
            EditorGUILayout.BeginHorizontal();
            GUI.backgroundColor = isRunning ? Color.red : Color.green;
            if (GUILayout.Button(isRunning ? "停止服务器" : "启动服务器", GUILayout.Height(30)))
            {
                if (isRunning)
                    StopServer();
                else
                    StartServer();
            }
            GUI.backgroundColor = Color.white;

            EditorGUI.BeginDisabledGroup(!isRunning);
            if (GUILayout.Button("打开管理页面", GUILayout.Height(30), GUILayout.Width(100)))
                Application.OpenURL($"http://localhost:{_settings.Port}/");
            EditorGUI.EndDisabledGroup();
            EditorGUILayout.EndHorizontal();
        }

        private void StartServer()
        {
            if (string.IsNullOrEmpty(_settings.RootDirectory))
            {
                EditorUtility.DisplayDialog("错误", "请先设置根目录", "确定");
                return;
            }

            _server = new LocalHttpServer
            {
                Port = _settings.Port,
                RootDirectory = _settings.RootDirectory
            };
            _server.OnLog += OnServerLog;
            _server.Start();
        }

        private void StopServer()
        {
            _server?.Stop();
            _server?.Dispose();
            _server = null;
        }

        [InitializeOnLoadMethod]
        private static void AutoStart()
        {
            // 注册日志轮询
            EditorApplication.update -= PollServerLogs;
            EditorApplication.update += PollServerLogs;

            EditorApplication.delayCall += () =>
            {
                var settings = LocalServerSettings.instance;

                EnsureServerInstance();
                if (_server.IsRunning)
                    return;

                if (settings.AutoStartOnUnityOpen && !string.IsNullOrEmpty(settings.RootDirectory))
                {
                    _server = new LocalHttpServer
                    {
                        Port = settings.Port,
                        RootDirectory = settings.RootDirectory
                    };
                    _server.Start();
                }
            };
        }
    }
}
