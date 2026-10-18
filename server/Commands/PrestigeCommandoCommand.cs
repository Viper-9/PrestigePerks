using System.Text;
using System.Text.RegularExpressions;
using PrestigePerks.Items;
using PrestigePerks.Perks;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Dialogue.Commando;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services.Commerce;

namespace PrestigePerks.Commands;

/// <summary>
/// Commando 채팅 명령 "prestige ...". SPT 의 "spt give" 와 같은 개발·테스트용이라
/// core.json 의 giveCommandEnabled 가 꺼져 있으면 명령을 등록하지 않는다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PrestigeCommandoCommand(
    CoreConfig coreConfig,
    PrestigeCoinMailer coinMailer,
    PerkService perkService,
    MailSendService mailSendService) : ICommandoCommand
{
    public const string Prefix = "prestige";
    public const string CoinCommand = "coin";
    public const string PerkCommand = "perk";

    /// <summary>한 번에 받을 수 있는 최대 개수. 스택 100개짜리 10묶음.</summary>
    public const int MaxCoinAmount = 1000;

    /// <summary>개발용 무료 리롤 설정 상한.</summary>
    public const int MaxFreeRerolls = 100;

    private static readonly Regex CoinRegex = new($"^{Prefix} {CoinCommand}(?: ([0-9]+))?$", RegexOptions.IgnoreCase);
    private static readonly Regex PerkRegex = new($"^{Prefix} {PerkCommand} ([a-z]+)(?: ([0-9]+))?$", RegexOptions.IgnoreCase);

    public string CommandPrefix => Prefix;

    public List<string> Commands =>
        coreConfig.Features.ChatbotFeatures.CommandoFeatures.GiveCommandEnabled ? [CoinCommand, PerkCommand] : [];

    public string GetCommandHelp(string command) =>
        command switch
        {
            CoinCommand => $"{Prefix} {CoinCommand}\n========\nSends prestige coins to the player through the message system.\n\n\t{Prefix} {CoinCommand} [quantity]\n\t\tEx: {Prefix} {CoinCommand} 10\n\t\tQuantity defaults to 1, max {MaxCoinAmount}.",
            PerkCommand => $"{Prefix} {PerkCommand}\n========\nDev commands for prestige perks.\n\n" +
                            $"\t{Prefix} {PerkCommand} status\n" +
                            $"\t{Prefix} {PerkCommand} roll\t\tfree rerolls only (paid rerolls use inventory coins on the perk page)\n" +
                            $"\t{Prefix} {PerkCommand} lock <slot>\t\tslot starts at 1\n" +
                            $"\t{Prefix} {PerkCommand} unlock <slot>\n" +
                            $"\t{Prefix} {PerkCommand} free <count>\t\tset free rerolls left (0-{MaxFreeRerolls})\n" +
                            $"\t{Prefix} {PerkCommand} reset\t\tclear perk data: slots, locks, free/paid rerolls (previous file kept as .bak)",
            _ => "",
        };

    public ValueTask<string> Handle(string command, UserDialogInfo commandHandler, MongoId sessionId, SendMessageRequest request)
    {
        var reply = command == PerkCommand ? HandlePerk(sessionId, request.Text) : HandleCoin(sessionId, request.Text);
        if (reply is not null)
        {
            mailSendService.SendUserMessageToPlayer(sessionId, commandHandler, reply);
        }

        return new ValueTask<string>(request.DialogId);
    }

    private string? HandleCoin(MongoId sessionId, string? text)
    {
        var amount = ParseCoinAmount(text);
        if (amount is null)
        {
            return $"Invalid use of {Prefix} {CoinCommand}. Quantity must be 1-{MaxCoinAmount}. Use 'help' for more information.";
        }

        return coinMailer.TrySend(sessionId, amount.Value, $"PRESTIGE COIN DELIVERY: {amount.Value}")
            ? null
            : "Prestige coin item is not registered. Check the server log for PrestigePerks errors.";
    }

    private string HandlePerk(MongoId sessionId, string? text)
    {
        var parsed = ParsePerkCommand(text);
        if (parsed is null)
        {
            return $"Invalid use of {Prefix} {PerkCommand}. Use 'help' for more information.";
        }

        var (action, argument) = parsed.Value;
        var state = action switch
        {
            PerkAction.Status => perkService.GetState(sessionId),
            PerkAction.Roll => perkService.RollFree(sessionId),
            PerkAction.Lock => perkService.SetLock(sessionId, argument - 1, true),
            PerkAction.Unlock => perkService.SetLock(sessionId, argument - 1, false),
            PerkAction.Free => perkService.SetFreeRerolls(sessionId, argument),
            PerkAction.Reset => perkService.Reset(sessionId),
            _ => throw new ArgumentOutOfRangeException(nameof(action)),
        };
        var reply = FormatState(state);
        if (action == PerkAction.Status)
        {
            var bonuses = perkService.GetAppliedBonuses(sessionId);
            reply += $"\nprofile bonuses (memory): {(bonuses.Count == 0 ? "none" : string.Join(", ", bonuses))}";
        }

        return reply;
    }

    public enum PerkAction
    {
        Status,
        Roll,
        Lock,
        Unlock,
        Free,
        Reset,
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

    /// <summary>"prestige perk &lt;동작&gt; [n]". lock/unlock 은 슬롯(1~6), free 는 0~MaxFreeRerolls 가 필요하다. 틀리면 null.</summary>
    internal static (PerkAction Action, int Argument)? ParsePerkCommand(string? text)
    {
        var match = PerkRegex.Match(text?.Trim() ?? "");
        if (!match.Success || !Enum.TryParse<PerkAction>(match.Groups[1].Value, ignoreCase: true, out var action))
        {
            return null;
        }

        var hasArgument = match.Groups[2].Success;
        var argument = 0;
        if (hasArgument && !int.TryParse(match.Groups[2].Value, out argument))
        {
            return null;
        }

        return action switch
        {
            PerkAction.Lock or PerkAction.Unlock => hasArgument && argument is >= 1 and <= 6 ? (action, argument) : null,
            PerkAction.Free => hasArgument && argument is >= 0 and <= MaxFreeRerolls ? (action, argument) : null,
            _ => hasArgument ? null : (action, 0),
        };
    }

    /// <summary>
    /// P3 | active: experience(1), scav(8)
    /// slots:  [1] experience(1) (locked)  [2] mastery(24)  [3] scav(8)
    /// free rerolls 2 | paid rerolls 0 | next reroll: free
    /// </summary>
    internal static string FormatState(PerkState state)
    {
        var text = new StringBuilder();
        if (state.Error is not null)
        {
            text.Append($"Error: {state.Error}\n");
        }

        text.Append($"P{state.PrestigeLevel} | active: ");
        text.Append(state.ActivePerks.Count == 0 ? "none" : string.Join(", ", state.ActivePerks.Select(PerkCatalog.Describe)));
        if (state.RollCost is > 0)
        {
            text.Append($"\nreroll paid {state.RollCost} coins");
        }

        if (state.NewPerks.Count > 0)
        {
            text.Append($"\nnew perks: {string.Join(", ", state.NewPerks.Select(PerkCatalog.Describe))}");
        }

        text.Append("\nslots:");
        for (var i = 0; i < state.Slots.Count; i++)
        {
            var slot = state.Slots[i];
            text.Append($"  [{i + 1}] {PerkCatalog.Describe(slot.Perk)}{(slot.Locked ? " (locked)" : "")}");
        }

        text.Append($"\nfree rerolls {state.FreeRerollsLeft} | paid rerolls {state.PaidRerolls} | next reroll: ");
        text.Append(state.NextRerollCost > 0 ? $"{state.NextRerollCost} coins" : "free");
        return text.ToString();
    }
}
