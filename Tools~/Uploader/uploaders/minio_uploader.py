import os
import time
import tempfile
from .base import BaseUploader

try:
    from minio import Minio
    from minio.error import S3Error
except ImportError:
    import subprocess
    import sys
    subprocess.check_call([sys.executable, "-m", "pip", "install", "minio"])
    from minio import Minio
    from minio.error import S3Error


class MinioUploader(BaseUploader):
    """MinIO 上传器实现"""

    def __init__(self, endpoint: str, bucket: str, access_key: str, secret_key: str, **kwargs):
        # 从 endpoint 解析协议和地址
        if endpoint.startswith("https://"):
            self.secure = True
            endpoint = endpoint[8:]
        elif endpoint.startswith("http://"):
            self.secure = False
            endpoint = endpoint[7:]
        else:
            self.secure = False
        super().__init__(endpoint, bucket, access_key, secret_key, **kwargs)
        self._client = None

    @property
    def client(self) -> Minio:
        if self._client is None:
            self._client = Minio(
                self.endpoint,
                access_key=self.access_key,
                secret_key=self.secret_key,
                secure=self.secure
            )
        return self._client

    def ensure_bucket(self) -> bool:
        try:
            if not self.client.bucket_exists(self.bucket):
                self.client.make_bucket(self.bucket)
                print(f"创建 bucket: {self.bucket}")
            return True
        except S3Error as e:
            print(f"Bucket 操作失败: {e}")
            return False

    def download_file(self, remote_path: str, local_path: str) -> bool:
        try:
            self.client.fget_object(self.bucket, remote_path, local_path)
            return True
        except S3Error as e:
            if e.code == "NoSuchKey":
                return False
            print(f"下载失败 {remote_path}: {e}")
            return False

    def upload_file(self, local_path: str, remote_path: str) -> bool:
        retry_count = 0
        while True:
            try:
                self.client.fput_object(self.bucket, remote_path, local_path)
                return True
            except S3Error as e:
                retry_count += 1
                print(f"上传失败 {remote_path} (重试 {retry_count}): {e}")
                time.sleep(2)

    def delete_prefix(self, prefix: str) -> bool:
        """删除指定前缀下的所有对象"""
        try:
            objects = self.client.list_objects(self.bucket, prefix=prefix, recursive=True)
            for obj in objects:
                self.client.remove_object(self.bucket, obj.object_name)
                print(f"删除: {obj.object_name}")
            return True
        except S3Error as e:
            print(f"删除失败: {e}")
            return False

    def upload_files(self, local_dir: str, remote_prefix: str, files: list, delete_all: bool = False) -> bool:
        # 暂时屏蔽删除逻辑
        # if delete_all:
        #     print(f"删除远程目录: {remote_prefix}")
        #     self.delete_prefix(remote_prefix)

        total = len(files)
        for i, rel_path in enumerate(files, 1):
            local_path = os.path.join(local_dir, rel_path)
            remote_path = f"{remote_prefix}/{rel_path}".replace("\\", "/")
            print(f"[{i}/{total}] 上传: {rel_path}")
            if not self.upload_file(local_path, remote_path):
                return False
        return True
