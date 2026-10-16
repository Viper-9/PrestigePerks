using PrestigePerks.Items;
using SPTarkov.Server.Core.Models.Common;

namespace PrestigePerks.Api;

/// <summary>
/// 다른 모드(프레스티지 상인 모드 등)가 PrestigePerks DLL 을 참조해 읽는 프레스티지 코인 정보.
/// 코인 아이템은 OnLoadOrder.Preload + 10 에서 만들어지므로, 읽는 쪽은 그 뒤에 아이템을 써야 한다.
/// </summary>
public static class PrestigeCoin
{
    /// <summary>프레스티지 코인 템플릿 ID. const 가 아니라 참조하는 DLL 에 값이 박히지 않는다.</summary>
    public static MongoId TemplateId { get; } = PrestigeCoinItem.TemplateId;

    /// <summary>환산 단가. 1코인 = 100,000₽.</summary>
    public static double RoublesPerCoin => PrestigeCoinItem.PriceRoubles;
}
