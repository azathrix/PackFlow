using System;
using System.Reflection;
using Cysharp.Threading.Tasks;
using GameProject.Scripts.PuzzleGame.Manager;
using HybridCLR;
using Sirenix.OdinInspector;
using UnityEngine;
using YooAsset;

public class EntryPoint : MonoBehaviour
{
    public static EntryPoint Instance { get; private set; }

    private RuntimeConfig _config;

    public RuntimeConfig config
    {
        get
        {
            if (_config == null)
            {
                // 优先级: RuntimeConfigChannel > RuntimeConfigGameCore > RuntimeConfig
                _config = Resources.Load<RuntimeConfig>("RuntimeConfigChannel")
                       ?? Resources.Load<RuntimeConfig>("RuntimeConfigGameCore")
                       ?? Resources.Load<RuntimeConfig>("RuntimeConfig");
            }
            return _config;
        }
    }

    public ResourcePackage mainPackage { get; private set; }
    public ResourcePackage picturePackage { get; private set; }
    public ResourcePackage hotUpdatePackage { get; private set; }
    public ResourcePackage gameCorePackage { get; private set; }
    public ResourcePackage gameChannelPackage { get; private set; }

    private void Awake()
    {
        Instance = this;
    }

    struct RemoteServices : IRemoteServices
    {
        private readonly string _defaultHostServer;
        private readonly string _fallbackHostServer;

        public RemoteServices(string defaultHostServer, string fallbackHostServer)
        {
            _defaultHostServer = defaultHostServer;
            _fallbackHostServer = fallbackHostServer;
        }

        string IRemoteServices.GetRemoteMainURL(string fileName)
        {
            return $"{_defaultHostServer}/{fileName}";
        }

        string IRemoteServices.GetRemoteFallbackURL(string fileName)
        {
            return $"{_fallbackHostServer}/{fileName}";
        }
    }

    private async UniTask InitializeYooAsset(EPlayMode playMode, ResourcePackage package,
        string packageName, string address)
    {
        InitializationOperation initOperation = null;

        re3:
        switch (playMode)
        {
            case EPlayMode.EditorSimulateMode:
            {
                try
                {
                    var buildResult = EditorSimulateModeHelper.SimulateBuild(packageName);
                    var packageRoot = buildResult.PackageRootDirectory;
                    var fileSystemParams = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);

                    var createParameters = new EditorSimulateModeParameters();
                    createParameters.EditorFileSystemParameters = fileSystemParams;

                    initOperation = package.InitializeAsync(createParameters);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{packageName} 没有配置资源，跳过初始化: {e.Message}");
                    return;
                }
            }
                break;
            case EPlayMode.OfflinePlayMode:
            {
                var buildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                var initParameters = new OfflinePlayModeParameters();
                initParameters.BuildinFileSystemParameters = buildinFileSystemParams;
                initOperation = package.InitializeAsync(initParameters);
            }
                break;
            case EPlayMode.HostPlayMode:
            {
                IRemoteServices remoteServices = new RemoteServices(address, address);
                var cacheFileSystemParams =
                    FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);
                var buildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                var initParameters = new HostPlayModeParameters();
                initParameters.BuildinFileSystemParameters = buildinFileSystemParams;
                initParameters.CacheFileSystemParameters = cacheFileSystemParams;
                initOperation = package.InitializeAsync(initParameters);
            }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        await initOperation;

        if (initOperation.Status == EOperationStatus.Succeed)
        {
            Debug.Log("资源包初始化成功！");
        }
        else
        {
            Debug.LogError($"资源包初始化失败：{initOperation.Error}");
            await UniTask.WaitForSeconds(1);
            goto re3;
        }

