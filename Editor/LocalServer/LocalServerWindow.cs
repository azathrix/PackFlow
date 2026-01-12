using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace Editor.LocalServer
{
    /// <summary>
    /// 本地服务器控制窗口
    /// </summary>
    public class LocalServerWindow : EditorWindow
    {
        private static LocalHttpServer _server;
        private LocalServerSettings _settings;
        private static double _lastLogCheckTime;

        // 日志相关
        private static readonly List<string> _logs = new();
        private static Vector2 _logScrollPos;
        private static string _filterText = "";
        private static bool _autoScroll = true;
        private static bool _isAtBottom = true;
        private const int MaxLogCount = 1000;

        [MenuItem("Azathrix/PackFlow/本地服务器", priority = 1001)]
        public static void ShowWindow()
        {
            var window = GetWindow<LocalServerWindow>("本地服务器");
            window.minSize = new Vector2(400, 400);
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
            if (EditorApplication.timeSinceStartup - _lastLogCheckTime < 0.5)
                return;
            _lastLogCheckTime = EditorApplication.timeSinceStartup;

            if (_server == null || !_server.IsRunning)
                return;

            if (!LocalServerSettings.instance.ShowLogs)
                return;

            var newLogs = _server.ReadNewLogs();
            if (string.IsNullOrEmpty(newLogs))
                return;

            var lines = newLogs.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (!string.IsNullOrEmpty(trimmed))
                    AddLog(trimmed);
            }
        }

        private static void AddLog(string message)
        {
            // 简化日志格式
            var time = System.DateTime.Now.ToString("HH:mm:ss");
            _logs.Add($"[{time}] {message}");

            // 限制日志数量
            while (_logs.Count > MaxLogCount)
                _logs.RemoveAt(0);
        }

        private void OnServerLog(string message)
        {
            AddLog(message);
            Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(5);

            EnsureServerInstance();

            // 状态栏
            DrawStatusBar();

            EditorGUILayout.Space(5);

            // 配置区域
            DrawConfig();

            EditorGUILayout.Space(5);

            // 控制按钮
            DrawControlButtons();

            EditorGUILayout.Space(5);

            // 日志区域
            if (_settings.ShowLogs)
                DrawLogArea();
        }

        private void DrawStatusBar()
        {
            EditorGUILayout.BeginHorizontal();
            var isRunning = _server?.IsRunning ?? false;
            var statusColor = isRunning ? Color.green : Color.gray;
            GUI.color = statusColor;
            GUILayout.Label(isRunning ? "● 运行中" : "● 已停止", GUILayout.Width(80));
            GUI.color = Color.white;

            if (isRunning)
                GUILayout.Label($"http://localhost:{_settings.Port}/", EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
        }

        private void DrawConfig()
        {
            var isRunning = _server?.IsRunning ?? false;

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
        }

        private void DrawControlButtons()
        {
            var isRunning = _server?.IsRunning ?? false;

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

        private void DrawLogArea()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("日志", EditorStyles.boldLabel, GUILayout.Width(30));

            // 过滤
            EditorGUILayout.LabelField("过滤:", GUILayout.Width(35));
            _filterText = EditorGUILayout.TextField(_filterText, GUILayout.Width(100));

            GUILayout.FlexibleSpace();

            _autoScroll = GUILayout.Toggle(_autoScroll, "自动滚动", GUILayout.Width(70));

            if (GUILayout.Button("复制", GUILayout.Width(45)))
                CopyLogs();

            if (GUILayout.Button("清空", GUILayout.Width(45)))
                _logs.Clear();

            EditorGUILayout.EndHorizontal();

            // 日志显示区域
            var logAreaRect = EditorGUILayout.BeginVertical("box", GUILayout.ExpandHeight(true));

            _logScrollPos = EditorGUILayout.BeginScrollView(_logScrollPos);

            var hasFilter = !string.IsNullOrEmpty(_filterText);
            var filterLower = hasFilter ? _filterText.ToLower() : "";

            foreach (var log in _logs)
            {
                if (hasFilter && !log.ToLower().Contains(filterLower))
                    continue;

                // 根据内容设置颜色
                if (log.Contains("ERROR") || log.Contains("错误"))
                    GUI.color = new Color(1f, 0.4f, 0.4f);
                else if (log.Contains("WARN") || log.Contains("警告"))
                    GUI.color = new Color(1f, 0.8f, 0.4f);
                else
                    GUI.color = Color.white;

                EditorGUILayout.SelectableLabel(log, EditorStyles.miniLabel, GUILayout.Height(16));
            }

            GUI.color = Color.white;

            EditorGUILayout.EndScrollView();

            // 检测用户是否手动滚动（滚动位置变化但不是自动滚动导致的）
            if (Event.current.type == EventType.Repaint)
            {
                // 计算内容高度和可视区域高度
                var contentHeight = _logs.Count * 16f;
                var viewHeight = logAreaRect.height - 10;
                var maxScroll = Mathf.Max(0, contentHeight - viewHeight);

                // 判断是否在底部（允许一定误差）
                _isAtBottom = _logScrollPos.y >= maxScroll - 20;

                // 只有开启自动滚动且在底部时才自动滚动
                if (_autoScroll && _isAtBottom)
                    _logScrollPos.y = float.MaxValue;
            }

            EditorGUILayout.EndVertical();
        }

        private void CopyLogs()
        {
            var sb = new StringBuilder();
            var hasFilter = !string.IsNullOrEmpty(_filterText);
            var filterLower = hasFilter ? _filterText.ToLower() : "";

            foreach (var log in _logs)
            {
                if (hasFilter && !log.ToLower().Contains(filterLower))
                    continue;
                sb.AppendLine(log);
            }

            GUIUtility.systemCopyBuffer = sb.ToString();
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
            AddLog("服务器已启动");
        }

        private void StopServer()
        {
            _server?.Stop();
            _server?.Dispose();
            _server = null;
            AddLog("服务器已停止");
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
                    AddLog("服务器自动启动");
                }
            };
        }
    }
}
