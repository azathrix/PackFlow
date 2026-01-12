import os
import shutil
import subprocess
import sys
from concurrent.futures import ThreadPoolExecutor, as_completed
from .base import BaseUploader


class CosUploader(BaseUploader):
    """腾讯云 COS 上传器 (使用 cos-python-sdk-v5)"""

    def __init__(self, endpoint: str, bucket: str, access_key: str = None, secret_key: str = None, **kwargs):
        super().__init__(endpoint, bucket, access_key, secret_key, **kwargs)
        self.region = endpoint  # endpoint 作为 region，如 ap-guangzhou
        self.cos_bucket = bucket  # bucket 格式: bucketname-appid
        self._ensure_sdk()
        self._init_client()
        print(f"[COS] 桶: {self.cos_bucket}, 区域: {self.region}")

    def _ensure_sdk(self):
        """确保 SDK 已安装"""
        try:
            import qcloud_cos
        except ImportError:
            print("[COS] cos-python-sdk-v5 未安装，正在安装...")
            subprocess.run([sys.executable, "-m", "pip", "install", "cos-python-sdk-v5"], check=True)

    def _init_client(self):
        """初始化 COS 客户端"""
        from qcloud_cos import CosConfig, CosS3Client
        config = CosConfig(
            Region=self.region,
            SecretId=self.access_key,
            SecretKey=self.secret_key,
            Scheme='https'
        )
        self.client = CosS3Client(config)

    def ensure_bucket(self) -> bool:
        return True

    def download_file(self, remote_path: str, local_path: str) -> bool:
        try:
            self.client.download_file(Bucket=self.cos_bucket, Key=remote_path, DestFilePath=local_path)
            return True
        except Exception as e:
            # 文件不存在是正常情况，不打印错误
            if 'NoSuchResource' in str(e) or 'NoSuchKey' in str(e):
                return False
            print(f"[COS] 下载失败: {e}")
            return False

    def upload_file(self, local_path: str, remote_path: str) -> bool:
        try:
            self.client.upload_file(Bucket=self.cos_bucket, Key=remote_path, LocalFilePath=local_path)
            print(f"[COS] 上传成功: {remote_path}")
            return True
        except Exception as e:
            print(f"[COS] 上传失败: {e}")
            return False

    def upload_files(self, local_dir: str, remote_prefix: str, files: list, delete_all: bool = False) -> bool:
        total = len(files)
        print(f"[COS] 开始并行上传: {total} 个文件")

        success = 0
        failed = []

        def upload_task(rel_path):
            local_path = os.path.join(local_dir, rel_path)
            remote_path = f"{remote_prefix}/{rel_path}".replace("\\", "/")
            return rel_path, self.upload_file(local_path, remote_path)

        with ThreadPoolExecutor(max_workers=8) as executor:
            futures = {executor.submit(upload_task, f): f for f in files}
            for future in as_completed(futures):
                rel_path, ok = future.result()
                if ok:
                    success += 1
                else:
                    failed.append(rel_path)

        print(f"[COS] 上传完成: {success}/{total}")
        if failed:
            print(f"[COS] 失败文件: {failed}")
        return success == total

    def _delete_prefix(self, prefix: str):
        """删除指定前缀的所有对象"""
        try:
            marker = ""
            while True:
                resp = self.client.list_objects(Bucket=self.cos_bucket, Prefix=prefix, Marker=marker, MaxKeys=1000)
                contents = resp.get('Contents', [])
                if not contents:
                    break
                for obj in contents:
                    self.client.delete_object(Bucket=self.cos_bucket, Key=obj['Key'])
                if resp.get('IsTruncated') == 'false':
                    break
                marker = resp.get('NextMarker', '')
        except Exception as e:
            print(f"[COS] 删除失败: {e}")
