using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Commerce;

namespace PrestigePerks.Items;

/// <summary>프레스티지 코인을 시스템 메일로 보낸다. 서버는 인벤토리를 직접 바꾸지 않는다 (구현 원칙).</summary>
[Injectable(InjectionType.Singleton)]
public class PrestigeCoinMailer(TemplateTable templateTable, ItemHelper itemHelper, MailSendService mailSendService)
{
    /// <summary>코인 메일 보관 기간 1년 (바닐라 프레스티지 전달 메일과 같음). SPT 기본값은 48시간이다.</summary>
    public const long OneYearSeconds = 31_536_000;

    /// <summary>코인 아이템이 등록돼 있는지.</summary>
    public bool IsAvailable => templateTable.Items.ContainsKey(PrestigeCoinItem.TemplateId);

    /// <summary>코인 아이템이 등록돼 있지 않으면 false.</summary>
    public bool TrySend(MongoId sessionId, int amount, string message, long maxStorageSeconds)
    {
        if (amount <= 0 || !templateTable.Items.TryGetValue(PrestigeCoinItem.TemplateId, out var template))
        {
            return false;
        }

        var coin = new Item
        {
            Id = new MongoId(),
            Template = template.Id,
            Upd = itemHelper.GenerateUpdForItem(template),
        };
        coin.Upd.StackObjectsCount = amount;

        var items = itemHelper.SplitStack(coin);
        itemHelper.SetFoundInRaid(items);
        mailSendService.SendSystemMessageToPlayer(sessionId, message, items, maxStorageSeconds);
        return true;
    }
}
