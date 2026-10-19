# 개발용 배포: 서버는 Dev(개발용 명령 포함), 클라는 Release 로 빌드해서 로컬 SPT 에 바로 복사한다.
#   서버 → <SptRoot>\SPT_Runtime\user\mods\PrestigePerks (폴더를 통째로 교체)
#   클라 → <SptRoot>\BepInEx\plugins\PrestigePerks
# SPT 경로는 PrestigePerks.local.props 의 SptRoot 를 쓴다. 서버·게임이 켜져 있으면 DLL 이 잠겨 있어 멈춘다.
#
#   .\tools\deploy-dev.ps1               서버 + 클라
#   .\tools\deploy-dev.ps1 -ServerOnly   서버만 (게임을 켜 둔 채 서버만 다시 올릴 때)
#   .\tools\deploy-dev.ps1 -Beta         베타 임시 규칙(슬롯 6개, 리롤 무제한)을 켠 서버
param([switch]$ServerOnly, [switch]$Beta)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$propsPath = Join-Path $root 'PrestigePerks.local.props'
if (-not (Test-Path $propsPath)) { throw "PrestigePerks.local.props 없음: PrestigePerks.local.props.example 을 복사해 SptRoot 를 정하세요" }
[xml]$props = Get-Content $propsPath
$spt = $props.Project.PropertyGroup.SptRoot
if (-not $spt -or -not (Test-Path $spt)) { throw "SptRoot 가 없거나 잘못됨: $spt" }

$running = @(Get-Process -Name 'SPT.Server' -ErrorAction SilentlyContinue)
if (-not $ServerOnly) { $running += @(Get-Process -Name 'EscapeFromTarkov' -ErrorAction SilentlyContinue) }
if ($running) { throw "먼저 종료하세요: $(($running | ForEach-Object ProcessName | Sort-Object -Unique) -join ', ')" }

# ---------------------------------------------------------------- server
dotnet build (Join-Path $root 'server\PrestigePerks.Server.csproj') -c Dev -p:Beta=$(if ($Beta) { 'true' } else { 'false' })
if ($LASTEXITCODE -ne 0) { throw 'server build failed' }
$serverOut = Join-Path $root 'server\bin\Dev\PrestigePerks'
$serverDest = Join-Path $spt 'SPT_Runtime\user\mods\PrestigePerks'

# SPT 가 이미 들고 있는 DLL 이 섞이면 충돌한다
$leaked = Get-ChildItem $serverOut -Filter *.dll | Where-Object Name -ne 'PrestigePerks.dll'
if ($leaked) { throw "빌드 출력에 SPT DLL 이 섞였습니다: $(($leaked | ForEach-Object Name) -join ', ')" }

if (Test-Path $serverDest) { Remove-Item $serverDest -Recurse -Force }
$exclude = @('*.pdb', '*.deps.json', '*.staticwebassets.*.json', '.gitkeep')
Get-ChildItem $serverOut -Recurse -File | Where-Object {
    $name = $_.Name
    -not ($exclude | Where-Object { $name -like $_ })
} | ForEach-Object {
    $dest = Join-Path $serverDest $_.FullName.Substring($serverOut.Length + 1)
    New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
    Copy-Item $_.FullName $dest
}
if (-not (Test-Path (Join-Path $serverDest 'bundles\prestige_coin.bundle'))) {
    Write-Warning 'prestige_coin.bundle 없음: 코인이 TarCoin 모델로 보입니다 (server\bundles 에 넣고 다시 배포)'
}
Write-Host "server deployed to $serverDest"

# ---------------------------------------------------------------- client
if (-not $ServerOnly) {
    dotnet build (Join-Path $root 'client\PrestigePerks.Client\PrestigePerks.Client.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'client build failed' }
    $clientDest = Join-Path $spt 'BepInEx\plugins\PrestigePerks'
    New-Item -ItemType Directory -Force $clientDest | Out-Null
    Copy-Item (Join-Path $root 'client\PrestigePerks.Client\bin\Release\netstandard2.1\PrestigePerks.Client.dll') $clientDest -Force
    Write-Host "client deployed to $clientDest"
}
