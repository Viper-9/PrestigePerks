"""개발용: SPT 프로필을 "다음 프레스티지를 바로 할 수 있는 상태"로 만든다.

    python tools/make-prestige-ready.py                      debug 프로필을 P0 으로 되돌리고 P1 조건을 채운다
    python tools/make-prestige-ready.py --target 3           P2 로 두고 P3 조건을 채운다
    python tools/make-prestige-ready.py --name fon --mail-coins 250
    python tools/make-prestige-ready.py --keep-perks         특전 데이터(PrestigePerks.json)를 그대로 둔다
    python tools/make-prestige-ready.py --mail-bitcoins 50   환산 테스트용 비트코인을 메일로 넣는다

하는 일 (조건 값은 배포된 모드의 config/config.json "prestige.stages" 를 읽는다)
  - PrestigeLevel = target - 1, Prestige 기록은 그 아래 단계만 남긴다, pendingPrestige 제거
  - 레벨이 모자라면 올린다 (경험치는 globals.json 경험치 표)
  - New Beginning(target 단계)을 완료 처리. P5·P6 은 Collector 도 완료 처리
  - 스킬·은신처 조건이 모자라면 올린다 (낮추지는 않는다)
  - 루블이 모자라면 모자란 만큼 시스템 메일로 넣는다 (게임에서 메일을 받아 스태시에 넣을 것)
  - --mail-coins N: 받지 않은 메일 속 코인 이월 테스트용으로 코인 N개를 메일로 넣는다
  - --mail-bitcoins N: 전달 창 환산 테스트용으로 비트코인 N개를 메일로 넣는다
  - 특전 데이터를 지운다 (프레스티지 후 무료 리롤·새 슬롯 추첨을 처음부터 보려고). --keep-perks 로 끈다

SPT 서버가 켜져 있으면 멈춘다 (서버가 프로필을 다시 덮어씀). 바꾸기 전에 프로필과 특전 데이터를
profiles/PrestigePerks-backups/ 에 백업한다 (set-prestige-level.ps1 의 "되돌리기"로 프로필을 복원할 수 있다).
특전 데이터 백업은 PrestigePerks-backups/perks/ (되돌릴 때는 user/profileData/<프로필ID>/PrestigePerks.json 으로 직접 복사).
"""

import argparse
import json
import re
import secrets
import shutil
import subprocess
import sys
import time
from pathlib import Path

ELEMENT_IDS = [
    "672df12f97f0469cea52f55e",
    "672df4281ab8d9c8849a0c88",
    "683da91d6f472cfa738c52f2",
    "6842f121000d98ce33b9a60f",
    "68d3ddb4fc101237e601d774",
    "68d3e6f46a7ba36646713fa6",
]
NEW_BEGINNING_QUESTS = [
    "6761f28a022f60bb320f3e95",
    "6761ff17cdc36bd66102e9d0",
    "6848100b00afffa81f09e365",
    "68481881f43abfdda2058369",
    "68d3eabc22a258476b064851",
    "68d3eb2b2712147eab000f21",
]
COLLECTOR_QUEST = "5c51aac186f77432ea65c552"
ROUBLE_TPL = "5449016a4bdc2d6f028b456f"
COIN_TPL = "9fc8dc52dd7dbf55fc27ac09"
BITCOIN_TPL = "59faff1d86f7746c51718c9c"
SYSTEM_DIALOG = "59e7125688a45068a6249071"
ROUBLE_STACK = 500_000
COIN_STACK = 100
QUEST_SUCCESS = 4
SYSTEM_MESSAGE = 7
ONE_YEAR = 31_536_000
BACKUP_FOLDER = "PrestigePerks-backups"
PERK_DATA = "PrestigePerks.json"

REPO = Path(__file__).resolve().parent.parent


def fail(message):
    print(f"error: {message}", file=sys.stderr)
    sys.exit(1)


def spt_root(arg):
    if arg:
        return Path(arg)
    props = REPO / "PrestigePerks.local.props"
    if not props.exists():
        fail("PrestigePerks.local.props 가 없습니다. --spt-root 로 SPT 폴더를 알려 주세요")
    match = re.search(r"<SptRoot>(.*?)</SptRoot>", props.read_text(encoding="utf-8"))
    if not match:
        fail("PrestigePerks.local.props 에 SptRoot 가 없습니다")
    return Path(match.group(1).strip())


def server_running():
    try:
        out = subprocess.run(["tasklist", "/FI", "IMAGENAME eq SPT.Server.exe"], capture_output=True, text=True).stdout
    except OSError:
        return False
    return "SPT.Server.exe" in out


def read_jsonc(path):
    """config.json 은 줄 주석(//)을 쓴다. 문자열 안의 // 는 건드리지 않는다."""
    text = path.read_text(encoding="utf-8")
    text = re.sub(r'("(?:\\.|[^"\\])*")|//[^\n]*', lambda m: m.group(1) or "", text)
    return json.loads(text)


def new_id():
    # MongoId 와 같은 24자리 16진수 (앞 4바이트 = 시각)
    return f"{int(time.time()):08x}{secrets.token_hex(8)}"


