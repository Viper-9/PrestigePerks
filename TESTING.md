# PrestigePerks 테스트 안내

테스터용 빌드(`PrestigePerks-<버전>-dev.zip`)를 받은 분을 위한 안내입니다. 개발용 명령과 프로필 도구가 들어 있습니다.

> 개발용 명령은 **테스터용(dev) 빌드에만** 있습니다. 일반 배포 빌드에는 들어가지 않습니다.

## 1. 설치

1. SPT 서버와 게임을 모두 끕니다.
2. zip 을 SPT 설치 폴더(`EscapeFromTarkov.exe` 가 있는 폴더)에 그대로 풉니다.
   - `SPT_Runtime/user/mods/PrestigePerks/` 서버 모드
   - `BepInEx/plugins/PrestigePerks/` 클라이언트 플러그인
   - `PrestigePerks-dev-tools/` 이 안내서와 프로필 도구
3. 필요한 모드: Content Backport - Prestiges 1.1.0 이상, WTT-CommonLib 3.x, WTT-ContentBackport 2.0.x

## 2. 개발용 명령 (Commando 채팅)

게임 메신저에서 **Commando** 친구에게 채팅으로 입력합니다. 결과는 같은 채팅으로 답장이 옵니다.
SPT 설정 `SPT_Data/configs/core.json` 의 `giveCommandEnabled` 가 `true` 여야 합니다 (기본값 true).

| 명령 | 하는 일 |
|---|---|
| `prestige coin [개수]` | 프레스티지 코인을 우편으로 받습니다. 개수 1~1000, 생략하면 1 |
| `prestige perk status` | 특전 상태: 단계, 보유 특전, 슬롯·잠금, 무료/유료 리롤 횟수, 다음 리롤 비용 |
| `prestige perk roll` | 무료 리롤 1회 (무료 횟수가 있을 때만. 유료 리롤은 특전 페이지에서 인벤토리 코인으로) |
| `prestige perk lock <슬롯>` | 슬롯 잠금. 슬롯 번호는 1부터 |
| `prestige perk unlock <슬롯>` | 슬롯 잠금 풀기 |
| `prestige perk free <횟수>` | 남은 무료 리롤 횟수를 정합니다 (0~100) |
| `prestige perk set <슬롯> <특전 번호>` | 슬롯에 원하는 특전을 넣습니다. 다른 슬롯에 같은 특전이 있으면 서로 바꿉니다. 특전 페이지를 다시 열거나 메인 메뉴로 가면 게임에 반영됩니다 |
| `prestige perk reset` | 특전 데이터 초기화: 슬롯·잠금·무료/유료 리롤. 지금 단계로 다시 들어가 슬롯을 새로 뽑고 무료 리롤 3회를 줍니다. 직전 데이터는 `.bak` 으로 남습니다 |
| `help` | Commando 명령 도움말 (영어) |

예시:

```
prestige coin 300
prestige perk free 100
prestige perk lock 2
prestige perk status
```

### 특전 효과 수치 확인

테스터용 빌드는 특전이 반영될 때마다 게임이 계산한 효과를 `BepInEx/LogOutput.log` 에 남깁니다.

```
[dev] bonus calc (input 100): Physical 120, Combat 100, Practical 100, Mental 100, ExperienceRate 110
```

입력 100 에 대한 결과라서 120 이면 1.2배입니다. 예: `prestige perk set 1 2` 후 특전 페이지를 다시 열고 로그에서 해당 그룹 값이 바뀌었는지 확인합니다.

## 3. 프로필 프레스티지 단계 바꾸기

테스트용으로 프로필의 프레스티지 단계(P0~P6)를 바로 바꾸는 도구입니다. 특전 슬롯 수가 단계에 따라 늘어나므로 P1~P6 상태를 빠르게 확인할 때 씁니다.

1. **SPT 서버를 끕니다.** 서버가 켜져 있으면 도구가 멈춥니다 (서버가 프로필을 다시 덮어쓰기 때문).
2. `PrestigePerks-dev-tools/set-prestige-level.bat` 을 더블클릭합니다.
3. `[1] 프레스티지 단계 바꾸기` → 프로필 번호 → 새 단계(0~6)를 고릅니다.
4. 서버와 게임을 켜고 접속하면 특전 슬롯이 새 단계에 맞게 채워지고, 메인 메뉴에 "새 특전" 알림이 뜹니다.

안전장치:

- 바꾸기 전에 **항상 프로필을 백업**합니다: `SPT_Runtime/user/profiles/PrestigePerks-backups/<프로필ID>-<날짜시각>-P<전>-before-P<후>.json`
- 바꾼 뒤 다시 읽어 확인하고, 실패하면 백업으로 되돌립니다.
- 메뉴 `[2] 백업에서 되돌리기` 로 원하는 백업을 고르면 복원됩니다. 복원 직전 상태도 따로 백업합니다.

참고:

- 바뀌는 값은 프로필의 프레스티지 단계 숫자 하나입니다 (`characters.pmc.Info.PrestigeLevel`). 바닐라 프레스티지 보상(칭호, 도그태그 등)은 주지 않습니다.
- 게임에서 캐릭터를 아직 만들지 않은 프로필은 바꿀 수 없습니다. 먼저 접속해 캐릭터를 만드세요.
- SPT 폴더를 못 찾으면 경로를 물어봅니다. PowerShell 에서 직접 실행할 수도 있습니다:

```powershell
.\set-prestige-level.ps1 -Name <프로필 이름 또는 ID> -Level 3
.\set-prestige-level.ps1 -SptRoot D:\SPT
```

## 4. 테스트 예시

1. 새 프로필을 만들고 캐릭터를 만든 뒤 게임을 끕니다.
2. 도구로 P3 로 바꾸고 접속 → 메인 메뉴 "새 특전" 알림, 작업 표시줄의 **특전** 버튼 → 슬롯 3칸 확인
3. `prestige perk free 100` 으로 무료 리롤을 채우고 리롤·잠금·흔들기를 시험
4. `prestige coin 1000` 으로 코인을 받아 우편에서 인벤토리로 옮긴 뒤 유료 리롤 시험
5. 도구로 P6 로 올려 슬롯 6칸과 비용 변화를 확인

## 5. 문제를 알릴 때

아래 파일과 함께, 무엇을 했고 무엇이 이상했는지 적어 주세요. 스크린샷이나 짧은 영상이 있으면 좋습니다.

- 서버 로그: `SPT_Runtime/user/logs/spt/` 의 가장 최근 파일
- 클라이언트 로그: `BepInEx/LogOutput.log`
- 특전 데이터: `SPT_Runtime/user/profileData/<프로필ID>/PrestigePerks.json`
