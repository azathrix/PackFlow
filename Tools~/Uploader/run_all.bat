@echo off
chcp 65001 >nul
python "%~dp0upload.py" MainPackage PicturePackage HotUpdatePackage %*
pause
