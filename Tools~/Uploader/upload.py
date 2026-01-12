#!/usr/bin/env python3
"""
统一上传工具 - 支持 MinIO 和 AWS S3
用法: python upload.py <packages...> [options]
"""
import argparse
import hashlib
import json
import os
import re
import shutil
import tempfile
from datetime import datetime

import config
from uploaders import get_uploader

_config = {
    "api_type": "minio",
    "upload_endpoint": "",
    "download_endpoint": "",
    "bucket": "",
    "project_id": "",
    "version": "",
    "access_key": "",
    "secret_key": "",
    "platform": config.PLATFORM,
    "max_versions": config.MAX_VERSION_COUNT,
    "bundle_root": config.BUNDLE_ROOT,
}


def calc_md5(file_path: str) -> str:
    """计算文件 MD5"""
    md5 = hashlib.md5()
    with open(file_path, "rb") as f:
        while chunk := f.read(8192):
            md5.update(chunk)
    return md5.hexdigest()


def find_all_version_dirs(package_dir: str) -> list:
    """查找所有版本目录，按日期和分钟数排序"""
    pattern = re.compile(r"^(\d{4}-\d{2}-\d{2})-(\d+)$")
    dirs = []
    if not os.path.exists(package_dir):
        return dirs
    for name in os.listdir(package_dir):
        match = pattern.match(name)
        if match and os.path.isdir(os.path.join(package_dir, name)):
            dirs.append((name, match.group(1), int(match.group(2))))
    # 按日期和分钟数排序
    dirs.sort(key=lambda x: (x[1], x[2]))
    return [d[0] for d in dirs]


def find_latest_version_dir(package_dir: str) -> str:
    """查找最新版本目录"""
    dirs = find_all_version_dirs(package_dir)
    return dirs[-1] if dirs else None


def generate_version_file(version_dir: str) -> dict:
    """生成 version.json，包含所有文件的 MD5"""
    version_file = os.path.join(version_dir, "version.json")

    # 如果已存在则直接读取
    if os.path.exists(version_file):
        with open(version_file, "r", encoding="utf-8") as f:
            return json.load(f)

    # 生成新的 version.json
    files = {}
    for root, _, filenames in os.walk(version_dir):
        for filename in filenames:
            if filename == "version.json":
                continue
            file_path = os.path.join(root, filename)
            rel_path = os.path.relpath(file_path, version_dir).replace("\\", "/")
            files[rel_path] = calc_md5(file_path)

    version_data = {"files": files, "timestamp": datetime.now().isoformat()}

    with open(version_file, "w", encoding="utf-8") as f:
        json.dump(version_data, f, indent=2, ensure_ascii=False)

    print(f"生成 version.json: {len(files)} 个文件")
    return version_data


def get_remote_prefix(package_name: str) -> str:
    """构建远程路径前缀: project_id/platform/version/package_name"""
    parts = []
    if _config["project_id"]:
        parts.append(_config["project_id"])
    parts.append(_config["platform"])
    if _config["version"]:
        parts.append(_config["version"])
    parts.append(package_name)
    return "/".join(parts)


def clean_old_versions(package_dir: str):
    """清理旧版本目录"""
    dirs = find_all_version_dirs(package_dir)
    max_count = _config["max_versions"]
    if len(dirs) <= max_count:
        return

    to_delete = dirs[:-max_count]
    for dir_name in to_delete:
        dir_path = os.path.join(package_dir, dir_name)
        print(f"删除旧版本: {dir_name}")
        shutil.rmtree(dir_path)


