using PrestigePerks.Logging;
using PrestigePerks.Perks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Services.Modding.Custom;

namespace PrestigePerks.Items;

/// <summary>
/// 전용 에셋(SPT_Prestige_Coin_Assets)으로 만든 프레스티지 코인 아이템.
/// TarCoin을 복제하므로 WTT-ContentBackport(Preload + 2) 뒤에 돈다.
/// 번들이 없으면(개발 중) TarCoin 모델을 그대로 써서 게임에서 깨지지 않게 한다. 출시 zip 은 번들이 있어야 만들어진다.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public class PrestigeCoinItem(
    ISptLogger<PerksFileLogger> logger,
    CustomItemService customItemService,
    ModHelper modHelper,
    PerkConfigLoader configLoader) : IOnLoad
{
    /// <summary>프레스티지 코인 템플릿 ID. 한 번 정하면 바꾸지 않는다 (플레이어 프로필에 저장됨).</summary>
    public const string TemplateId = "9fc8dc52dd7dbf55fc27ac09";

    /// <summary>bundles.json 의 key 이자 아이템 Prefab.path. WTT SDK 에서 빌드한 번들 이름과 같아야 한다.</summary>
    public const string BundleKey = "prestige_coin.bundle";

    /// <summary>WTT-ContentBackport 의 TarCoin. 복제 원본.</summary>
    public const string TarCoinTemplateId = "69dd0de23dfe95d9e70b5ebb";

    private const string MoneyParentId = "543be5dd4bdc2deb348b4569";
    private const string HandbookValuablesId = "5b47574386f77428ca22b2f1";

    /// <summary>1코인 = 100,000₽ (기획서 3장 환산 단가).</summary>
    internal const double PriceRoubles = 100_000;

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var bundlePath = System.IO.Path.Combine(modHelper.GetAbsolutePathToModFolder(typeof(PrestigeCoinItem).Assembly), "bundles", BundleKey);
        var bundleAvailable = File.Exists(bundlePath);

        var result = customItemService.CreateItemFromClone(BuildDetails(bundleAvailable, configLoader.SafeCoins));
        if (result.Success != true)
        {
            logger.Error($"[PrestigePerks] 프레스티지 코인 생성 실패: {string.Join(", ", result.Errors ?? [])}");
            return Task.CompletedTask;
        }

        if (bundleAvailable)
        {
            logger.Info($"[PrestigePerks] 프레스티지 코인 등록 ({TemplateId}, 전용 모델)");
        }
        else
        {
            logger.Warning($"[PrestigePerks] 프레스티지 코인 등록 ({TemplateId}). bundles/{BundleKey} 가 없어 TarCoin 모델로 표시합니다");
        }

        return Task.CompletedTask;
    }

    internal static NewItemFromCloneDetails BuildDetails(bool bundleAvailable, SafeCoinConfig? safeCoins = null) =>
        new()
        {
            ItemTplToClone = TarCoinTemplateId,
            ParentId = MoneyParentId,
            NewId = TemplateId,
            NewItemName = "item_prestige_coin",
            OverrideProperties = new TemplateItemProperties
            {
                // null 이면 복제 원본(TarCoin)의 Prefab 이 그대로 남는다
                Prefab = bundleAvailable ? new Prefab { Path = BundleKey, Rcid = "" } : null,
                BackgroundColor = "green",
                // 기획서 3장: 쌓이고, 보험·판매로 루블이 되지 않게 한다
                StackMaxSize = 100,
                InsuranceDisabled = true,
                IsUnsaleable = true,
                CanSellOnRagfair = false,
                // 금고에서 나올 때 스택 수 (화폐는 맵 루트 생성 때 이 범위에서 고른다, PrestigeCoinLoot)
                StackMinRandom = (safeCoins ?? new SafeCoinConfig()).MinStack,
                StackMaxRandom = (safeCoins ?? new SafeCoinConfig()).MaxStack,
            },
            HandbookParentId = HandbookValuablesId,
            HandbookPriceRoubles = PriceRoubles,
            FleaPriceRoubles = PriceRoubles,
            AddToWeaponShelf = false,
            Locales = new Dictionary<string, LocaleDetails>
            {
                // 첫 항목(en)이 나머지 언어의 기본값이 된다
                ["en"] = new()
                {
                    Name = "Prestige Coin",
                    ShortName = "Prestige",
                    Description = "A coin minted for those who chose to start over. Spent on prestige perks and at the prestige trader.",
                },
                ["kr"] = new()
                {
                    Name = "프레스티지 코인",
                    ShortName = "프레스티지",
                    Description = "다시 시작하기로 한 이들을 위해 주조된 코인. 프레스티지 특전과 프레스티지 상인에게 쓰인다.",
                },
            },
        };
}
