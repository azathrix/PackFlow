#!/usr/bin/env python3
"""
PackFlow 本地 HTTP 服务器
支持文件上传、下载、删除和 Web 管理界面
"""

import os
import sys
import json
import argparse
from http.server import HTTPServer, BaseHTTPRequestHandler
from urllib.parse import unquote, urlparse, parse_qs
import shutil

# Web 管理页面 HTML
MANAGER_HTML = """<!DOCTYPE html>
<html>
<head>
    <meta charset="UTF-8">
    <title>PackFlow 资源管理</title>
    <style>
        * { box-sizing: border-box; margin: 0; padding: 0; }
        body { font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; background: #1a1a2e; color: #eee; padding: 20px; }
        h1 { color: #00d4ff; margin-bottom: 20px; }
        .path { background: #16213e; padding: 10px 15px; border-radius: 6px; margin-bottom: 20px; word-break: break-all; }
        .path a { color: #00d4ff; text-decoration: none; }
        .path a:hover { text-decoration: underline; }
        .toolbar { margin-bottom: 15px; display: flex; gap: 10px; flex-wrap: wrap; }
        .btn { background: #0f3460; color: #fff; border: none; padding: 8px 16px; border-radius: 4px; cursor: pointer; }
        .btn:hover { background: #00d4ff; color: #1a1a2e; }
        .btn-danger { background: #e94560; }
        .btn-danger:hover { background: #ff6b6b; }
        table { width: 100%; border-collapse: collapse; background: #16213e; border-radius: 6px; overflow: hidden; }
        th, td { padding: 12px 15px; text-align: left; border-bottom: 1px solid #0f3460; }
        th { background: #0f3460; color: #00d4ff; }
        tr:hover { background: #1f4068; }
        .icon { margin-right: 8px; }
        a { color: #00d4ff; text-decoration: none; }
        a:hover { text-decoration: underline; }
        .size { color: #888; }
        .upload-form { background: #16213e; padding: 20px; border-radius: 6px; margin-bottom: 20px; }
        .upload-form input[type="file"] { margin-right: 10px; }
        .empty { text-align: center; padding: 40px; color: #666; }
        .checkbox { width: 20px; }
    </style>
</head>
<body>
    <h1>PackFlow 资源管理</h1>
    <div class="path" id="breadcrumb"></div>
    <div class="upload-form">
        <input type="file" id="fileInput" multiple>
        <button class="btn" onclick="uploadFiles()">上传文件</button>
        <input type="file" id="folderInput" webkitdirectory multiple style="margin-left: 20px;">
        <button class="btn" onclick="uploadFolder()">上传文件夹</button>
    </div>
    <div class="toolbar">
        <button class="btn" onclick="createFolder()">新建文件夹</button>
        <button class="btn btn-danger" onclick="deleteSelected()">删除选中</button>
        <button class="btn" onclick="refresh()">刷新</button>
    </div>
    <table>
        <thead><tr><th class="checkbox"><input type="checkbox" id="selectAll" onchange="toggleAll()"></th><th>名称</th><th>大小</th><th>操作</th></tr></thead>
        <tbody id="fileList"></tbody>
    </table>
    <script>
        let currentPath = '';

        function formatSize(bytes) {
            if (bytes === 0) return '-';
            const k = 1024;
            const sizes = ['B', 'KB', 'MB', 'GB'];
            const i = Math.floor(Math.log(bytes) / Math.log(k));
            return parseFloat((bytes / Math.pow(k, i)).toFixed(1)) + ' ' + sizes[i];
        }

        function updateBreadcrumb() {
            const parts = currentPath.split('/').filter(p => p);
            let html = '<a href="#" onclick="navigate(\\'\\')">根目录</a>';
            let path = '';
            for (const part of parts) {
                path += '/' + part;
                const p = path;
                html += ' / <a href="#" onclick="navigate(\\'' + p + '\\')">' + part + '</a>';
            }
            document.getElementById('breadcrumb').innerHTML = html;
        }

        function navigate(path) {
            currentPath = path;
            loadFiles();
        }

        function loadFiles() {
            updateBreadcrumb();
            fetch('/list' + currentPath)
                .then(r => r.json())
                .then(data => {
                    const tbody = document.getElementById('fileList');
                    if (!data.items || data.items.length === 0) {
                        tbody.innerHTML = '<tr><td colspan="4" class="empty">空目录</td></tr>';
                        return;
                    }
                    data.items.sort((a, b) => (b.isDir - a.isDir) || a.name.localeCompare(b.name));
                    tbody.innerHTML = data.items.map(item => {
                        const icon = item.isDir ? '📁' : '📄';
                        const path = currentPath + '/' + item.name;
                        const nameHtml = item.isDir
                            ? '<a href="#" onclick="navigate(\\'' + path + '\\')">' + icon + ' ' + item.name + '</a>'
                            : '<a href="' + path + '" target="_blank">' + icon + ' ' + item.name + '</a>';
                        return '<tr><td class="checkbox"><input type="checkbox" value="' + item.name + '"></td><td>' + nameHtml + '</td><td class="size">' + formatSize(item.size) + '</td><td><a href="#" onclick="deleteItem(\\'' + item.name + '\\')">删除</a></td></tr>';
                    }).join('');
                });
        }

        function uploadFiles() {
            const files = document.getElementById('fileInput').files;
            if (files.length === 0) return alert('请选择文件');
            let uploaded = 0;
            for (const file of files) {
                const path = currentPath + '/' + file.name;
                fetch('/upload' + path, { method: 'PUT', body: file })
                    .then(() => { if (++uploaded === files.length) { loadFiles(); document.getElementById('fileInput').value = ''; } });
            }
        }

        function uploadFolder() {
            const files = document.getElementById('folderInput').files;
            if (files.length === 0) return alert('请选择文件夹');
            let uploaded = 0;
            for (const file of files) {
                const path = currentPath + '/' + file.webkitRelativePath;
                fetch('/upload' + path, { method: 'PUT', body: file })
                    .then(() => { if (++uploaded === files.length) { loadFiles(); document.getElementById('folderInput').value = ''; } });
            }
        }

        function createFolder() {
            const name = prompt('文件夹名称:');
            if (!name) return;
            fetch('/mkdir' + currentPath + '/' + name, { method: 'POST' })
                .then(() => loadFiles());
        }

        function deleteItem(name) {
            if (!confirm('确定删除 ' + name + '?')) return;
            fetch(currentPath + '/' + name, { method: 'DELETE' })
                .then(() => loadFiles());
        }

        function deleteSelected() {
            const checkboxes = document.querySelectorAll('#fileList input[type="checkbox"]:checked');
            if (checkboxes.length === 0) return alert('请选择要删除的项目');
            if (!confirm('确定删除 ' + checkboxes.length + ' 个项目?')) return;
            let deleted = 0;
            checkboxes.forEach(cb => {
                fetch(currentPath + '/' + cb.value, { method: 'DELETE' })
                    .then(() => { if (++deleted === checkboxes.length) loadFiles(); });
            });
        }

        function toggleAll() {
            const checked = document.getElementById('selectAll').checked;
            document.querySelectorAll('#fileList input[type="checkbox"]').forEach(cb => cb.checked = checked);
        }

        function refresh() { loadFiles(); }

        loadFiles();
    </script>
</body>
</html>"""


