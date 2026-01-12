import os

SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
BUNDLE_ROOT = os.path.normpath(os.path.join(SCRIPT_DIR, "..", "..", "GameShell", "Bundles"))

# 默认配置
PLATFORM = "Android"
MAX_VERSION_COUNT = 5
