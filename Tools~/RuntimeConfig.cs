using System;
using System.Collections.Generic;
using Sirenix.OdinInspector;
using UnityEngine;
using YooAsset;

public enum UploadApiType { MinIO, AwsS3, TencentCos }

[CreateAssetMenu(fileName = "RuntimeConfig", menuName = "Config/RuntimeConfig")]
public class RuntimeConfig : ScriptableObject
{
    [Serializable]
    public class ServerConfig
    {
        [LabelText("上传API类型")]
        public UploadApiType apiType = UploadApiType.MinIO;

        [LabelText("上传服务器地址"), HideIf("apiType", UploadApiType.TencentCos)]
        public string upLoadpoint = "192.168.101.11:61296";

        [LabelText("COS区域"), ShowIf("apiType", UploadApiType.TencentCos)]
        public string cosRegion = "ap-guangzhou";

        [LabelText("下载服务器地址(含协议)")]
        public string DownLoadpoint = "http://192.168.101.11:61296";

        [LabelText("Bucket(上传用)")]
        public string bucket = "puzzle02";

        [LabelText("版本号")]
        public string version = "1.0.0";

#if UNITY_EDITOR
        [LabelText("Access Key")]
        public string accessKey = "admin";

        [LabelText("Secret Key")]
        public string secretKey = "egWZt3RL628VudQ3";
#endif
    }

    [TitleGroup("YooAsset配置")]
    [LabelText("主资源包")]
    public string mainPackageName = "MainPackage";

    [LabelText("图片资源包")]
    public string picturePackageName = "PicturePackage";

    [LabelText("热更新包")]
    public string hotUpdatePackageName = "HotUpdatePackage";

    [LabelText("游戏核心包")]
    public string gameCorePackageName = "GameCorePackage";

    [LabelText("游戏渠道包")]
    public string gameChannelPackageName = "GameChannelPackage";

    [LabelText("运行模式")]
    public EPlayMode playMode = EPlayMode.EditorSimulateMode;

    [TitleGroup("服务器配置")]
    [LabelText("使用远程服务器")]
    public bool useRemote = true;

    [LabelText("远程服务器"), ShowIf("useRemote")]
    public ServerConfig remoteServer = new()
    {
        DownLoadpoint = "http://35.245.172.230:61296",
        bucket = "puzzle"
    };

    [LabelText("本地服务器"), HideIf("useRemote")]
    public ServerConfig localServer = new()
    {
        DownLoadpoint = "http://192.168.101.11:61296",
        bucket = "puzzle02"
    };

    [TitleGroup("热更配置")]
    [LabelText("热更DLL列表")]
    public List<string> hotUpdateDlls = new()
    {
        "Framework.dll",
        "Game.dll",
        "Shell.dll",
        "WatermelonCore.dll",
        "ProjectFiles.dll"
    };

    [TitleGroup("其他配置")]
    [LabelText("启动场景")]
    public string launcherScene = "Launcher";

#if UNITY_EDITOR
    [LabelText("版本保留数量")]
    public int maxVersionCount = 5;

    [LabelText("平台")]
    public string platform = "Android";
#endif

    public ServerConfig CurrentServer => useRemote ? remoteServer : localServer;

    public EPlayMode GetPlayMode()
    {
#if !UNITY_EDITOR
        return EPlayMode.HostPlayMode;
#else
        return playMode;
#endif
    }

    public string GetResServerAddress()
    {
        return GetPackageServerAddress("");
    }

    /// <summary>
    /// 获取指定package的资源服务器地址
    /// 格式: {DownLoadpoint}/{projectId}/{platform}/{version}/{packageName}
    /// </summary>
    public string GetPackageServerAddress(string packageName)
    {
        var server = CurrentServer;
        var parts = new List<string> { server.DownLoadpoint.TrimEnd('/') };

        // 从 Resources/ProjectId.txt 读取 projectId
        var projectIdAsset = Resources.Load<TextAsset>("ProjectId");
        if (projectIdAsset != null && !string.IsNullOrEmpty(projectIdAsset.text))
            parts.Add(projectIdAsset.text.Trim());

#if UNITY_EDITOR
        parts.Add(platform);
#else
        parts.Add(Application.platform == RuntimePlatform.Android ? "Android" : "iOS");
#endif
        if (!string.IsNullOrEmpty(server.version)) parts.Add(server.version);
        if (!string.IsNullOrEmpty(packageName)) parts.Add(packageName);
        return string.Join("/", parts);
    }
}
