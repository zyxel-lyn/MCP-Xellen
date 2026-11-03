[CmdletBinding()]
param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA "MCP Xellen")
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$Project = Join-Path $RepoRoot "src\MCP-Xellen\MCP-Xellen.csproj"

function Require-Command([string]$Name) {
    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name tidak ditemukan. Instal dependensi yang disebutkan di README, lalu jalankan ulang."
    }
}

Write-Host "== MCP Xellen installer ==" -ForegroundColor Cyan
Require-Command "node"
Require-Command "npm"
Require-Command "dotnet"

$nodeVersionText = (& node -p "process.versions.node").Trim()
$nodeVersion = [version]$nodeVersionText
if ($nodeVersion -lt [version]"22.12.0") {
    throw "Node.js 22.12.0 atau lebih baru diperlukan. Versi saat ini: $nodeVersionText"
}

$sdkList = & dotnet --list-sdks
if (-not ($sdkList | Where-Object { $_ -match '^10\.' })) {
    throw ".NET 10 SDK diperlukan untuk membangun MCP Xellen dari source."
}

if (-not (Test-Path $Project)) { throw "Project source tidak ditemukan: $Project" }

New-Item -ItemType Directory -Force -Path $InstallRoot, (Join-Path $InstallRoot "backend"), (Join-Path $InstallRoot "secrets"), (Join-Path $InstallRoot "logs") | Out-Null

Write-Host "[1/4] Installing Desktop Commander and Supergateway..." -ForegroundColor Yellow
$backendDir = Join-Path $InstallRoot "backend"; Copy-Item (Join-Path $RepoRoot "package.json") (Join-Path $backendDir "package.json") -Force; Copy-Item (Join-Path $RepoRoot "package-lock.json") (Join-Path $backendDir "package-lock.json") -Force; npm ci --prefix $backendDir --no-audit --no-fund
if ($LASTEXITCODE -ne 0) { throw "npm ci gagal." }

Write-Host "[2/4] Creating local bearer token..." -ForegroundColor Yellow
$tokenPath = Join-Path $InstallRoot "secrets\mcp-xellen-token.txt"
if (-not (Test-Path $tokenPath)) {
    $bytes = [System.Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
    $token = [Convert]::ToHexString($bytes).ToLowerInvariant()
    [System.IO.File]::WriteAllText($tokenPath, $token, [System.Text.Encoding]::ASCII)
    [Array]::Clear($bytes, 0, $bytes.Length)
}

$configPath = Join-Path $InstallRoot "config.json"
if (-not (Test-Path $configPath)) {
    Copy-Item (Join-Path $RepoRoot "config.example.json") $configPath
}

Write-Host "[3/4] Publishing Windows desktop application..." -ForegroundColor Yellow
$appDir = Join-Path $InstallRoot "app"
New-Item -ItemType Directory -Force -Path $appDir | Out-Null
dotnet publish $Project -c Release -r win-x64 --self-contained false -o $appDir
if ($LASTEXITCODE -ne 0) { throw "dotnet publish gagal." }

Write-Host "[4/4] Creating Desktop shortcut..." -ForegroundColor Yellow
$exe = Join-Path $appDir "MCP-Xellen.exe"
if (-not (Test-Path $exe)) { throw "Executable hasil publish tidak ditemukan." }

try {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut((Join-Path ([Environment]::GetFolderPath("Desktop")) "MCP Xellen.lnk"))
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $InstallRoot
    $shortcut.Description = "MCP Xellen local MCP gateway"
    $shortcut.Save()
} catch {
    Write-Warning "Shortcut tidak dapat dibuat otomatis: $($_.Exception.Message)"
}

Write-Host ""
Write-Host "MCP Xellen berhasil dipasang di: $InstallRoot" -ForegroundColor Green
Write-Host "Token dibuat lokal dan tidak pernah ditulis ke repository."
Write-Host "Edit config.json untuk mengisi publicEndpoint setelah Tailscale Funnel aktif."
Write-Host "Jalankan MCP Xellen melalui shortcut Desktop."