        re:
        //获取包版本
        var operation = package.RequestPackageVersionAsync();
        await operation;
        if (operation.Status == EOperationStatus.Succeed)
        {
            string packageVersion = operation.PackageVersion;
            Debug.Log($"请求资源包版本 : {packageVersion}");

            re2:
            //更新资源清单
            var manifestOperation = package.UpdatePackageManifestAsync(packageVersion);
            await manifestOperation;
            //下载所有内置资源
            if (manifestOperation.Status == EOperationStatus.Succeed)
            {
                await DownLoadByTag(packageName, "Download", "Builtin", "Sound");
            }
            else
            {
                Debug.LogError(operation.Error);
                Debug.LogError("重新请求更新资源列表 UpdatePackageManifestAsync");
                await UniTask.WaitForSeconds(1);
                goto re2;
            }
        }
        else
        {
            Debug.LogError("重新请求下载 RequestPackageVersionAsync");
            Debug.LogError(operation.Error);
            await UniTask.WaitForSeconds(1);
            goto re;
        }
    }

    //下载
    async UniTask DownLoadByTag(string packageName, params string[] tags)
    {
        int downloadingMaxNum = 10;
        int failedTryAgain = 3;
        var package = YooAssets.GetPackage(packageName);
        var downloader = package.CreateResourceDownloader(tags, downloadingMaxNum, failedTryAgain);
        //没有需要下载的资源
        if (downloader.TotalDownloadCount == 0)
        {
            Debug.Log("资源更新完毕!");
            return;
        }

        //需要下载的文件总数和总大小
        int totalDownloadCount = downloader.TotalDownloadCount;
        long totalDownloadBytes = downloader.TotalDownloadBytes;

        //注册回调方法
        downloader.DownloadFinishCallback = OnDownloadFinishFunction; //当下载器结束（无论成功或失败）
        downloader.DownloadErrorCallback = OnDownloadErrorFunction; //当下载器发生错误
        downloader.DownloadUpdateCallback = OnDownloadUpdateFunction; //当下载进度发生变化
        downloader.DownloadFileBeginCallback = OnDownloadFileBeginFunction; //当开始下载某个文件

        //开启下载
        downloader.BeginDownload();
        await downloader;

        //检测下载结果
        if (downloader.Status == EOperationStatus.Succeed)
        {
            //下载成功
            //yield return InitCode();
            //Init().Forget();

            Debug.Log("下载成功");
        }
        else
        {
            //下载失败
            Debug.LogError("下载失败");
        }
    }


    private void OnDownloadFileBeginFunction(DownloadFileData data)
    {
        Debug.Log($"[{data.PackageName}] 开始下载文件:{data.FileName}");
    }

    private void OnDownloadUpdateFunction(DownloadUpdateData data)
    {
        Debug.Log($"下载进度更新 =>  {data.Progress}");
        EntryLoadingPanel.instance.SetProgress(data.Progress);
    }

    private void OnDownloadErrorFunction(DownloadErrorData data)
    {
        Debug.LogError($"{data.PackageName} 下载失败 => {data.FileName}");
        Debug.LogError($"{data.ErrorInfo}");
    }

    private void OnDownloadFinishFunction(DownloaderFinishData data)
    {
        Debug.Log("下载结束 => " + data.PackageName);
    }


    async void Start()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        stopwatch.Start();

        // 从Resources按优先级加载EntryLoadingPanel
        EntryLoadingPanel.CreateInstance();
        EntryLoadingPanel.instance.Show();

        await InitializeYooAssetsAsync();
#if !UNITY_EDITOR
        await InitializeHybridAsync();
