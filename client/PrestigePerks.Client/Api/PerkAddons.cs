using System;
using EFT;
using PrestigePerks.Game;
using PrestigePerks.Model;

namespace PrestigePerks.Api
{
    /// <summary>
    /// 클라이언트 패치 특전 add-on(B형)용 공개 API. 서버 perks/&lt;add-on&gt;/perks.json 에 "kind": "code" 로 정의한 특전의 효과를
    /// add-on 이 자기 코드(Harmony 패치, 키 입력·화면 같은 새 기능)로 낸다. Has/Scale 은 패치 본문 밖(Update 등)에서도 읽을 수 있다.
    /// 플러그인 Awake 에서 Register 로 칸 번호를 받고 ([BepInDependency("com.viper.prestigeperks")]), 패치 본문은 Has → IsPmc → Scale 순서로 읽는다 (배열 읽기만, 할당 없음).
    /// </summary>
    public static class PerkAddons
    {
        /// <summary>특전 ID(서버 perks.json 의 id)의 칸 번호. 잘못된 ID·겹친 등록이면 -1 (경고 로그). 레이드 밖에서 부른다. 효과는 다음 state 부터 켜진다.</summary>
        public static int Register(string id)
        {
            var slot = PerkPatchSlots.Register(id);
            if (slot < 0)
            {
                Log.Warn($"perk add-on registration rejected: {id}");
            }

            return slot;
        }

        /// <summary>이 칸의 효과가 켜져 있는가 (PMC 의 열린 슬롯에 있고 단계 비율 &gt; 0). 모르는 칸이면 false.</summary>
        public static bool Has(int slot) => PerkEffects.Has(slot);

        /// <summary>효과 비율 (단계 I 0.5, II 0.75, III 1, config tierEffectRatio 따름). 꺼져 있으면 0. III 효과량에 곱한다.</summary>
        public static float Scale(int slot) => PerkEffects.Scale(slot);

        /// <summary>패치에서 예외가 났을 때. 그 특전을 이번 실행 동안 끄고 경고를 한 번 남긴다.</summary>
        public static void Fail(int slot, Exception error) => PerkEffects.Disable(slot, error);

        /// <summary>메인 세션 PMC 인가 (스캐브·봇 제외). 특전은 PMC 전용이다.</summary>
        public static bool IsPmc(Profile profile) => PerkTarget.IsPmc(profile);

        public static bool IsPmc(Player player) => PerkTarget.IsPmc(player);

        public static bool IsPmc(SkillManager skills) => PerkTarget.IsPmc(skills);

        /// <summary>아이템 소유자 ID(InventoryController.ID 등)가 메인 세션 PMC 인가.</summary>
        public static bool IsPmcOwnerId(string ownerId) => PerkTarget.IsPmcOwnerId(ownerId);
    }
}
