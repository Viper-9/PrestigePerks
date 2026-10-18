using EFT;

namespace PrestigePerks.Game
{
    /// <summary>
    /// 패치 특전 적용 대상 판정: 소유 프로필이 메인 세션 PMC 프로필과 같은가 (dev-docs 2_특전.md 6절 "적용 대상: PMC 전용").
    /// 스캐브 레이드(ProfileOfPet)와 봇은 다른 프로필 객체라 빠진다. 레이드 뒤 프로필 객체가 바뀔 수 있어 매번 세션에서 읽는다.
    /// </summary>
    internal static class PerkTarget
    {
        public static bool IsPmc(Profile profile) => profile != null && ReferenceEquals(profile, PerkGame.Profile);

        public static bool IsPmc(Player player) => player != null && IsPmc(player.Profile);

        /// <summary>
        /// 아이템 소유자(InventoryController.ID)가 PMC 인가. InventoryController 생성자가 profile.ProfileId 를 ID 로 넘긴다.
        /// 스캐브 레이드 프로필(ProfileOfPet)과 봇은 ProfileId 가 다르다.
        /// </summary>
        public static bool IsPmcOwnerId(string ownerId)
        {
            if (ownerId == null)
            {
                return false;
            }

            var profile = PerkGame.Profile;
            return profile != null && string.Equals(ownerId, profile.ProfileId, System.StringComparison.Ordinal);
        }

        public static bool IsPmc(SkillManager skills)
        {
            if (skills == null)
            {
                return false;
            }

            var profile = PerkGame.Profile;
            return profile != null && ReferenceEquals(profile.Skills, skills);
        }
    }
}
