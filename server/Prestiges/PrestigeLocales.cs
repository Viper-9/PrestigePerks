namespace PrestigePerks.Prestiges;

/// <summary>
/// 프레스티지 전달 창 문구 (dev-docs 3_프레스티지_코인.md 6절). 바닐라는 "아이템을 넘긴다" 문구라 코인 환산·자동 이월로 바꾼다.
/// kr 도 원래 영어 원문이라 en·kr 둘 다 덮어쓴다.
/// </summary>
public static class PrestigeLocales
{
    public const string TransferHeader = "Prestige/ItemsTransfer/Header";
    public const string TransferSubTitle = "ObtainPrestigeScreen/Transfer/SubTitle";
    public const string ConfirmationText = "Prestige/Conformation/Text";

    public static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        ["en"] = new()
        {
            [TransferHeader] = "Items placed here become prestige coins",
            [TransferSubTitle] = "Coin conversion",
            [ConfirmationText] = "Upon gaining Prestige, you will start the game from the beginning.\nTransferred items become prestige coins (capped per level), and the coins you own carry over automatically",
        },
        ["kr"] = new()
        {
            [TransferHeader] = "여기 넣은 아이템은 프레스티지 코인으로 환산됩니다",
            [TransferSubTitle] = "코인 환산",
            [ConfirmationText] = "프레스티지를 얻으면 처음부터 다시 시작합니다.\n전달한 아이템은 프레스티지 코인으로 환산되고(단계별 상한), 가진 코인은 자동으로 넘어갑니다",
        },
    };
}
