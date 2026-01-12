using System;
using UnityEditor;
using UnityEngine;

namespace Azathrix.PackFlow
{
    /// <summary>
    /// 应用构建设置
    /// </summary>
    [FilePath("ProjectSettings/PackFlowAppBuildSettings.asset", FilePathAttribute.Location.ProjectFolder)]
    public class AppBuildSettings : ScriptableSingleton<AppBuildSettings>
    {
        [Header("版本设置")]
        public string versionFormat = "{major}.{minor}.{patch}";
        public int majorVersion = 1;
        public int minorVersion = 0;
        public int patchVersion = 0;
        public int buildNumber = 1;
        public bool autoIncrementBuild = true;
        public VersionIncrementType autoIncrementType = VersionIncrementType.Build;

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

        public string Version => versionFormat
            .Replace("{major}", majorVersion.ToString())
            .Replace("{minor}", minorVersion.ToString())
            .Replace("{patch}", patchVersion.ToString())
            .Replace("{build}", buildNumber.ToString());

        public string FullVersion => $"{Version}.{buildNumber}";

        public void IncrementVersion()
        {
            switch (autoIncrementType)
            {
                case VersionIncrementType.Build:
                    buildNumber++;
                    break;
                case VersionIncrementType.Patch:
                    patchVersion++;
                    buildNumber = 1;
                    break;
                case VersionIncrementType.Minor:
                    minorVersion++;
                    patchVersion = 0;
                    buildNumber = 1;
                    break;
                case VersionIncrementType.Major:
                    majorVersion++;
                    minorVersion = 0;
                    patchVersion = 0;
                    buildNumber = 1;
                    break;
            }
            Save(true);
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
