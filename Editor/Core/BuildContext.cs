using System.Collections.Generic;
using UnityEditor;

namespace Azathrix.PackFlow
{
    /// <summary>
    /// 构建上下文，在构建步骤之间传递数据
    /// </summary>
    public class BuildContext
    {
        /// <summary>
        /// 构建目标平台
        /// </summary>
        public BuildTarget BuildTarget { get; set; }

        /// <summary>
        /// 输出根目录
        /// </summary>
        public string OutputRoot { get; set; }

        /// <summary>
        /// 要构建的包名列表
        /// </summary>
        public List<string> PackageNames { get; set; } = new();

        /// <summary>
        /// 是否执行上传
        /// </summary>
        public bool DoUpload { get; set; }

        /// <summary>
        /// 上传配置
        /// </summary>
        public UploadConfig UploadConfig { get; set; }

        /// <summary>
        /// 自定义数据存储
        /// </summary>
        public Dictionary<string, object> Data { get; } = new();

        /// <summary>
        /// 构建日志
        /// </summary>
        public List<string> Logs { get; } = new();

        /// <summary>
        /// 构建是否成功
        /// </summary>
        public bool Success { get; set; } = true;

        /// <summary>
        /// 添加日志
        /// </summary>
        public void Log(string message)
        {
            Logs.Add($"[{System.DateTime.Now:HH:mm:ss}] {message}");
            UnityEngine.Debug.Log($"[PackFlow] {message}");
        }

        /// <summary>
        /// 添加错误日志
        /// </summary>
        public void LogError(string message)
        {
            Logs.Add($"[{System.DateTime.Now:HH:mm:ss}] ERROR: {message}");
            UnityEngine.Debug.LogError($"[PackFlow] {message}");
        }

        /// <summary>
        /// 获取自定义数据
        /// </summary>
        public T GetData<T>(string key, T defaultValue = default)
        {
            return Data.TryGetValue(key, out var value) && value is T t ? t : defaultValue;
        }

        /// <summary>
        /// 设置自定义数据
        /// </summary>
        public void SetData<T>(string key, T value)
        {
            Data[key] = value;
        }
    }
}
