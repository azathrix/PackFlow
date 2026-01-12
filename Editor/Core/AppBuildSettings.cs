using Azathrix.Framework.Settings;
using UnityEditor;
using UnityEngine;

namespace Editor.Core
{
    /// <summary>
    /// 应用构建设置
    /// </summary>
    [FilePath("ProjectSettings/PackFlowAppBuildSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class AppBuildSettings : ScriptableSingleton<AppBuildSettings>
    {
        [Header("版本设置")]
        [Tooltip("构建后自动递增版本")]
        public bool autoIncrementBuild = true;
        public VersionIncrementType autoIncrementType = VersionIncrementType.Build;

        // 版本信息从框架配置读取
        private AzathrixFrameworkSettings FrameworkSettings => AzathrixFrameworkSettings.Instance;
        public string Version => FrameworkSettings?.Version ?? "1.0.0";
        public string FullVersion => FrameworkSettings?.FullVersion ?? "1.0.0.1";

        [Header("Android 设置")]
        public bool androidExportProject;
        public AndroidBuildType androidBuildType = AndroidBuildType.APK;
        public string androidKeystorePath;
        public string androidKeystorePass;
        public string androidKeyaliasName;
        public string androidKeyaliasPass;

        [Header("Windows 设置")]
        public bool windowsCreateZip;

        [Header("通用设置")]
        public bool developmentBuild;
        public bool scriptDebugging;
        public string customDefines;
        public bool buildAssetBeforeApp;

        public void IncrementVersion()
        {
            var settings = FrameworkSettings;
            if (settings == null) return;

            switch (autoIncrementType)
            {
                case VersionIncrementType.Build:
                    settings.buildNumber++;
                    break;
                case VersionIncrementType.Patch:
                    settings.patchVersion++;
                    settings.buildNumber = 1;
                    break;
                case VersionIncrementType.Minor:
                    settings.minorVersion++;
                    settings.patchVersion = 0;
                    settings.buildNumber = 1;
                    break;
                case VersionIncrementType.Major:
                    settings.majorVersion++;
                    settings.minorVersion = 0;
                    settings.patchVersion = 0;
                    settings.buildNumber = 1;
                    break;
            }
            settings.Save();
        }

        public void Save() => Save(true);
    }

    public enum VersionIncrementType
    {
        Build,
        Patch,
        Minor,
        Major
    }

    public enum AndroidBuildType
    {
        APK,
        AAB
    }
}
