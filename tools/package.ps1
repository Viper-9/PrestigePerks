# 배포 zip 만들기. SPT 설치 폴더에 그대로 풀면 되는 구조로 묶는다.
#   dist\PrestigePerks-<버전>.zip
#     SPT_Runtime/user/mods/PrestigePerks/...   서버 DLL + bundles
#     BepInEx/plugins/PrestigePerks/...         클라 DLL
# 버전은 Directory.Build.props 의 <Version> 하나에서 온다. 로컬 SPT 에는 복사하지 않는다 (그건 deploy-dev.ps1).
# 클라 빌드에 게임 DLL 이 필요하므로 PrestigePerks.local.props 의 SptRoot 가 있어야 한다.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
[xml]$build = Get-Content (Join-Path $root 'Directory.Build.props')
$version = ($build.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw 'Directory.Build.props 에 Version 이 없습니다' }

# 1) 테스트 → Release 빌드
dotnet test (Join-Path $root 'PrestigePerks.slnx')
if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
dotnet build (Join-Path $root 'PrestigePerks.slnx') -c Release
if ($LASTEXITCODE -ne 0) { throw 'build failed' }

$serverOut = Join-Path $root 'server\bin\Release\PrestigePerks'
$clientDll = Join-Path $root 'client\PrestigePerks.Client\bin\Release\netstandard2.1\PrestigePerks.Client.dll'

# 출시본은 전용 모델이 있어야 한다
if (-not (Test-Path (Join-Path $serverOut 'bundles\prestige_coin.bundle'))) { throw 'server\bundles\prestige_coin.bundle 이 없습니다' }
# SPT 가 이미 들고 있는 DLL 이 섞이면 충돌한다
$leaked = Get-ChildItem $serverOut -Filter *.dll | Where-Object Name -ne 'PrestigePerks.dll'
if ($leaked) { throw "빌드 출력에 SPT DLL 이 섞였습니다: $(($leaked | ForEach-Object Name) -join ', ')" }

# 2) zip 내용 모으기
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
$serverStage = Join-Path $stage 'SPT_Runtime\user\mods\PrestigePerks'
$clientStage = Join-Path $stage 'BepInEx\plugins\PrestigePerks'
New-Item -ItemType Directory -Force $serverStage, $clientStage | Out-Null

$exclude = @('*.pdb', '*.deps.json', '*.staticwebassets.*.json', '.gitkeep')
Get-ChildItem $serverOut -Recurse -File | Where-Object {
    $name = $_.Name
    -not ($exclude | Where-Object { $name -like $_ })
} | ForEach-Object {
    $dest = Join-Path $serverStage $_.FullName.Substring($serverOut.Length + 1)
    New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
    Copy-Item $_.FullName $dest
}
Copy-Item $clientDll $clientStage

# 3) zip (항목 경로는 '/' — PowerShell 5.1 Compress-Archive 는 '\' 를 써서 리눅스 도구가 한 파일 이름으로 읽는다)
$zip = Join-Path $dist "PrestigePerks-$version.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, 'Create')
try {
    foreach ($file in Get-ChildItem $stage -Recurse -File) {
        $entry = $file.FullName.Substring($stage.Length + 1).Replace('\', '/')
        [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($archive, $file.FullName, $entry, 'Optimal') | Out-Null
    }
}
finally { $archive.Dispose() }
Remove-Item $stage -Recurse -Force

Write-Host "packaged $zip (version $version)"