def upload_package(package_name: str) -> bool:
    """上传单个包"""
    print(f"\n{'='*50}")
    print(f"处理包: {package_name}")
    print(f"{'='*50}")

    # 定位本地目录
    package_dir = os.path.join(_config["bundle_root"], _config["platform"], package_name)
    if not os.path.exists(package_dir):
        print(f"错误: 目录不存在 {package_dir}")
        return False

    version_dir_name = find_latest_version_dir(package_dir)
    if not version_dir_name:
        print(f"错误: 未找到版本目录")
        return False

    version_dir = os.path.join(package_dir, version_dir_name)
    print(f"版本目录: {version_dir_name}")

    # 生成本地 version.json
    local_version = generate_version_file(version_dir)

    # 获取上传器
    uploader = get_uploader(
        _config["api_type"],
        endpoint=_config["upload_endpoint"],
        bucket=_config["bucket"],
        access_key=_config["access_key"],
        secret_key=_config["secret_key"]
    )

    # 确保 bucket 存在
    if not uploader.ensure_bucket():
        return False

    # 下载远程 version.json
    remote_prefix = get_remote_prefix(package_name)
    remote_version_path = f"{remote_prefix}/version.json"

    print(f"Bucket: {_config['bucket']}")
    temp_file = tempfile.NamedTemporaryFile(delete=False, suffix=".json")
    temp_file.close()

    remote_version = None
    if uploader.download_file(remote_version_path, temp_file.name):
        try:
            with open(temp_file.name, "r", encoding="utf-8") as f:
                remote_version = json.load(f)
            print("已获取远程版本信息，将进行增量上传")
        except:
            pass
    else:
        print("远程无版本信息，将进行全量上传")
    os.unlink(temp_file.name)

    # 对比 MD5，找出需要上传的文件
    local_files = local_version.get("files", {})
    remote_files = remote_version.get("files", {}) if remote_version else {}

    changed_files = []
    for rel_path, md5 in local_files.items():
        if rel_path not in remote_files or remote_files[rel_path] != md5:
            changed_files.append(rel_path)

    if not changed_files:
        print("没有文件需要上传")
        clean_old_versions(package_dir)
        return True

    print(f"需要上传 {len(changed_files)} 个文件 (共 {len(local_files)} 个)")

    print(f"上传服务器: {_config['bucket']}/{remote_prefix}")
    # 上传文件，没有远程version.json时删除整个目录
    delete_all = remote_version is None
    if not uploader.upload_files(version_dir, remote_prefix, changed_files, delete_all):
        print("文件上传失败")
        return False

    # 上传 version.json
    version_file_path = os.path.join(version_dir, "version.json")
    if not uploader.upload_file(version_file_path, remote_version_path):
        print("version.json 上传失败")
        return False

    print(f"上传完成: {package_name}")
    clean_old_versions(package_dir)
    return True


def main():
    parser = argparse.ArgumentParser(description="统一上传工具")
    parser.add_argument("packages", nargs="+", help="要上传的包名")
    parser.add_argument("--api-type", default="minio", help="API 类型: minio, s3, cos")
    parser.add_argument("--upload-endpoint", help="上传服务器地址")
    parser.add_argument("--download-endpoint", help="下载服务器地址")
    parser.add_argument("--bucket", help="Bucket 名称")
    parser.add_argument("--project-id", help="项目ID (8位哈希)")
    parser.add_argument("--version", help="版本号子目录")
    parser.add_argument("--access-key", help="Access Key")
    parser.add_argument("--secret-key", help="Secret Key")
    parser.add_argument("--platform", default=config.PLATFORM, help="平台名称")
    parser.add_argument("--max-versions", type=int, default=config.MAX_VERSION_COUNT, help="保留版本数")
    parser.add_argument("--bundle-root", help="Bundle根目录路径")

    args = parser.parse_args()

    # 更新配置
    _config["api_type"] = args.api_type
    _config["platform"] = args.platform
    _config["max_versions"] = args.max_versions

    if args.upload_endpoint:
        _config["upload_endpoint"] = args.upload_endpoint
    if args.download_endpoint:
        _config["download_endpoint"] = args.download_endpoint
    if args.bucket:
        _config["bucket"] = args.bucket
    if args.project_id:
        _config["project_id"] = args.project_id
    if args.version:
        _config["version"] = args.version
    if args.access_key:
        _config["access_key"] = args.access_key
    if args.secret_key:
        _config["secret_key"] = args.secret_key
    if args.bundle_root:
        _config["bundle_root"] = args.bundle_root

    # 验证必要参数
    if not _config["upload_endpoint"]:
        print("错误: 必须指定 --upload-endpoint")
        return 1

    # 非 LocalHttp 需要 bucket
    is_local = _config["api_type"] in ("local", "localhttp", "http")
    if not is_local and not _config["bucket"]:
        print("错误: 必须指定 --bucket")
        return 1

    print(f"API 类型: {_config['api_type']}")
    print(f"上传服务器: {_config['upload_endpoint']}")
    print(f"下载服务器: {_config['download_endpoint']}")
    print(f"Bucket: {_config['bucket']}")
    if _config["project_id"]:
        print(f"项目ID: {_config['project_id']}")
    print(f"平台: {_config['platform']}")
    if _config["version"]:
        print(f"版本: {_config['version']}")

    # 上传所有包
    success = True
    for package in args.packages:
        if not upload_package(package):
            success = False

    print()
    if success:
        print("=" * 50)
        print("所有资源上传完成!")
        print("=" * 50)
    else:
        print("=" * 50)
        print("上传过程中出现错误!")
        print("=" * 50)

    input("\n按回车键关闭窗口...")
    return 0 if success else 1


if __name__ == "__main__":
    exit(main())
