from abc import ABC, abstractmethod


class BaseUploader(ABC):
    """上传器抽象基类"""

    def __init__(self, endpoint: str, bucket: str, access_key: str = None, secret_key: str = None, **kwargs):
        self.endpoint = endpoint
        self.bucket = bucket
        self.access_key = access_key
        self.secret_key = secret_key

    @abstractmethod
    def ensure_bucket(self) -> bool:
        """确保 bucket 存在，不存在则创建"""
        pass

    @abstractmethod
    def download_file(self, remote_path: str, local_path: str) -> bool:
        """下载远程文件到本地，文件不存在返回 False"""
        pass

    @abstractmethod
    def upload_file(self, local_path: str, remote_path: str) -> bool:
        """上传单个文件"""
        pass

    @abstractmethod
    def upload_files(self, local_dir: str, remote_prefix: str, files: list, delete_all: bool = False) -> bool:
        """上传多个文件，files 为相对于 local_dir 的路径列表，delete_all 为 True 时先删除整个远程目录"""
        pass