def backup(path, backup_dir, profile_id, reason):
    backup_dir.mkdir(parents=True, exist_ok=True)
    stamp = time.strftime("%Y%m%d-%H%M%S")
    dest = backup_dir / f"{profile_id}-{stamp}-{reason}{path.suffix}"
    shutil.copy2(path, dest)
    return dest


def count_tpl(items, tpl):
    return sum((item.get("upd") or {}).get("StackObjectsCount", 1) for item in items if item.get("_tpl") == tpl)


def level_experience(exp_table, level):
    return sum(row["exp"] for row in exp_table[:level])


def complete_quest(quests, qid, now):
    entry = next((q for q in quests if q.get("qid") == qid), None)
    if entry is None:
        entry = {"qid": qid, "startTime": 0, "status": 0, "statusTimers": {}, "completedConditions": [], "availableAfter": 0}
        quests.append(entry)
    before = entry.get("status")
    entry["status"] = QUEST_SUCCESS
    entry.setdefault("statusTimers", {})[str(QUEST_SUCCESS)] = now
    return before


def stacks(total, size):
    while total > 0:
        yield min(total, size)
        total -= size


def mail_items(profile, text, tpl, amount, stack_size, now):
    stash = new_id()
    data = [
        {"_id": new_id(), "_tpl": tpl, "parentId": stash, "slotId": "main", "upd": {"StackObjectsCount": count}}
        for count in stacks(amount, stack_size)
    ]
    dialogues = profile.setdefault("dialogues", {})
    dialogue = dialogues.setdefault(SYSTEM_DIALOG, {"attachmentsNew": 0, "new": 0, "type": SYSTEM_MESSAGE, "pinned": False, "_id": SYSTEM_DIALOG, "messages": []})
    dialogue.setdefault("messages", []).append({
        "_id": new_id(),
        "uid": SYSTEM_DIALOG,
        "type": SYSTEM_MESSAGE,
        "dt": now,
        "text": text,
        "hasRewards": True,
        "rewardCollected": False,
        "items": {"stash": stash, "data": data},
        "maxStorageTime": ONE_YEAR,
    })
    dialogue["new"] = dialogue.get("new", 0) + 1
    dialogue["attachmentsNew"] = dialogue.get("attachmentsNew", 0) + 1


