using PrestigePerks.Logging;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace PrestigePerks.Items;

/// <summary>
/// 바닐라 상인이 프레스티지 코인을 사들이지 않게 매입 금지 목록(items_buy_prohibited)에 넣는다.
/// 코인은 화폐 분류라 "쌓이는 아이템" 분류를 사는 Fence 가 IsUnsaleable 과 상관없이 사들였다 (2026-10-23 게임 확인).
/// 다른 모드의 상인은 건드리지 않는다. 상인이 코인을 받고 물건을 파는 물물교환(프레스티지 상인)은 이 목록과 상관없다.
/// </summary>
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public class PrestigeCoinTraderBlock(ISptLogger<PerksFileLogger> logger, TradersTable tradersTable) : IOnLoad
{
    /// <summary>
    /// SPT 기본 데이터베이스(database/traders)에서 아이템을 사들이는 상인.
    /// Lightkeeper·BTR·Storyteller 는 매입 분류가 비어 있어 넣지 않는다.
    /// </summary>
    internal static readonly string[] VanillaTraderIds =
    [
        "54cb50c76803fa8b248b4571", // Prapor
        "54cb57776803fa99248b456e", // Therapist
        "579dc571d53a0658a154fbec", // Fence
        "58330581ace78e27b8b10cee", // Skier
        "5935c25fb3acc3127c3d8cd9", // Peacekeeper
        "5a7c2eca46aef81a7ca2145d", // Mechanic
        "5ac3b934156ae10c4430e83c", // Ragman
        "5c0647fdd443bc2504c2d371", // Jaeger
        "6617beeaa9cfa777ca915b7c", // Ref
    ];

    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        var coin = new MongoId(PrestigeCoinItem.TemplateId);
        var blocked = 0;
        foreach (var id in VanillaTraderIds)
        {
            if (tradersTable.GetTrader(new MongoId(id)) is not { } trader)
            {
                continue;
            }

            var prohibited = trader.Base.ItemsBuyProhibited ??= new() { Category = [], IdList = [] };
            prohibited.IdList ??= [];
            prohibited.IdList.Add(coin);
            blocked++;
        }

        logger.Info($"[PrestigePerks] prestige coin added to the buy-prohibited list of {blocked}/{VanillaTraderIds.Length} vanilla traders");
        return Task.CompletedTask;
    }
}
