using System;

namespace Azathrix.PackFlow
{
    /// <summary>
    /// 上传 API 类型
    /// </summary>
    public enum UploadApiType
    {
        MinIO,
        AwsS3,
        TencentCOS,
        LocalHttp
    }

    /// <summary>
    /// 上传配置
    /// </summary>
    [Serializable]
    public class UploadConfig
    {
        public UploadApiType apiType = UploadApiType.LocalHttp;
        public string endpoint = "http://127.0.0.1:8080";
        public string bucket = "bundles";
        public string accessKey;
        public string secretKey;
        public string projectId;
        public string version;
        public string platform;
        public int maxVersions = 5;
    }
}
