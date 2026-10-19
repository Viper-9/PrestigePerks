# 배포 zip 만들기. SPT 설치 폴더에 그대로 풀면 되는 구조로 묶는다.
#   .\tools\package.ps1        상용: 서버 Release (개발용 명령 없음). 명령 코드가 섞여 있으면 멈춘다
#   .\tools\package.ps1 -Dev   테스터용: 서버 Dev (개발용 명령 포함) + 테스터 도구(PrestigePerks-dev-tools/). zip 이름 끝에 -dev
#   .\tools\package.ps1 -Beta  베타: 서버에 임시 규칙(슬롯 6개, 리롤 무제한, server\Perks\PerkBeta.cs)을 켠다. zip 이름 끝에 -beta
#   dist\<ModName>-<버전>.zip
#     SPT_Runtime/user/mods/<ModName>/...   서버 모드 (DLL + Data, bundles 등)
#     BepInEx/plugins/<ModName>/...         클라 플러그인 DLL
# 버전은 $VersionProps 의 <Version> 하나에서 온다. 로컬 SPT 에는 복사하지 않는다 (그건 deploy-dev.ps1).
# 클라 빌드에 게임 DLL 이 필요하면 SptRoot 가 잡혀 있어야 한다 (이 저장소는 PrestigePerks.local.props).
#
# 다른 모드에 쓸 때는 아래 설정 블록만 고친다. 경로는 모두 저장소 루트 기준.
param([switch]$Dev, [switch]$Beta)
$ErrorActionPreference = 'Stop'

# ---------------------------------------------------------------- 설정
$ModName      = 'PrestigePerks'                       # zip 이름이자 설치 폴더 이름
$Solution     = 'PrestigePerks.slnx'                  # 테스트·빌드할 솔루션 (.sln / .slnx)
$RunTests     = $true                                 # 테스트 프로젝트가 없으면 $false
$VersionProps = 'Directory.Build.props'               # <Version> 을 읽을 파일

# 서버 모드. 서버 모드가 없으면 $ServerOut = $null
$ServerProject     = 'server\PrestigePerks.Server.csproj'
$ServerOut         = if ($Dev) { 'server\bin\Dev\PrestigePerks' } else { 'server\bin\Release\PrestigePerks' }   # 빌드 출력 폴더
$ServerAllowedDlls = @('PrestigePerks.dll')                       # 출력에 있어도 되는 DLL (SPT 가 이미 들고 있는 DLL 이 섞이면 충돌한다)
$ServerRequired    = @('bundles\prestige_coin.bundle')            # 출력에 반드시 있어야 하는 파일 (없으면 @())

# 클라 플러그인. 클라가 없으면 @()
$ClientDlls = @('client\PrestigePerks.Client\bin\Release\netstandard2.1\PrestigePerks.Client.dll')

# 상용 빌드 서버 DLL 에 있으면 안 되는 형식 이름 (개발용 명령)
$DevOnlyTypes = @('PrestigeCommandoCommand')

# 테스터용(-Dev) zip 에 넣을 도구. zip 의 <ModName>-dev-tools/ 에 들어간다
$DevTools = @('tools\set-prestige-level.ps1', 'tools\set-prestige-level.bat', 'TESTING.md')

# zip 에서 뺄 파일
$Exclude = @('*.pdb', '*.deps.json', '*.staticwebassets.*.json', '.gitkeep')
# ---------------------------------------------------------------- 설정 끝

$root = Split-Path $PSScriptRoot -Parent
if (-not $ServerOut -and -not $ClientDlls) { throw '서버($ServerOut)와 클라($ClientDlls)가 둘 다 비어 있습니다' }

[xml]$build = Get-Content (Join-Path $root $VersionProps)
$version = ($build.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { throw "$VersionProps 에 Version 이 없습니다" }

# 1) 테스트 → Release 빌드
$slnPath = Join-Path $root $Solution
if ($RunTests) {
    dotnet test $slnPath
    if ($LASTEXITCODE -ne 0) { throw 'tests failed' }
}
dotnet build $slnPath -c Release
if ($LASTEXITCODE -ne 0) { throw 'build failed' }
if ($Dev -or $Beta) {
    # 서버만 다시 빌드한다 (-Dev: 개발용 명령, -Beta: 베타 임시 규칙)
    dotnet build (Join-Path $root $ServerProject) -c $(if ($Dev) { 'Dev' } else { 'Release' }) -p:Beta=$(if ($Beta) { 'true' } else { 'false' })
    if ($LASTEXITCODE -ne 0) { throw 'server build failed' }
}

# 2) 빌드 결과 검사
if ($ServerOut) {
    $serverOutPath = Join-Path $root $ServerOut
    if (-not (Test-Path $serverOutPath)) { throw "서버 빌드 출력이 없습니다: $ServerOut" }
    foreach ($file in $ServerRequired) {
        if (-not (Test-Path (Join-Path $serverOutPath $file))) { throw "서버 출력에 $file 이 없습니다" }
    }
    $leaked = Get-ChildItem $serverOutPath -Filter *.dll | Where-Object { $ServerAllowedDlls -notcontains $_.Name }
    if ($leaked) { throw "빌드 출력에 허용되지 않은 DLL 이 섞였습니다: $(($leaked | ForEach-Object Name) -join ', ')" }
    if (-not $Dev) {
        # 상용 빌드에 개발용 명령이 들어가지 않았는지 DLL 메타데이터 이름으로 확인한다
        foreach ($dll in Get-ChildItem $serverOutPath -Filter *.dll) {
            $text = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($dll.FullName))
            foreach ($type in $DevOnlyTypes) {
                if ($text.Contains($type)) { throw "상용 빌드 $($dll.Name) 에 개발용 형식 $type 이 들어 있습니다" }
            }
        }
    }
}
$clientPaths = @($ClientDlls | ForEach-Object { Join-Path $root $_ })
foreach ($dll in $clientPaths) {
    if (-not (Test-Path $dll)) { throw "클라 DLL 이 없습니다: $dll" }
}

# 3) zip 내용 모으기
$dist = Join-Path $root 'dist'
$stage = Join-Path $dist 'stage'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

if ($ServerOut) {
    $serverStage = Join-Path $stage "SPT_Runtime\user\mods\$ModName"
    Get-ChildItem $serverOutPath -Recurse -File | Where-Object {
        $name = $_.Name
        -not ($Exclude | Where-Object { $name -like $_ })
    } | ForEach-Object {
        $dest = Join-Path $serverStage $_.FullName.Substring($serverOutPath.Length + 1)
        New-Item -ItemType Directory -Force (Split-Path $dest -Parent) | Out-Null
        Copy-Item $_.FullName $dest
    }
}
if ($clientPaths) {
    $clientStage = Join-Path $stage "BepInEx\plugins\$ModName"
    New-Item -ItemType Directory -Force $clientStage | Out-Null
    Copy-Item $clientPaths $clientStage
}

if ($Dev) {
    $toolsStage = Join-Path $stage "$ModName-dev-tools"
    New-Item -ItemType Directory -Force $toolsStage | Out-Null
    foreach ($tool in $DevTools) { Copy-Item (Join-Path $root $tool) $toolsStage }
}

# 4) zip (항목 경로는 '/' — PowerShell 5.1 Compress-Archive 는 '\' 를 써서 리눅스 도구가 한 파일 이름으로 읽는다)
$zip = Join-Path $dist "$ModName-$version$(if ($Dev) { '-dev' })$(if ($Beta) { '-beta' }).zip"
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
