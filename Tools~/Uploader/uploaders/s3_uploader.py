import os
import shutil
import subprocess
import time
from .base import BaseUploader


class S3Uploader(BaseUploader):
    """AWS S3 上传器 (使用 aws cli)"""

    def __init__(self, endpoint: str, bucket: str, access_key: str = None, secret_key: str = None, **kwargs):
        super().__init__(endpoint, bucket, access_key, secret_key, **kwargs)
        self.s3_bucket = bucket
        print(f"[S3] 桶: {self.s3_bucket}")

    def _s3_uri(self, path: str = "") -> str:
        if path:
            return f"s3://{self.s3_bucket}/{path}"
        return f"s3://{self.s3_bucket}"

    def _run_cmd(self, cmd: str) -> bool:
        print(f"[S3] 执行: {cmd}")
        env = os.environ.copy()
        if self.access_key:
            env["AWS_ACCESS_KEY_ID"] = self.access_key
        if self.secret_key:
            env["AWS_SECRET_ACCESS_KEY"] = self.secret_key
        result = subprocess.run(cmd, shell=True, env=env)
        return result.returncode == 0

    def ensure_bucket(self) -> bool:
        return True

    def download_file(self, remote_path: str, local_path: str) -> bool:
        uri = self._s3_uri(remote_path)
        return self._run_cmd(f'aws s3 cp "{uri}" "{local_path}"')

    def upload_file(self, local_path: str, remote_path: str) -> bool:
        uri = self._s3_uri(remote_path)
        self._run_cmd(f'aws s3 rm "{uri}"')
        for attempt in range(3):
            if self._run_cmd(f'aws s3 cp "{local_path}" "{uri}"'):
                return True
            print(f"[S3] 重试 {attempt + 1}/3...")
            time.sleep(2)
        return False

    def upload_files(self, local_dir: str, remote_prefix: str, files: list, delete_all: bool = False) -> bool:
        temp_dir = os.path.join(local_dir, "_upload_temp")
        if os.path.exists(temp_dir):
            shutil.rmtree(temp_dir)
        os.makedirs(temp_dir)

        try:
            for rel_path in files:
                src = os.path.join(local_dir, rel_path)
                dst = os.path.join(temp_dir, rel_path)
                os.makedirs(os.path.dirname(dst), exist_ok=True)
                shutil.copy2(src, dst)

            uri = self._s3_uri(remote_prefix)

            # 暂时屏蔽删除逻辑
            # if delete_all:
            #     print(f"[S3] 删除整个远程目录: {uri}")
            #     self._run_cmd(f'aws s3 rm "{uri}" --recursive')

            total = len(files)
            print(f"[S3] 开始并行上传: {total} 个文件")
            for attempt in range(3):
                print(f"[S3] sync 尝试 {attempt + 1}/3...")
                if self._run_cmd(f'aws s3 sync "{temp_dir}" "{uri}" --no-progress'):
                    print(f"[S3] 上传成功: {total} 个文件")
                    return True
                time.sleep(2)
            return False
        finally:
            if os.path.exists(temp_dir):
                shutil.rmtree(temp_dir)
