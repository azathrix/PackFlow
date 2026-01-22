using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Azathrix.PackFlow.Editor.Core
{
    /// <summary>
    /// 上传配置注册表 - 支持多套上传配置的增删改
    /// </summary>
    [FilePath("ProjectSettings/PackFlowUploadConfig.asset", FilePathAttribute.Location.ProjectFolder)]
    public class UploadConfigRegistry : ScriptableSingleton<UploadConfigRegistry>
    {
        [Serializable]
        public class NamedUploadConfig
        {
            public string name = "新配置";
            public UploadConfig config = new();
        }

        [SerializeField]
        private List<NamedUploadConfig> _configs = new()
        {
            new() { name = "本地服务器", config = new() { apiType = UploadApiType.LocalHttp, endpoint = "http://127.0.0.1:8080" } },
            new() { name = "远程服务器", config = new() { apiType = UploadApiType.MinIO } }
        };

        [SerializeField]
        private int _selectedIndex;

        public IReadOnlyList<NamedUploadConfig> Configs => _configs;

        public int SelectedIndex
        {
            get => _selectedIndex;
            set
            {
                _selectedIndex = Mathf.Clamp(value, 0, Mathf.Max(0, _configs.Count - 1));
                Save(true);
            }
        }

        public UploadConfig Current => _configs.Count > 0 ? _configs[_selectedIndex].config : null;

        public NamedUploadConfig CurrentNamed => _configs.Count > 0 ? _configs[_selectedIndex] : null;

        /// <summary>
        /// 添加新配置
        /// </summary>
        public void AddConfig(string name = "新配置")
        {
            _configs.Add(new NamedUploadConfig { name = name });
            Save(true);
        }

        /// <summary>
        /// 删除配置
        /// </summary>
        public void RemoveConfig(int index)
        {
            if (_configs.Count <= 1) return; // 至少保留一个
            if (index < 0 || index >= _configs.Count) return;

            _configs.RemoveAt(index);
            if (_selectedIndex >= _configs.Count)
                _selectedIndex = _configs.Count - 1;
            Save(true);
        }

        /// <summary>
        /// 复制配置
        /// </summary>
        public void DuplicateConfig(int index)
        {
            if (index < 0 || index >= _configs.Count) return;

            var source = _configs[index];
            var copy = new NamedUploadConfig
            {
                name = source.name + " (副本)",
                config = JsonUtility.FromJson<UploadConfig>(JsonUtility.ToJson(source.config))
            };
            _configs.Insert(index + 1, copy);
            Save(true);
        }

        /// <summary>
        /// 获取所有配置名称
        /// </summary>
        public string[] GetConfigNames()
        {
            var names = new string[_configs.Count];
            for (int i = 0; i < _configs.Count; i++)
                names[i] = _configs[i].name;
            return names;
        }

        /// <summary>
        /// 标记为已修改并保存
        /// </summary>
        public void MarkDirty()
        {
            Save(true);
        }
    }
}
