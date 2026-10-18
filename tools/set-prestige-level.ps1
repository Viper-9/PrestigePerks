# PrestigePerks 테스트 도구: SPT 프로필의 프레스티지 단계(0~6)를 바꾼다.
# Tester tool: change the prestige level (0-6) of an SPT profile.
#
#   set-prestige-level.bat 을 더블클릭하거나 PowerShell 에서 실행한다.
#   .\set-prestige-level.ps1                          메뉴로 고른다
#   .\set-prestige-level.ps1 -Name debug -Level 3  바로 바꾼다 (프로필 이름 또는 ID)
#   .\set-prestige-level.ps1 -SptRoot D:\SPT          SPT 폴더를 직접 알려 준다
#
# 바꾸는 값은 프로필의 characters.pmc.Info.PrestigeLevel 하나다. 프레스티지 보상(칭호·도그태그 등)은 주지 않는다.
# SPT 서버가 켜져 있으면 서버가 프로필을 다시 덮어쓰므로 멈춘다.
# 바꾸기 전에 항상 프로필을 백업한다 (profiles\PrestigePerks-backups\). 메뉴의 "되돌리기"로 복원할 수 있다.
param(
    [string]$SptRoot,
    [string]$Name,
    [int]$Level = -1
)

$ErrorActionPreference = 'Stop'
$MaxLevel = 6
$BackupFolderName = 'PrestigePerks-backups'

function Say([string]$ko, [string]$en) { Write-Host "$ko  ($en)" }

# 더블클릭으로 연 창이 바로 닫히지 않게 메뉴 모드에서는 기다린다
$Interactive = -not $Name
function Pause-Exit([int]$code) {
    if ($Interactive) { Read-Host '엔터를 누르면 닫힙니다 (Press Enter to close)' | Out-Null }
    exit $code
}

# ---------------------------------------------------------------- SPT 폴더 찾기
function Find-ProfilesDir([string]$start) {
    $dir = $start
    for ($i = 0; $i -lt 6 -and $dir; $i++) {
        foreach ($candidate in @('SPT_Runtime\user\profiles', 'user\profiles')) {
            $path = Join-Path $dir $candidate
            if (Test-Path $path) { return (Resolve-Path $path).Path }
        }
        $dir = Split-Path $dir -Parent
    }
    return $null
}

$profilesDir = $null
if ($SptRoot) { $profilesDir = Find-ProfilesDir $SptRoot }
if (-not $profilesDir) { $profilesDir = Find-ProfilesDir $PSScriptRoot }
if (-not $profilesDir) { $profilesDir = Find-ProfilesDir (Get-Location).Path }
while (-not $profilesDir) {
    Say 'SPT 폴더를 찾지 못했습니다.' 'SPT folder not found'
    $answer = Read-Host 'SPT 설치 폴더 경로를 입력하세요 (Enter your SPT folder path, e.g. D:\SPT)'
    if (-not $answer) { exit 1 }
    $profilesDir = Find-ProfilesDir $answer.Trim('"')
}
Say "프로필 폴더: $profilesDir" 'profiles folder'

if (Get-Process -Name 'SPT.Server' -ErrorAction SilentlyContinue) {
    Say 'SPT 서버가 켜져 있습니다. 서버를 끄고 다시 실행하세요.' 'Close SPT server first'
    Pause-Exit 1
}

# ---------------------------------------------------------------- 프로필 읽기
$Utf8NoBom = New-Object System.Text.UTF8Encoding($false)

