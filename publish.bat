@echo off
echo ======================================
echo 输入法语言自动切换器 - 发布脚本
echo ======================================
echo.

echo [1/4] 清理旧的构建文件...
if exist "bin" rmdir /s /q "bin"
if exist "obj" rmdir /s /q "obj"
if exist "publish" rmdir /s /q "publish"

echo [2/4] 还原NuGet包...
dotnet restore
if %errorlevel% neq 0 (
    echo 还原失败！
    pause
    exit /b %errorlevel%
)

echo [3/4] 发布项目（自包含单文件模式）...
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish
if %errorlevel% neq 0 (
    echo 发布失败！
    pause
    exit /b %errorlevel%
)

echo [4/4] 清理不必要的文件...
cd publish
for %%f in (*) do (
    if not "%%~xf"==".exe" (
        if not "%%~xf"==".dll" (
            del "%%f" 2>nul
        )
    )
)
cd ..

echo.
echo ======================================
echo 发布成功！
echo.
echo 分发文件位置: publish\
echo 包含以下文件：
echo   - auto-lang.exe （主程序，自包含运行时）
echo   - assets\ （图标资源文件夹，必须一起分发）
echo.
echo ⚠️ 重要提示：
echo 分发时请将整个 publish 文件夹打包给用户
echo 用户无需安装 .NET Runtime 即可运行
echo ======================================
echo.
pause

