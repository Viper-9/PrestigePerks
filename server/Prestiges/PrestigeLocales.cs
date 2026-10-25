namespace PrestigePerks.Prestiges;

/// <summary>
/// 프레스티지 전달 창 문구 (dev-docs 3_프레스티지_코인.md 6절). 바닐라는 "아이템을 넘긴다" 문구라 코인 환산·자동 이월로 바꾼다.
/// kr 은 프레스티지 화면 문구 상당수가 영어 원문 그대로라 번역도 같이 넣는다.
/// </summary>
public static class PrestigeLocales
{
    public const string TransferHeader = "Prestige/ItemsTransfer/Header";
    public const string TransferSubTitle = "ObtainPrestigeScreen/Transfer/SubTitle";
    public const string ConfirmationText = "Prestige/Conformation/Text";
    public const string TransferWarning = "TransferItemsMessage/Description";

    // 프레스티지 확인 창의 "유지(STAYS)" 목록. 끝에 특전 도감을 더한다 (산 특전과 단계는 모든 프레스티지에서 남는다). 앞 줄은 바닐라 문구 그대로
    public const string StaysList = "Prestige/Conformation/Save/EFT{S}{M}";

    public static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        ["en"] = new()
        {
            [TransferHeader] = "Converts to prestige coins",
            [TransferSubTitle] = "Coin conversion",
            [ConfirmationText] = "Upon gaining Prestige, you will start the game from the beginning.\nTransferred items become prestige coins (capped per level), and the coins you own carry over automatically",
            [TransferWarning] = "Items left in the stash will be lost. Prestige coins you own carry over automatically.",
            [StaysList] = "• {S}% SKILLS \n• {M}% MASTERING\n• EARNED ACHIEVEMENTS\n• PROFILE STATISTICS\n• PRESTIGE PROGRESS\n• PERK COLLECTION",
        },
        ["kr"] = new()
        {
            [TransferHeader] = "프레스티지 코인으로 환산됩니다",
            [TransferSubTitle] = "코인 환산",
            [ConfirmationText] = "프레스티지를 얻으면 처음부터 다시 시작합니다.\n전달한 아이템은 프레스티지 코인으로 환산되고(단계별 상한), 가진 코인은 자동으로 넘어갑니다",
            [TransferWarning] = "창고에 남긴 아이템은 사라집니다. 가진 프레스티지 코인은 자동으로 넘어갑니다.",

            // kr 언어 파일에 번역이 없는 프레스티지 문구 (2026-10-23 확인)
            ["Prestige/Conformation/Title"] = "축하합니다! 프레스티지 조건을 모두 달성했습니다!",
            ["ObtainPrestigeScreen/Conformation/SubTitle"] = "기본 정보",
            ["Prestige/Conformation/Save"] = "유지",
            ["Prestige/Conformation/Lost"] = "초기화",
            [StaysList] = "• 스킬 {S}%\n• 숙련도 {M}%\n• 획득한 업적\n• 프로필 통계\n• 프레스티지 진행도\n• 특전 도감",
            ["Prestige/Conformation/Save/Arena{S}{M}"] = "• 스킬 {S}%\n• 숙련도 {M}%\n• 획득한 업적\n• ARP 등급\n• 무기고 진행도\n• 프로필 통계",
            ["Prestige/ItemsTransfer/FitButtonTooltip"] = "넣을 수 있는 아이템 보기",
            ["Prestige/ItemsTransfer/FitWindow"] = "넣을 수 있는 아이템 전체",
            ["Prestige/Levels/AlreadyClaimed"] = "<color=#919191>획득 완료</color>",
            ["Prestige/Levels/Claim"] = "획득",
            ["Prestige/Levels/ConditionsListCaption"] = "달성 조건",
            ["Prestige/Levels/ConditionsNotReady"] = "조건 미달성",
            ["Prestige/Levels/NotAvailableInRaid"] = "레이드 중에는 불가",
            ["Prestige/Levels/PreviousNotClaimed"] = "이전 단계 미획득",
            ["Prestige/Levels/RewardOverviewCaption"] = "보상 미리보기",
            ["Prestige/Levels/RewardsListCaption"] = "프레스티지 획득 보상",
            ["Prestige/Rewards/ModesCaption"] = "적용 게임 모드:",
            ["Prestige/Rewards/RewardDescription"] = "설명:",
            ["PrestigeTooltip{0}"] = "프레스티지 레벨: {0}",
            ["ClothingPanel/PrestigeLevel"] = "프레스티지\n레벨:",
            ["UI/Prestige/NonEmptyContainerError"] = "아이템이 들어 있는 컨테이너는 넣을 수 없습니다.",
        },
    };
}
