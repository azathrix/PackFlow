import os
import requests
from .base import BaseUploader


class LocalUploader(BaseUploader):
    """本地 HTTP 服务器上传器"""

    def __init__(self, endpoint: str, bucket: str = "", **kwargs):
        super().__init__(endpoint, bucket, **kwargs)
        # 本地服务器不需要认证
        self.base_url = endpoint.rstrip("/")

    def ensure_bucket(self) -> bool:
        """本地服务器不需要创建 bucket"""
        return True

    def download_file(self, remote_path: str, local_path: str) -> bool:
        """下载文件"""
        try:
            url = f"{self.base_url}/{remote_path}"
            response = requests.get(url, timeout=3)
            if response.status_code == 200:
                os.makedirs(os.path.dirname(local_path), exist_ok=True)
                with open(local_path, "wb") as f:
                    f.write(response.content)
                return True
            return False
        except Exception as e:
            print(f"下载失败: {e}")
            return False

    def upload_file(self, local_path: str, remote_path: str) -> bool:
        """上传单个文件"""
        try:
            url = f"{self.base_url}/{remote_path}"
            file_size = os.path.getsize(local_path)
            print(f"  上传: {os.path.basename(local_path)} ({file_size} bytes) -> {url}")
            with open(local_path, "rb") as f:
                response = requests.put(url, data=f, timeout=30)
            if response.status_code == 200:
                print(f"  ✓ {os.path.basename(local_path)} ({file_size} bytes)")
                return True
            else:
                print(f"  ✗ {os.path.basename(local_path)} - HTTP {response.status_code}")
                return False
        except Exception as e:
            print(f"  ✗ {os.path.basename(local_path)} - {e}")
            return False

    def upload_files(self, local_dir: str, remote_prefix: str, files: list, delete_all: bool = False) -> bool:
        """上传多个文件"""
        if delete_all:
            # 删除远程目录
            try:
                url = f"{self.base_url}/{remote_prefix}"
                requests.delete(url, timeout=30)
                print(f"已清理远程目录: {remote_prefix}")
            except:
                pass

        print(f"开始上传 {len(files)} 个文件...")
        success = True
        uploaded = 0
        for file in files:
            local_path = os.path.join(local_dir, file)
            remote_path = f"{remote_prefix}/{file}".replace("\\", "/")
            if self.upload_file(local_path, remote_path):
                uploaded += 1
            else:
                success = False

        print(f"上传完成: {uploaded}/{len(files)} 个文件")
        return success

    def delete_file(self, remote_path: str) -> bool:
        """删除文件"""
        try:
            url = f"{self.base_url}/{remote_path}"
            response = requests.delete(url, timeout=30)
            return response.status_code == 200
        except Exception as e:
            print(f"删除失败: {e}")
            return False

    def list_files(self, remote_prefix: str) -> list:
        """列出目录下的文件"""
        try:
            url = f"{self.base_url}/list/{remote_prefix}"
            response = requests.get(url, timeout=3)
            if response.status_code == 200:
                data = response.json()
                return data.get("files", [])
            return []
        except Exception as e:
            print(f"列出文件失败: {e}")
            return []