class LocalServerHandler(BaseHTTPRequestHandler):
    root_dir = "."

    def log_message(self, format, *args):
        print(f"[{self.log_date_time_string()}] {format % args}", flush=True)

    def send_json(self, data, status=200):
        body = json.dumps(data, ensure_ascii=False).encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "application/json; charset=utf-8")
        self.send_header("Content-Length", len(body))
        self.send_header("Access-Control-Allow-Origin", "*")
        self.end_headers()
        self.wfile.write(body)

    def send_html(self, html, status=200):
        body = html.encode("utf-8")
        self.send_response(status)
        self.send_header("Content-Type", "text/html; charset=utf-8")
        self.send_header("Content-Length", len(body))
        self.end_headers()
        self.wfile.write(body)

    def get_local_path(self, url_path):
        path = unquote(urlparse(url_path).path).lstrip("/")
        return os.path.normpath(os.path.join(self.root_dir, path))

    def do_OPTIONS(self):
        self.send_response(200)
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, PUT, POST, DELETE, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.end_headers()

    def do_GET(self):
        path = self.path

        # 管理页面
        if path == "/" or path == "":
            self.send_html(MANAGER_HTML)
            return

        # 列出目录
        if path.startswith("/list"):
            dir_path = path[5:] if len(path) > 5 else "/"
            self.handle_list(dir_path)
            return

        # 下载文件
        local_path = self.get_local_path(path)
        if not local_path.startswith(os.path.normpath(self.root_dir)):
            self.send_json({"error": "Access denied"}, 403)
            return

        if not os.path.exists(local_path):
            self.send_json({"error": "Not found"}, 404)
            return

        if os.path.isdir(local_path):
            self.handle_list(path)
            return

        try:
            with open(local_path, "rb") as f:
                data = f.read()
            self.send_response(200)
            self.send_header("Content-Length", len(data))
            self.send_header("Access-Control-Allow-Origin", "*")
            self.end_headers()
            self.wfile.write(data)
        except Exception as e:
            self.send_json({"error": str(e)}, 500)

    def do_PUT(self):
        path = self.path

        # 上传文件 - 支持 /upload/{path} 和直接 /{path}
        if path.startswith("/upload/"):
            rel_path = path[8:]
        else:
            rel_path = path.lstrip("/")

        local_path = self.get_local_path(rel_path)

        if not local_path.startswith(os.path.normpath(self.root_dir)):
            self.send_json({"error": "Access denied"}, 403)
            return

        try:
            os.makedirs(os.path.dirname(local_path), exist_ok=True)
            content_length = int(self.headers.get("Content-Length", 0))
            data = self.rfile.read(content_length)
            with open(local_path, "wb") as f:
                f.write(data)
            self.send_json({"success": True, "path": rel_path})
        except Exception as e:
            self.send_json({"error": str(e)}, 500)

    def do_POST(self):
        path = self.path

        # 创建文件夹
        if path.startswith("/mkdir/"):
            rel_path = path[7:]
            local_path = self.get_local_path(rel_path)

            if not local_path.startswith(os.path.normpath(self.root_dir)):
                self.send_json({"error": "Access denied"}, 403)
                return

            try:
                os.makedirs(local_path, exist_ok=True)
                self.send_json({"success": True})
            except Exception as e:
                self.send_json({"error": str(e)}, 500)
            return

        # 其他 POST 当作上传处理
        self.do_PUT()

    def do_DELETE(self):
        local_path = self.get_local_path(self.path)

        if not local_path.startswith(os.path.normpath(self.root_dir)):
            self.send_json({"error": "Access denied"}, 403)
            return

        if not os.path.exists(local_path):
            self.send_json({"error": "Not found"}, 404)
            return

        try:
            if os.path.isdir(local_path):
                shutil.rmtree(local_path)
            else:
                os.remove(local_path)
            self.send_json({"success": True})
        except Exception as e:
            self.send_json({"error": str(e)}, 500)

    def handle_list(self, path):
        local_path = self.get_local_path(path)

        if not local_path.startswith(os.path.normpath(self.root_dir)):
            self.send_json({"error": "Access denied"}, 403)
            return

        if not os.path.exists(local_path):
            os.makedirs(local_path, exist_ok=True)

        if not os.path.isdir(local_path):
            self.send_json({"error": "Not a directory"}, 400)
            return

        try:
            items = []
            for name in os.listdir(local_path):
                full_path = os.path.join(local_path, name)
                items.append({
                    "name": name,
                    "isDir": os.path.isdir(full_path),
                    "size": os.path.getsize(full_path) if os.path.isfile(full_path) else 0
                })
            self.send_json({"items": items})
        except Exception as e:
            self.send_json({"error": str(e)}, 500)


def main():
    parser = argparse.ArgumentParser(description="PackFlow Local HTTP Server")
    parser.add_argument("-p", "--port", type=int, default=8080, help="Port")
    parser.add_argument("-r", "--root", type=str, default=".", help="Root directory")
    parser.add_argument("-l", "--log", type=str, default="", help="Log file path")
    args = parser.parse_args()

    # 设置日志输出
    if args.log:
        log_file = open(args.log, "w", encoding="utf-8", buffering=1)
        sys.stdout = log_file
        sys.stderr = log_file

    LocalServerHandler.root_dir = os.path.abspath(args.root)

    if not os.path.exists(LocalServerHandler.root_dir):
        os.makedirs(LocalServerHandler.root_dir)

    server = HTTPServer(("0.0.0.0", args.port), LocalServerHandler)
    print(f"Server started at http://localhost:{args.port}/", flush=True)
    print(f"Root directory: {LocalServerHandler.root_dir}", flush=True)

    try:
        server.serve_forever()
    except KeyboardInterrupt:
        print("\nServer stopped.", flush=True)
        server.shutdown()


if __name__ == "__main__":
    main()
