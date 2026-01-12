from .base import BaseUploader


def get_uploader(api_type: str, **kwargs) -> BaseUploader:
    """
    根据 API 类型获取对应的上传器实例（延迟导入）

    Args:
        api_type: "minio", "s3", "cos" 或 "local"
        **kwargs: 传递给上传器的参数 (endpoint, bucket, access_key, secret_key, secure)

    Returns:
        BaseUploader 实例
    """
    api_type = api_type.lower()

    if api_type in ("local", "localhttp", "http"):
        from .local_uploader import LocalUploader
        return LocalUploader(**kwargs)
    elif api_type == "minio":
        from .minio_uploader import MinioUploader
        return MinioUploader(**kwargs)
    elif api_type in ("s3", "aws", "awss3"):
        from .s3_uploader import S3Uploader
        return S3Uploader(**kwargs)
    elif api_type in ("cos", "tencent", "qcloud"):
        from .cos_uploader import CosUploader
        return CosUploader(**kwargs)
    else:
        raise ValueError(f"不支持的 API 类型: {api_type}，支持: minio, s3, cos, local")


__all__ = ["BaseUploader", "get_uploader"]
