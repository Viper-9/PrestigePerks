# 배포 zip 만들기. SPT 설치 폴더에 그대로 풀면 되는 구조로 묶는다.
#   .\tools\package.ps1        상용: 서버 Release (개발용 명령 없음). 명령 코드가 섞여 있으면 멈춘다
#   .\tools\package.ps1 -Dev   테스터용: 서버 Dev (개발용 명령 포함). zip 이름 끝에 -dev
#   dist\<ModName>-<버전>.zip
#     SPT_Runtime/user/mods/<ModName>/...   서버 모드 (DLL + Data, bundles 등)
#     BepInEx/plugins/<ModName>/...         클라 플러그인 DLL
# 버전은 $VersionProps 의 <Version> 하나에서 온다. 로컬 SPT 에는 복사하지 않는다 (그건 deploy-dev.ps1).
# 클라 빌드에 게임 DLL 이 필요하면 SptRoot 가 잡혀 있어야 한다 (이 저장소는 PrestigePerks.local.props).
#
# 다른 모드에 쓸 때는 아래 설정 블록만 고친다. 경로는 모두 저장소 루트 기준.
param([switch]$Dev)
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

# 클라 플러그인 폴더에 그대로 넣을 폴더 (언어 파일 en·kr. 사용자가 다른 언어 파일을 더한다). 없으면 @{}
$ClientFolders = @{ 'client\PrestigePerks.Client\Locales' = 'Locales' }

# 클라 DLL 에 리소스로 넣는 그림 (특전 페이지). 폴더의 PNG 마다 DLL 에 '<앞부분><파일 이름>' 리소스가 있어야 한다. 없으면 @{}
$ClientResources = @{
    'client\PrestigePerks.Client\Assets\Icons'   = 'PrestigePerks.Icons.'
    'client\PrestigePerks.Client\Assets\Sprites' = 'PrestigePerks.Sprites.'
}

# 상용 빌드 서버 DLL 에 있으면 안 되는 형식 이름 (개발용 명령)
$DevOnlyTypes = @('PrestigeCommandoCommand')

# 서버 모드 폴더와 클라 플러그인 폴더에 둘 다 넣을 라이선스 파일 (MIT 는 배포본에 문구를 넣어야 한다). 없으면 @()
$LicenseFiles = @('LICENSE.md', 'THIRD_PARTY_NOTICES.md')

# zip 에서 뺄 파일
$Exclude = @('*.pdb', '*.deps.json', '*.staticwebassets.*.json', '.gitkeep')

# zip 에 들어가면 안 되는 경로 조각 (예제 add-on 은 저장소에만 둔다, dev-docs 95). 있으면 멈춘다
$Forbidden = @('examples', 'perks\example', 'PrestigePerks.ExampleAddon')
# ---------------------------------------------------------------- 설정 끝

$root = Split-Path $PSScriptRoot -Parent
if (-not $ServerOut -and -not $ClientDlls) { throw '서버($ServerOut)와 클라($ClientDlls)가 둘 다 비어 있습니다' }
$licensePaths = @($LicenseFiles | ForEach-Object { Join-Path $root $_ })
foreach ($file in $licensePaths) {
    if (-not (Test-Path $file)) { throw "라이선스 파일이 없습니다: $file" }
}

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
if ($Dev) {
    dotnet build (Join-Path $root $ServerProject) -c Dev
    if ($LASTEXITCODE -ne 0) { throw 'dev server build failed' }
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
if ($ClientResources.Count -gt 0) {
    # 그림은 파일로 따로 배포하지 않고 DLL 에 넣는다. 빠진 그림이 없는지 리소스 이름으로 확인한다
    $clientText = [System.Text.Encoding]::ASCII.GetString([System.IO.File]::ReadAllBytes($clientPaths[0]))
    foreach ($folder in $ClientResources.Keys) {
        foreach ($png in Get-ChildItem (Join-Path $root $folder) -Filter *.png) {
            $name = $ClientResources[$folder] + $png.Name
            if (-not $clientText.Contains($name)) { throw "클라 DLL 에 그림 리소스가 없습니다: $name" }
        }
    }
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
    if ($licensePaths) { Copy-Item $licensePaths $serverStage }
}
if ($clientPaths) {
    $clientStage = Join-Path $stage "BepInEx\plugins\$ModName"
    New-Item -ItemType Directory -Force $clientStage | Out-Null
    Copy-Item $clientPaths $clientStage
    if ($licensePaths) { Copy-Item $licensePaths $clientStage }
    foreach ($folder in $ClientFolders.Keys) {
        $folderStage = Join-Path $clientStage $ClientFolders[$folder]
        New-Item -ItemType Directory -Force $folderStage | Out-Null
        Copy-Item (Join-Path (Join-Path $root $folder) '*') $folderStage
    }
}

foreach ($item in Get-ChildItem $stage -Recurse) {
    $relative = $item.FullName.Substring($stage.Length + 1)
    foreach ($part in $Forbidden) {
        if ($relative.IndexOf($part, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { throw "zip 에 들어가면 안 되는 파일이 있습니다: $relative" }
    }
}

# 4) zip (항목 경로는 '/' — PowerShell 5.1 Compress-Archive 는 '\' 를 써서 리눅스 도구가 한 파일 이름으로 읽는다)
$zip = Join-Path $dist "$ModName-$version$(if ($Dev) { '-dev' }).zip"
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