def main():
    parser = argparse.ArgumentParser(description="SPT 프로필을 다음 프레스티지를 바로 할 수 있는 상태로 만든다 (개발용)")
    parser.add_argument("--name", default="debug", help="프로필 이름(username) 또는 ID. 기본 debug")
    parser.add_argument("--target", type=int, default=1, choices=range(1, 7), help="얻을 프레스티지 단계 (1~6). 프로필은 target - 1 단계가 된다. 기본 1")
    parser.add_argument("--mail-coins", type=int, default=0, help="받지 않은 메일로 넣을 코인 수 (이월 테스트)")
    parser.add_argument("--mail-bitcoins", type=int, default=0, help="메일로 넣을 비트코인 수 (전달 창 환산 테스트)")
    parser.add_argument("--keep-perks", action="store_true", help="특전 데이터(PrestigePerks.json)를 지우지 않는다")
    parser.add_argument("--spt-root", help="SPT 설치 폴더. 기본은 PrestigePerks.local.props 의 SptRoot")
    args = parser.parse_args()

    root = spt_root(args.spt_root)
    runtime = root / "SPT_Runtime" if (root / "SPT_Runtime").exists() else root
    profiles_dir = runtime / "user" / "profiles"
    if not profiles_dir.exists():
        fail(f"프로필 폴더가 없습니다: {profiles_dir}")
    if server_running():
        fail("SPT 서버가 켜져 있습니다. 서버를 끄고 다시 실행하세요")

    # 배포된 모드 설정을 먼저 쓴다. 프레스티지 조건이 없는 빌드가 배포돼 있으면 저장소 설정을 쓴다
    config_path = runtime / "user" / "mods" / "PrestigePerks" / "config" / "config.json"
    if not config_path.exists() or "prestige" not in read_jsonc(config_path):
        config_path = REPO / "server" / "config" / "config.json"
    stage = read_jsonc(config_path)["prestige"]["stages"][args.target - 1]
    exp_table = json.loads((runtime / "SPT_Data" / "database" / "globals.json").read_text(encoding="utf-8"))["config"]["exp"]["level"]["exp_table"]

    path = next((p for p in profiles_dir.glob("*.json") if p.stem == args.name), None)
    if path is None:
        for candidate in profiles_dir.glob("*.json"):
            if json.loads(candidate.read_text(encoding="utf-8")).get("info", {}).get("username") == args.name:
                path = candidate
                break
    if path is None:
        fail(f"프로필을 찾지 못했습니다: {args.name}")

    profile = json.loads(path.read_text(encoding="utf-8"))
    pmc = (profile.get("characters") or {}).get("pmc") or {}
    info = pmc.get("Info")
    if not info:
        fail("이 프로필에는 PMC 캐릭터가 없습니다")

    profile_id = path.stem
    now = int(time.time())
    target = args.target
    changes = []

    # 단계
    old_level = info.get("PrestigeLevel", 0)
    info["PrestigeLevel"] = target - 1
    pmc["Prestige"] = {ELEMENT_IDS[i]: now for i in range(target - 1)}
    profile.setdefault("spt", {}).pop("pendingPrestige", None)
    profile.get("info", {})["wipe"] = False
    changes.append(f"prestige P{old_level} -> P{target - 1} (ready for P{target})")

    # 레벨
    need_level = max(stage.get("level") or 0, stage.get("newBeginningLevel") or 0)
    if info.get("Level", 0) < need_level:
        changes.append(f"level {info.get('Level')} -> {need_level}")
        info["Level"] = need_level
        info["Experience"] = max(info.get("Experience", 0), level_experience(exp_table, need_level))

    # 퀘스트
    quests = pmc.setdefault("Quests", [])
    to_complete = [NEW_BEGINNING_QUESTS[target - 1]] + ([COLLECTOR_QUEST] if target >= 5 else [])
    for qid in to_complete:
        before = complete_quest(quests, qid, now)
        changes.append(f"quest {qid} status {before} -> {QUEST_SUCCESS} (Success)")

    # 스킬 (진행도 100 = 1레벨)
    skills = {s["Id"]: s for s in pmc.get("Skills", {}).get("Common", [])}
    for skill, level in (stage.get("skills") or {}).items():
        entry = skills.get(skill)
        if entry is None:
            changes.append(f"skill {skill}: not in profile, skipped")
        elif entry.get("Progress", 0) < level * 100:
            changes.append(f"skill {skill} {entry.get('Progress', 0) / 100:g} -> {level}")
            entry["Progress"] = level * 100

    # 은신처
    areas = {a["type"]: a for a in pmc.get("Hideout", {}).get("Areas", [])}
    for area_type, level in (stage.get("hideout") or {}).items():
        area = areas.get(int(area_type))
        if area is None:
            changes.append(f"hideout area {area_type}: not in profile, skipped")
        elif area.get("level", 0) < level:
            changes.append(f"hideout area {area_type} {area.get('level', 0)} -> {level}")
            area["level"] = level

    # 루블
    items = pmc.get("Inventory", {}).get("items", [])
    roubles = count_tpl(items, ROUBLE_TPL)
    need_roubles = stage.get("roubles") or 0
    if roubles < need_roubles:
        shortfall = need_roubles - roubles
        mail_items(profile, f"DEV: roubles for prestige P{target} ({shortfall})", ROUBLE_TPL, shortfall, ROUBLE_STACK, now)
        changes.append(f"roubles {roubles:,} < {need_roubles:,}: mailed {shortfall:,} (collect the system mail into the stash)")
    else:
        changes.append(f"roubles {roubles:,} >= {need_roubles:,}")

    if args.mail_coins > 0:
        mail_items(profile, f"DEV: unclaimed prestige coins ({args.mail_coins})", COIN_TPL, args.mail_coins, COIN_STACK, now)
        changes.append(f"mailed {args.mail_coins} coins (leave unclaimed to test carry-over)")

    if args.mail_bitcoins > 0:
        mail_items(profile, f"DEV: bitcoins for the transfer window ({args.mail_bitcoins})", BITCOIN_TPL, args.mail_bitcoins, 1, now)
        changes.append(f"mailed {args.mail_bitcoins} bitcoins (collect them, then put them in the transfer window)")

    backup_dir = profiles_dir / BACKUP_FOLDER
    print(f"backup: {backup(path, backup_dir, profile_id, f'P{old_level}-before-ready-P{target}')}")
    temp = path.with_suffix(".json.tmp")
    temp.write_text(json.dumps(profile, ensure_ascii=False, separators=(",", ":")), encoding="utf-8")
    json.loads(temp.read_text(encoding="utf-8"))  # 다시 읽히는지 확인
    temp.replace(path)

    perk_dir = runtime / "user" / "profileData" / profile_id
    if not args.keep_perks:
        for name in (PERK_DATA, PERK_DATA + ".bak"):
            perk_file = perk_dir / name
            if perk_file.exists():
                # 프로필 백업과 섞이면 set-prestige-level.ps1 의 되돌리기 목록에 뜨므로 하위 폴더에 둔다
                print(f"backup: {backup(perk_file, backup_dir / 'perks', profile_id, name.replace('.', '_'))}")
                perk_file.unlink()
                changes.append(f"removed {perk_file.name}")

    print(f"profile {profile.get('info', {}).get('username')} ({profile_id}), conditions from {config_path}")
    for change in changes:
        print(f"  - {change}")
    coins = count_tpl(items, COIN_TPL) + count_tpl(((profile.get("characters") or {}).get("scav") or {}).get("Inventory", {}).get("items", []), COIN_TPL)
    print(f"coins now in PMC + scav inventory: {coins} (mail not counted)")


if __name__ == "__main__":
    main()
