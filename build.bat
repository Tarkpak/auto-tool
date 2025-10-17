@echo off
echo ======================================
echo 输入法语言自动切换器 - 构建脚本
echo ======================================
echo.

echo [1/3] 清理旧的构建文件...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"

echo [2/3] 还原NuGet包...
dotnet restore
if %errorlevel% neq 0 (
    echo 还原失败！
    pause
    exit /b %errorlevel%
)

echo [3/3] 构建项目...
dotnet build -c Release
if %errorlevel% neq 0 (
    echo 构建失败！
    pause
    exit /b %errorlevel%
)

echo.
echo ======================================
echo 构建成功！
echo 可执行文件位置: bin\Release\net8.0-windows\
echo ======================================
pause