#endif

        await YooAssets.LoadSceneAsync("Launcher");
        
        //设置启动时间
        ArchiveManager.SetTemp("startup_time",stopwatch.Elapsed.Ticks);
    }


    async UniTask InitializeYooAssetsAsync()
    {
        Debug.Log("初始化YooAsset..");
        YooAssets.Initialize();

        var playMode = config.GetPlayMode();

        // 1. 初始化MainPackage
        Debug.Log($"初始化 {config.mainPackageName}...");
        mainPackage = YooAssets.CreatePackage(config.mainPackageName);
        YooAssets.SetDefaultPackage(mainPackage);
        await InitializeYooAsset(playMode, mainPackage, config.mainPackageName,
            config.GetPackageServerAddress(config.mainPackageName));

        // 2. 初始化HotUpdatePackage (纯远程，无内置资源)
        Debug.Log($"初始化 {config.hotUpdatePackageName}...");
        hotUpdatePackage = YooAssets.CreatePackage(config.hotUpdatePackageName);
        await InitializeRemoteOnlyPackage(playMode, hotUpdatePackage, config.hotUpdatePackageName,
            config.GetPackageServerAddress(config.hotUpdatePackageName));

        // 3. 初始化PicturePackage (按需下载，这里只初始化)
        Debug.Log($"初始化 {config.picturePackageName}...");
        picturePackage = YooAssets.CreatePackage(config.picturePackageName);
        await InitializePicturePackage(playMode, picturePackage, config.picturePackageName,
            config.GetPackageServerAddress(config.picturePackageName));

        // 4. 初始化GameCorePackage (纯远程)
        Debug.Log($"初始化 {config.gameCorePackageName}...");
        gameCorePackage = YooAssets.CreatePackage(config.gameCorePackageName);
        await InitializeRemoteOnlyPackage(playMode, gameCorePackage, config.gameCorePackageName,
            config.GetPackageServerAddress(config.gameCorePackageName));

        // 5. 初始化GameChannelPackage (纯远程)
        Debug.Log($"初始化 {config.gameChannelPackageName}...");
        gameChannelPackage = YooAssets.CreatePackage(config.gameChannelPackageName);
        await InitializeRemoteOnlyPackage(playMode, gameChannelPackage, config.gameChannelPackageName,
            config.GetPackageServerAddress(config.gameChannelPackageName));
    }

    // 纯远程Package初始化（无内置资源，如HotUpdatePackage）
    private async UniTask InitializeRemoteOnlyPackage(EPlayMode playMode, ResourcePackage package,
        string packageName, string address)
    {
        InitializationOperation initOperation = null;

        re3:
        switch (playMode)
        {
            case EPlayMode.EditorSimulateMode:
            {
                try
                {
                    var buildResult = EditorSimulateModeHelper.SimulateBuild(packageName);
                    var packageRoot = buildResult.PackageRootDirectory;
                    var fileSystemParams = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);

                    var createParameters = new EditorSimulateModeParameters();
                    createParameters.EditorFileSystemParameters = fileSystemParams;

                    initOperation = package.InitializeAsync(createParameters);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{packageName} 没有配置资源，跳过初始化: {e.Message}");
                    return;
                }
            }
                break;
            case EPlayMode.OfflinePlayMode:
            case EPlayMode.HostPlayMode:
            {
                // 纯远程模式，只使用缓存文件系统，不使用内置文件系统
                IRemoteServices remoteServices = new RemoteServices(address, address);
                var cacheFileSystemParams =
                    FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);

                var initParameters = new HostPlayModeParameters();
                initParameters.BuildinFileSystemParameters = null;
                initParameters.CacheFileSystemParameters = cacheFileSystemParams;
                initOperation = package.InitializeAsync(initParameters);
            }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        await initOperation;

        if (initOperation.Status == EOperationStatus.Succeed)
        {
            Debug.Log($"{packageName} 初始化成功！");
        }
        else
        {
            Debug.LogError($"{packageName} 初始化失败：{initOperation.Error}");
            await UniTask.WaitForSeconds(1);
            goto re3;
        }

        re:
        var operation = package.RequestPackageVersionAsync();
        await operation;
        if (operation.Status == EOperationStatus.Succeed)
        {
            string packageVersion = operation.PackageVersion;
            Debug.Log($"{packageName} 版本 : {packageVersion}");

            re2:
            var manifestOperation = package.UpdatePackageManifestAsync(packageVersion);
            await manifestOperation;
            if (manifestOperation.Status == EOperationStatus.Succeed)
            {
                await DownLoadByTag(packageName, "Download");
            }
            else
            {
                Debug.LogError($"{packageName} 更新清单失败: {manifestOperation.Error}");
                await UniTask.WaitForSeconds(1);
                goto re2;
            }
        }
        else
        {
            Debug.LogError($"{packageName} 请求版本失败: {operation.Error}");
            await UniTask.WaitForSeconds(1);
            goto re;
        }
    }

    // PicturePackage只初始化和更新清单，不自动下载所有资源
    private async UniTask InitializePicturePackage(EPlayMode playMode, ResourcePackage package,
        string packageName, string address)
    {
        InitializationOperation initOperation = null;

        re3:
        switch (playMode)
        {
            case EPlayMode.EditorSimulateMode:
            {
                try
                {
                    var buildResult = EditorSimulateModeHelper.SimulateBuild(packageName);
                    var packageRoot = buildResult.PackageRootDirectory;
                    var fileSystemParams = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot);

                    var createParameters = new EditorSimulateModeParameters();
                    createParameters.EditorFileSystemParameters = fileSystemParams;

                    initOperation = package.InitializeAsync(createParameters);
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"{packageName} 没有配置资源，跳过初始化: {e.Message}");
                    return;
                }
            }
                break;
            case EPlayMode.OfflinePlayMode:
            {
                var buildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                var initParameters = new OfflinePlayModeParameters();
                initParameters.BuildinFileSystemParameters = buildinFileSystemParams;
                initOperation = package.InitializeAsync(initParameters);
            }
                break;
            case EPlayMode.HostPlayMode:
            {
                IRemoteServices remoteServices = new RemoteServices(address, address);
                var cacheFileSystemParams =
                    FileSystemParameters.CreateDefaultCacheFileSystemParameters(remoteServices);
                var buildinFileSystemParams = FileSystemParameters.CreateDefaultBuildinFileSystemParameters();
                var initParameters = new HostPlayModeParameters();
                initParameters.BuildinFileSystemParameters = buildinFileSystemParams;
                initParameters.CacheFileSystemParameters = cacheFileSystemParams;
                initOperation = package.InitializeAsync(initParameters);
            }
                break;
            default:
                throw new ArgumentOutOfRangeException();
        }

        await initOperation;

        if (initOperation.Status == EOperationStatus.Succeed)
        {
            Debug.Log($"{packageName} 初始化成功！");
        }
        else
        {
            Debug.LogError($"{packageName} 初始化失败：{initOperation.Error}");
            await UniTask.WaitForSeconds(1);
            goto re3;
        }

        re:
        var operation = package.RequestPackageVersionAsync();
        await operation;
        if (operation.Status == EOperationStatus.Succeed)
        {
            string packageVersion = operation.PackageVersion;
            Debug.Log($"{packageName} 版本 : {packageVersion}");

            re2:
            var manifestOperation = package.UpdatePackageManifestAsync(packageVersion);
            await manifestOperation;
            if (manifestOperation.Status == EOperationStatus.Succeed)
            {
                // 只下载Builtin标签的资源，Picture标签的按需下载
                await DownLoadByTag(packageName, "Builtin");
            }
            else
            {
                Debug.LogError($"{packageName} 更新清单失败: {manifestOperation.Error}");
                await UniTask.WaitForSeconds(1);
                goto re2;
            }
        }
        else
        {
            Debug.LogError($"{packageName} 请求版本失败: {operation.Error}");
            await UniTask.WaitForSeconds(1);
            goto re;
        }
    }


    async UniTask InitializeHybridAsync()
    {
        Debug.Log("初始化程序集..");
        
        await LoadMetadataForAOTAssemblies();
        
        await UniTask.NextFrame();

        foreach (var dllName in config.hotUpdateDlls)
        {
            try
            {
                Debug.Log($"准备加载热更程序集: {dllName}");

               
                var dllAssetHandle = hotUpdatePackage.LoadAssetAsync<TextAsset>(dllName);
                await dllAssetHandle;

                if (dllAssetHandle.Status != EOperationStatus.Succeed ||
                    !(dllAssetHandle.AssetObject is TextAsset dllTextAsset))
                {
                    Debug.LogError($"加载热更DLL文件失败: {dllName}");
                    continue;
                }

                byte[] dllBytes = dllTextAsset.bytes;
                Debug.Log($"成功读取DLL字节，长度: {dllBytes.Length}");

                byte[] pdbBytes = null;
                string pdbName = dllName.Replace(".dll", ".pdb");
                var pdbAssetHandle = hotUpdatePackage.LoadAssetAsync<TextAsset>(pdbName);
                await pdbAssetHandle;
                if (pdbAssetHandle.Status == EOperationStatus.Succeed &&
                    pdbAssetHandle.AssetObject is TextAsset pdbTextAsset)
                {
                    pdbBytes = pdbTextAsset.bytes;
                    Debug.Log($"成功加载关联的PDB文件: {pdbName}");
                }
                else
                {
                    Debug.LogWarning($"未找到或加载PDB文件: {pdbName}，将继续加载DLL（调试信息可能受限）。");
                }
                
                Assembly assembly = null;
                if (pdbBytes != null)
                {
                    // 同时加载DLL和PDB
                    assembly = Assembly.Load(dllBytes, pdbBytes);
                }
                else
                {
                    // 仅加载DLL
                    assembly = Assembly.Load(dllBytes);
                }

                Debug.Log($"成功加载程序集: {assembly.FullName}, 位置: {assembly.Location}");
                // 可以在这里对加载的程序集进行额外检查或初始化
            }
            catch (System.Exception e)
            {
                // 重点捕获并记录加载过程中的任何异常
                Debug.LogError($"加载热更程序集 {dllName} 时发生异常: {e.GetType()}");
                Debug.LogError($"异常信息: {e.Message}");
                Debug.LogError($"调用堆栈: {e.StackTrace}");
                // 根据你的需求决定是继续加载下一个还是抛出异常终止
                // throw; // 如果希望失败就停止，取消这行注释
            }
        }

        Debug.Log("所有热更新程序集加载流程完毕。");
        
        // foreach (var dllName in config.hotUpdateDlls)
        // {
        //     var r = hotUpdatePackage.LoadAssetSync<TextAsset>(dllName);
        //     await r;
        //     if (r.AssetObject is TextAsset ta)
        //     {
        //         var am = Assembly.Load(ta.bytes);
        //         Debug.Log($"加载程序集 => {am?.FullName}");
        //     }
        //     else
        //     {
        //         Debug.LogError($"加载程序集失败 => {dllName}");
        //     }
        // }

      
    }

    //加载补充元数据
    private async UniTask LoadMetadataForAOTAssemblies()
    {
        var aotType = Type.GetType("AOTGenericReferences");
        if (aotType == null)
        {
            Debug.LogWarning("AOTGenericReferences type not found");
            return;
        }
        var listField = aotType.GetField("PatchedAOTAssemblyList", BindingFlags.Public | BindingFlags.Static);
        if (listField == null)
        {
            Debug.LogWarning("PatchedAOTAssemblyList field not found");
            return;
        }
        var aotList = listField.GetValue(null) as System.Collections.Generic.IList<string>;
        if (aotList == null)
        {
            Debug.LogWarning("PatchedAOTAssemblyList is null or not IList<string>");
            return;
        }
        foreach (var aotDllName in aotList)
        {
            var r = hotUpdatePackage.LoadAssetSync<TextAsset>(aotDllName);
            await r;
            if (r.AssetObject is TextAsset ta)
            {
                byte[] dllBytes = ta.bytes;
                int err = (int)RuntimeApi.LoadMetadataForAOTAssembly(dllBytes, HomologousImageMode.SuperSet);
                Debug.Log($"LoadMetadataForAOTAssembly:{aotDllName}. ret:{err}");
            }
        }
    }

    /// <summary>
    /// 从PicturePackage按需下载指定资源
    /// </summary>
    public async UniTask DownloadPictureAsset(string assetName)
    {
        var downloader = picturePackage.CreateBundleDownloader(assetName, 10, 3);
        if (downloader.TotalDownloadCount == 0)
            return;

        downloader.DownloadUpdateCallback = OnDownloadUpdateFunction;
        downloader.DownloadErrorCallback = OnDownloadErrorFunction;
        downloader.BeginDownload();
        await downloader;
    }

    /// <summary>
    /// 从PicturePackage按需下载指定标签的资源
    /// </summary>
    public async UniTask DownloadPictureByTag(params string[] tags)
    {
        await DownLoadByTag(config.picturePackageName, tags);
    }
}