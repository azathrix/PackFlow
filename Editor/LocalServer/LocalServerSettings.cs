using System;
using UnityEditor;
using UnityEngine;

namespace Azathrix.PackFlow
{
    /// <summary>
    /// 本地服务器配置
    /// </summary>
    [FilePath("PackFlow/LocalServerSettings.asset", FilePathAttribute.Location.PreferencesFolder)]
    public class LocalServerSettings : ScriptableSingleton<LocalServerSettings>
    {
        [SerializeField] private int port = 8080;
        [SerializeField] private string rootDirectory;
        [SerializeField] private bool autoStartOnUnityOpen;
        [SerializeField] private bool showLogs = true;

        public int Port
        {
            get => port;
            set { port = value; Save(true); }
        }

        public string RootDirectory
        {
            get => rootDirectory;
            set { rootDirectory = value; Save(true); }
        }

        public bool AutoStartOnUnityOpen
        {
            get => autoStartOnUnityOpen;
            set { autoStartOnUnityOpen = value; Save(true); }
        }

        public bool ShowLogs
        {
            get => showLogs;
            set { showLogs = value; Save(true); }
        }

        public void Save()
        {
            Save(true);
        }
    }
}
