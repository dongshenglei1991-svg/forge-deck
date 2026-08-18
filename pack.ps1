#Requires -Version 5.1
<#
.SYNOPSIS
    一键发布 ForgeDeck：先构建前端，再框架依赖 publish 到仓库根目录的 publish/。
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $Root

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Assert-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "未找到命令「$Name」，请先安装并确保已加入 PATH。"
    }
}

Write-Host "ForgeDeck 打包" -ForegroundColor Green
Write-Host "仓库：$Root"

Assert-Command npm
Assert-Command dotnet

$uiDir = Join-Path $Root 'ui'
if (-not (Test-Path (Join-Path $uiDir 'package.json'))) {
    throw "未找到 ui/package.json，当前目录不是仓库根：$Root"
}

if (-not (Test-Path (Join-Path $uiDir 'node_modules'))) {
    Write-Step "安装前端依赖"
    Push-Location $uiDir
    try {
        npm install
        if ($LASTEXITCODE -ne 0) { throw "npm install 失败（退出码 $LASTEXITCODE）" }
    }
    finally {
        Pop-Location
    }
}

Write-Step "构建前端"
Push-Location $uiDir
try {
    npm run build
    if ($LASTEXITCODE -ne 0) { throw "npm run build 失败（退出码 $LASTEXITCODE）" }
}
finally {
    Pop-Location
}

$distIndex = Join-Path $uiDir 'dist\index.html'
if (-not (Test-Path $distIndex)) {
    throw "前端产物缺失：$distIndex。App.csproj 依赖 ui/dist 复制进 wwwroot，顺序不能反。"
}

$publishDir = Join-Path $Root 'publish'
Write-Step "发布桌面壳 → publish\"
if (Test-Path $publishDir) {
    Remove-Item $publishDir -Recurse -Force
}

dotnet publish (Join-Path $Root 'src\ForgeDeck.App\ForgeDeck.App.csproj') `
    -c Release `
    -o $publishDir `
    --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败（退出码 $LASTEXITCODE）" }

$exe = Join-Path $publishDir 'ForgeDeck.App.exe'
if (-not (Test-Path $exe)) {
    throw "发布完成但未找到 $exe"
}

$wwwroot = Join-Path $publishDir 'wwwroot\index.html'
if (-not (Test-Path $wwwroot)) {
    throw "发布完成但未找到 wwwroot\index.html，前端产物可能未打进壳。"
}

Write-Host ""
Write-Host "打包完成（框架依赖，目标机器需已安装 .NET 8 桌面运行时）" -ForegroundColor Green
Write-Host "输出目录：$publishDir"
Write-Host "可执行文件：$exe"
