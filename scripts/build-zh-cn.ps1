# 简体中文汉化版构建脚本：发布自包含单文件 exe（与官方 MSI 内的发布参数一致）
param(
    [ValidateSet("win-x64", "win-arm64", "win-x86")]
    [string]$Runtime = "win-x64",
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
Set-Location $repoRoot

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "未找到 dotnet 命令。请先安装 .NET 10 SDK：https://dotnet.microsoft.com/download/dotnet/10.0"
}

# 上一次构建出来的程序关闭窗口只是最小化到托盘，进程仍在运行，会锁住 dist 下的 exe。
# 这里先尝试结束“从该目录启动”的实例，再重试删除，避免构建一开始就失败。
function Remove-PublishOutput {
    param(
        [string]$Path,
        [string]$ProductName
    )

    for ($attempt = 1; $attempt -le 2; $attempt++) {
        try {
            Remove-Item $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -ge 2) {
                throw "无法清理 $Path。请先退出正在运行的 $ProductName（托盘图标右键 → 退出）后重试。原始错误：$($_.Exception.Message)"
            }
        }

        $stopped = 0
        foreach ($proc in @(Get-Process -Name $ProductName -ErrorAction SilentlyContinue)) {
            $exePath = $null
            try { $exePath = $proc.Path } catch { }

            # 只结束从本次构建输出目录启动的实例，不动用户从别处复制出去的副本
            if (-not $exePath) { continue }
            if (-not $exePath.StartsWith($Path, [System.StringComparison]::OrdinalIgnoreCase)) { continue }

            Write-Host "检测到 $exePath 仍在运行（PID $($proc.Id)），结束它以便清理..."
            try {
                Stop-Process -Id $proc.Id -Force -ErrorAction Stop
                $stopped++
            }
            catch {
                Write-Warning "结束进程 $($proc.Id) 失败：$($_.Exception.Message)"
            }
        }

        if ($stopped -eq 0) {
            Write-Host "占用进程无法自动结束，等待 1 秒后重试..."
        }

        Start-Sleep -Milliseconds 900
    }
}

$propsPath = Join-Path $repoRoot "Directory.Build.props"
[xml]$propsXml = Get-Content $propsPath
$productName = $propsXml.Project.PropertyGroup.ProductName
if ([string]::IsNullOrWhiteSpace($productName)) {
    throw "在 $propsPath 中未找到 ProductName"
}

$output = Join-Path $repoRoot "dist\$Runtime"
if (Test-Path $output) {
    Write-Host "清理上次的发布目录：$output"
    Remove-PublishOutput -Path $output -ProductName $productName
}

Write-Host "正在发布简体中文版..."
Write-Host "  项目：OpenNetMeter\OpenNetMeter.csproj"
Write-Host "  配置：$Configuration"
Write-Host "  运行时：$Runtime"
Write-Host "  输出：$output"

dotnet publish ".\OpenNetMeter\OpenNetMeter.csproj" `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    --output $output

if ($LASTEXITCODE -ne 0) {
    throw "发布失败，dotnet publish 退出码 $LASTEXITCODE"
}

$exe = Join-Path $output "$productName.exe"
if (-not (Test-Path $exe)) {
    throw "发布完成但未找到预期的可执行文件：$exe"
}

$sizeMb = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "构建完成：$exe（约 $sizeMb MB）"
Write-Host "该 exe 为自包含单文件，目标机器无需预装 .NET 运行时；运行时需要管理员权限。"
