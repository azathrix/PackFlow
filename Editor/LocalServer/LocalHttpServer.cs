using System;
using System.Diagnostics;
using System.IO;
using UnityEditor;

namespace Editor.LocalServer
{
    /// <summary>
    /// 本地 HTTP 服务器管理器（通过 Python 进程实现）
    /// </summary>
    public class LocalHttpServer : IDisposable
    {
        private const string ProcessIdKey = "PackFlow_LocalServer_ProcessId";
        private const string LogFileKey = "PackFlow_LocalServer_LogFile";

        private Process _process;
        private bool _isRunning;
        private string _logFilePath;
        private long _lastLogPosition;

        public string RootDirectory { get; set; }
        public int Port { get; set; } = 8080;

        public bool IsRunning
        {
            get
            {
                if (_isRunning && _process != null && !_process.HasExited)
                    return true;

                TryRestoreProcess();
                return _isRunning && _process != null && !_process.HasExited;
            }
        }

        public event Action<string> OnLog;

        private static string ScriptPath
        {
            get
            {
                var packageInfo = UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(LocalHttpServer).Assembly);
                if (packageInfo != null)
                    return Path.GetFullPath(Path.Combine(packageInfo.resolvedPath, "Tools~", "LocalServer", "server.py"));
                return null;
            }
        }

        private static string LogDirectory
        {
            get
            {
                var path = Path.Combine(Path.GetTempPath(), "PackFlow");
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
                return path;
            }
        }

        private void TryRestoreProcess()
        {
            if (_process != null) return;

            var savedPid = EditorPrefs.GetInt(ProcessIdKey, -1);
            if (savedPid <= 0) return;

            try
            {
                var process = Process.GetProcessById(savedPid);
                if (process != null && !process.HasExited)
                {
                    _process = process;
                    _isRunning = true;
                    _logFilePath = EditorPrefs.GetString(LogFileKey, "");
                }
                else
                {
                    ClearSavedState();
                }
            }
            catch
            {
                ClearSavedState();
            }
        }

        private void ClearSavedState()
        {
            EditorPrefs.DeleteKey(ProcessIdKey);
            EditorPrefs.DeleteKey(LogFileKey);
            _isRunning = false;
        }

        public void Start()
        {
            if (IsRunning) return;

            if (string.IsNullOrEmpty(RootDirectory))
            {
                Log("根目录未设置");
                return;
            }

            if (!Directory.Exists(RootDirectory))
                Directory.CreateDirectory(RootDirectory);

            if (!File.Exists(ScriptPath))
            {
                Log($"找不到服务器脚本: {ScriptPath}");
                return;
            }

            try
            {
                _logFilePath = Path.Combine(LogDirectory, $"server_{Port}.log");
                _lastLogPosition = 0;

                var startInfo = new ProcessStartInfo
                {
                    FileName = "python",
                    Arguments = $"\"{ScriptPath}\" -p {Port} -r \"{RootDirectory}\" -l \"{_logFilePath}\"",
                    UseShellExecute = true,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };

                _process = Process.Start(startInfo);
                _isRunning = true;

                // 保存状态
                EditorPrefs.SetInt(ProcessIdKey, _process.Id);
                EditorPrefs.SetString(LogFileKey, _logFilePath);
                Log($"服务器已启动 (PID: {_process.Id})");
            }
            catch (Exception e)
            {
                Log($"启动失败: {e.Message}");
                _isRunning = false;
            }
        }

        public void Stop()
        {
            TryRestoreProcess();

            if (_process == null) return;

            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill();
                    _process.WaitForExit(1000);
                }
            }
            catch (Exception e)
            {
                Log($"停止失败: {e.Message}");
            }
            finally
            {
                _process?.Dispose();
                _process = null;
                _isRunning = false;
                ClearSavedState();
                Log("服务器已停止");
            }
        }

        /// <summary>
        /// 读取新的日志内容
        /// </summary>
        public string ReadNewLogs()
        {
            if (string.IsNullOrEmpty(_logFilePath) || !File.Exists(_logFilePath))
            {
                // 尝试恢复日志文件路径
                _logFilePath = EditorPrefs.GetString(LogFileKey, "");
                if (string.IsNullOrEmpty(_logFilePath) || !File.Exists(_logFilePath))
                    return null;
            }

            try
            {
                using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                if (fs.Length <= _lastLogPosition)
                    return null;

                fs.Seek(_lastLogPosition, SeekOrigin.Begin);
                using var reader = new StreamReader(fs);
                var newContent = reader.ReadToEnd();
                _lastLogPosition = fs.Length;
                return newContent;
            }
            catch
            {
                return null;
            }
        }

        private void Log(string message)
        {
            OnLog?.Invoke($"[{DateTime.Now:HH:mm:ss}] {message}");
        }

        public void Dispose()
        {
            Stop();
        }
    }
}