# start 의 '{' 와 짝이 맞는 '}' 위치. 문자열 안의 괄호는 세지 않는다
function Find-ObjectEnd([string]$text, [int]$start) {
    $depth = 0; $inString = $false; $escaped = $false
    for ($i = $start; $i -lt $text.Length; $i++) {
        $c = $text[$i]
        if ($inString) {
            if ($escaped) { $escaped = $false }
            elseif ($c -eq '\') { $escaped = $true }
            elseif ($c -eq '"') { $inString = $false }
            continue
        }
        if ($c -eq '"') { $inString = $true }
        elseif ($c -eq '{') { $depth++ }
        elseif ($c -eq '}') { $depth--; if ($depth -eq 0) { return $i } }
    }
    return -1
}

# characters.pmc.Info 객체의 범위 (시작 '{', 끝 '}'). 캐릭터를 아직 만들지 않았으면 $null
function Find-PmcInfo([string]$text) {
    $pmc = [regex]::Match($text, '"characters"\s*:\s*\{\s*"pmc"\s*:\s*\{')
    if (-not $pmc.Success) { return $null }
    $pmcEnd = Find-ObjectEnd $text ($pmc.Index + $pmc.Length - 1)
    $info = [regex]::new('"Info"\s*:\s*\{').Match($text, $pmc.Index + $pmc.Length)
    if (-not $info.Success -or $info.Index -gt $pmcEnd) { return $null }
    $start = $info.Index + $info.Length - 1
    $end = Find-ObjectEnd $text $start
    if ($end -lt 0) { return $null }
    return @{ Start = $start; End = $end }
}

function Read-ProfileSummary([System.IO.FileInfo]$file) {
    $text = [System.IO.File]::ReadAllText($file.FullName, $Utf8NoBom)
    $username = [regex]::Match($text, '"username"\s*:\s*"([^"]*)"').Groups[1].Value
    $span = Find-PmcInfo $text
    $summary = [pscustomobject]@{ File = $file; Id = $file.BaseName; Username = $username; Nickname = ''; Level = ''; Prestige = $null }
    if ($span) {
        $info = $text.Substring($span.Start, $span.End - $span.Start + 1)
        $summary.Nickname = [regex]::Match($info, '"Nickname"\s*:\s*"([^"]*)"').Groups[1].Value
        $summary.Level = [regex]::Match($info, '"Level"\s*:\s*(\d+)').Groups[1].Value
        $prestige = [regex]::Match($info, '"PrestigeLevel"\s*:\s*(\d+)')
        $summary.Prestige = if ($prestige.Success) { [int]$prestige.Groups[1].Value } else { 0 }
    }
    return $summary
}

function Get-Profiles {
    @(Get-ChildItem $profilesDir -Filter '*.json' -File | Where-Object { $_.Extension -eq '.json' } | ForEach-Object { Read-ProfileSummary $_ })
}

function Show-Profiles($profiles) {
    Write-Host ''
    for ($i = 0; $i -lt $profiles.Count; $i++) {
        $p = $profiles[$i]
        $state = if ($null -eq $p.Prestige) { '캐릭터 없음 (no character yet)' } else { "닉네임 $($p.Nickname) · 레벨 $($p.Level) · 프레스티지 P$($p.Prestige)" }
        Write-Host ("  [{0}] {1}  ({2})  {3}" -f ($i + 1), $p.Username, $p.Id, $state)
    }
    Write-Host ''
}

function Choose-Profile($profiles) {
    if ($Name) {
        $match = $profiles | Where-Object { $_.Username -eq $Name -or $_.Id -eq $Name } | Select-Object -First 1
        if (-not $match) { throw "프로필을 찾지 못했습니다: $Name (profile not found)" }
        return $match
    }
    while ($true) {
        $answer = Read-Host '번호를 고르세요 (Choose a number)'
        $n = 0
        if ([int]::TryParse($answer, [ref]$n) -and $n -ge 1 -and $n -le $profiles.Count) { return $profiles[$n - 1] }
        Say '잘못된 번호입니다.' 'invalid number'
    }
}

# ---------------------------------------------------------------- 백업
function Get-BackupDir { Join-Path $profilesDir $BackupFolderName }

function Backup-Profile($p, [string]$reason) {
    $dir = Get-BackupDir
    New-Item -ItemType Directory -Force $dir | Out-Null
    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $dest = Join-Path $dir "$($p.Id)-$stamp-$reason.json"
    Copy-Item $p.File.FullName $dest
    # 복사본이 원본과 같은지 확인한다. 다르면 고치지 않는다
    if ((Get-FileHash $dest).Hash -ne (Get-FileHash $p.File.FullName).Hash) { throw "백업이 원본과 다릅니다: $dest (backup mismatch)" }
    return $dest
}

# ---------------------------------------------------------------- 바꾸기
function Set-PrestigeLevel($p, [int]$newLevel) {
    $path = $p.File.FullName
    $text = [System.IO.File]::ReadAllText($path, $Utf8NoBom)
    $span = Find-PmcInfo $text
    if (-not $span) { throw '이 프로필에는 PMC 캐릭터가 없습니다. 게임에서 캐릭터를 먼저 만드세요. (create the character in game first)' }

    $info = $text.Substring($span.Start, $span.End - $span.Start + 1)
    $field = [regex]::Match($info, '("PrestigeLevel"\s*:\s*)\d+')
    if ($field.Success) {
        $newInfo = $info.Substring(0, $field.Index) + $field.Groups[1].Value + $newLevel + $info.Substring($field.Index + $field.Length)
    }
    else {
        $newInfo = $info.Substring(0, $info.Length - 1) + ",`"PrestigeLevel`":$newLevel}"
    }

    $backup = Backup-Profile $p "P$($p.Prestige)-before-P$newLevel"
    Say "백업: $backup" 'backup'

    $newText = $text.Substring(0, $span.Start) + $newInfo + $text.Substring($span.End + 1)
    $temp = "$path.tmp"
    [System.IO.File]::WriteAllText($temp, $newText, $Utf8NoBom)
    Move-Item $temp $path -Force

    $check = Read-ProfileSummary (Get-Item $path)
    if ($check.Prestige -ne $newLevel) {
        Copy-Item $backup $path -Force
        throw '바꾼 값을 다시 읽지 못해 백업으로 되돌렸습니다. (verify failed, restored backup)'
    }
    Say "완료: $($p.Username) P$($p.Prestige) → P$newLevel" 'done'
}

function Restore-Profile {
    $dir = Get-BackupDir
    $backups = @(if (Test-Path $dir) { Get-ChildItem $dir -Filter '*.json' -File | Sort-Object LastWriteTime -Descending })
    if (-not $backups) { Say '백업이 없습니다.' 'no backups'; return }
    Write-Host ''
    for ($i = 0; $i -lt $backups.Count; $i++) { Write-Host ("  [{0}] {1}" -f ($i + 1), $backups[$i].Name) }
    Write-Host ''
    $answer = Read-Host '되돌릴 백업 번호 (backup number, Enter = cancel)'
    $n = 0
    if (-not [int]::TryParse($answer, [ref]$n) -or $n -lt 1 -or $n -gt $backups.Count) { Say '취소했습니다.' 'cancelled'; return }
    $backup = $backups[$n - 1]
    $id = $backup.Name.Split('-')[0]
    $target = Join-Path $profilesDir "$id.json"
    if (Test-Path $target) {
        # 지금 상태도 남겨 두고 덮어쓴다
        $current = Backup-Profile (Read-ProfileSummary (Get-Item $target)) 'before-restore'
        Say "지금 상태 백업: $current" 'current state backed up'
    }
    Copy-Item $backup.FullName $target -Force
    Say "되돌렸습니다: $target" 'restored'
}

# ---------------------------------------------------------------- 실행
try {
    $profiles = Get-Profiles
    if (-not $profiles) { Say '프로필이 없습니다. 게임에서 프로필을 먼저 만드세요.' 'no profiles'; Pause-Exit 1 }

    $mode = '1'
    if (-not $Name) {
        Write-Host ''
        Write-Host '  [1] 프레스티지 단계 바꾸기 (change prestige level)'
        Write-Host '  [2] 백업에서 되돌리기 (restore a backup)'
        $mode = Read-Host '고르세요 (Choose)'
    }

    if ($mode -eq '2') {
        Restore-Profile
    }
    else {
        if (-not $Name) { Show-Profiles $profiles }
        $p = Choose-Profile $profiles
        if ($null -eq $p.Prestige) { throw '이 프로필에는 PMC 캐릭터가 없습니다. 게임에서 캐릭터를 먼저 만드세요. (create the character in game first)' }
        while ($Level -lt 0 -or $Level -gt $MaxLevel) {
            $answer = Read-Host "새 프레스티지 단계 0~$MaxLevel (new prestige level, now P$($p.Prestige))"
            $n = 0
            if ([int]::TryParse($answer, [ref]$n)) { $Level = $n }
        }
        Set-PrestigeLevel $p $Level
        Say '서버를 켜고 접속하면 특전 슬롯이 단계에 맞게 채워집니다.' 'start the server; perk slots fill to the new level'
    }
    Pause-Exit 0
}
catch {
    Write-Host ''
    Write-Host "오류 (error): $($_.Exception.Message)" -ForegroundColor Red
    Pause-Exit 1
}
