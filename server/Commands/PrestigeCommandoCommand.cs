using System.Text.RegularExpressions;
using PrestigePerks.Items;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Dialogue.Commando;
using SPTarkov.Server.Core.Helpers.Items;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Services.Commerce;

namespace PrestigePerks.Commands;

/// <summary>
/// Commando 채팅 명령 "prestige ...". SPT 의 "spt give" 와 같은 개발·테스트용이라
/// core.json 의 giveCommandEnabled 가 꺼져 있으면 명령을 등록하지 않는다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PrestigeCommandoCommand(
    CoreConfig coreConfig,
    TemplateTable templateTable,
    ItemHelper itemHelper,
    MailSendService mailSendService) : ICommandoCommand
{
    public const string Prefix = "prestige";
    public const string CoinCommand = "coin";

    /// <summary>한 번에 받을 수 있는 최대 개수. 스택 100개짜리 10묶음.</summary>
    public const int MaxCoinAmount = 1000;

    private static readonly Regex CoinRegex = new($"^{Prefix} {CoinCommand}(?: ([0-9]+))?$", RegexOptions.IgnoreCase);

    public string CommandPrefix => Prefix;

    public List<string> Commands =>
        coreConfig.Features.ChatbotFeatures.CommandoFeatures.GiveCommandEnabled ? [CoinCommand] : [];

    public string GetCommandHelp(string command) =>
        command == CoinCommand
            ? $"{Prefix} {CoinCommand}\n========\nSends prestige coins to the player through the message system.\n\n\t{Prefix} {CoinCommand} [quantity]\n\t\tEx: {Prefix} {CoinCommand} 10\n\t\tQuantity defaults to 1, max {MaxCoinAmount}."
            : "";

    public ValueTask<string> Handle(string command, UserDialogInfo commandHandler, MongoId sessionId, SendMessageRequest request)
    {
        var amount = ParseCoinAmount(request.Text);
        if (amount is null)
        {
            mailSendService.SendUserMessageToPlayer(sessionId, commandHandler,
                $"Invalid use of {Prefix} {CoinCommand}. Quantity must be 1-{MaxCoinAmount}. Use 'help' for more information.");
            return new ValueTask<string>(request.DialogId);
        }

        if (!templateTable.Items.TryGetValue(PrestigeCoinItem.TemplateId, out var template))
        {
            mailSendService.SendUserMessageToPlayer(sessionId, commandHandler,
                "Prestige coin item is not registered. Check the server log for PrestigePerks errors.");
            return new ValueTask<string>(request.DialogId);
        }

        var coin = new Item
        {
            Id = new MongoId(),
            Template = template.Id,
            Upd = itemHelper.GenerateUpdForItem(template),
        };
        coin.Upd.StackObjectsCount = amount.Value;

        var items = itemHelper.SplitStack(coin);
        itemHelper.SetFoundInRaid(items);
        mailSendService.SendSystemMessageToPlayer(sessionId, $"PRESTIGE COIN DELIVERY: {amount.Value}", items);
        return new ValueTask<string>(request.DialogId);
    }

    /// <summary>"prestige coin [n]" 에서 수량을 읽는다. 생략하면 1, 범위를 벗어나면 null.</summary>
    internal static int? ParseCoinAmount(string? text)
    {
        var match = CoinRegex.Match(text?.Trim() ?? "");
        if (!match.Success)
        {
            return null;
        }

        if (!match.Groups[1].Success)
        {
            return 1;
        }

        return int.TryParse(match.Groups[1].Value, out var amount) && amount is >= 1 and <= MaxCoinAmount
            ? amount
            : null;
    }
}
