using System.Text;
using System.Text.RegularExpressions;
using PrestigePerks.Items;
using PrestigePerks.Perks;
using PrestigePerks.Saving;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Dialogue.Commando;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Dialog;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Enums;
using SPTarkov.Server.Core.Models.Spt.Config;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Controllers;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers;

namespace PrestigePerks.Commands;

// 개발용 명령은 Dev 빌드에만 넣는다 (상용 Release 빌드에는 이 클래스가 없다). 서버 csproj 의 Dev 구성 참고
#if DEV_COMMANDS

/// <summary>
/// Commando 채팅 명령 "prestige ...". SPT 의 "spt give" 와 같은 개발·테스트용이라
/// Dev 빌드에만 들어가고, core.json 의 giveCommandEnabled 가 꺼져 있으면 명령을 등록하지 않는다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PrestigeCommandoCommand(
    CoreConfig coreConfig,
    PrestigeCoinMailer coinMailer,
    PrestigeCoinService coinService,
    PerkService perkService,
    MailSendService mailSendService,
    SaveServer saveServer,
    HideoutHelper hideoutHelper,
    HideoutController hideoutController,
    HideoutTable hideoutTable,
    BossCoinCheck bossCoinCheck) : ICommandoCommand
{
    public const string Prefix = "prestige";
    public const string CoinCommand = "coin";
    public const string PerkCommand = "perk";

    /// <summary>한 번에 받을 수 있는 최대 개수. 스택 100개짜리 10묶음.</summary>
    public const int MaxCoinAmount = 1000;

    /// <summary>개발용 무료 리롤 설정 상한.</summary>
    public const int MaxFreeRerolls = 100;

    private static readonly Regex CoinRegex = new($"^{Prefix} {CoinCommand}(?: ([0-9]+))?$", RegexOptions.IgnoreCase);
    private static readonly Regex CoinPreviewRegex = new($"^{Prefix} {CoinCommand} preview$", RegexOptions.IgnoreCase);
    private static readonly Regex BossCheckRegex = new($"^{Prefix} {CoinCommand} boss(?: (\\S+))?$", RegexOptions.IgnoreCase);
    private static readonly Regex PerkRegex = new($"^{Prefix} {PerkCommand} ([a-z]+)(?: ([0-9]+))?$", RegexOptions.IgnoreCase);
    private static readonly Regex SetRegex = new($"^{Prefix} {PerkCommand} set ([0-9]+) ([a-z0-9_.-]+)$", RegexOptions.IgnoreCase);
    private static readonly Regex HideoutRegex = new($"^{Prefix} {PerkCommand} hideout$", RegexOptions.IgnoreCase);
    private static readonly Regex TierRegex = new($"^{Prefix} {PerkCommand} tier ([a-z0-9_.-]+) ([0-9]+)$", RegexOptions.IgnoreCase);
    private static readonly Regex CollectionResetRegex = new($"^{Prefix} {PerkCommand} collection reset$", RegexOptions.IgnoreCase);
    private static readonly Regex AllRegex = new($"^{Prefix} {PerkCommand} all (on|off)$", RegexOptions.IgnoreCase);
    private static readonly Regex AddonsRegex = new($"^{Prefix} {PerkCommand} addons$", RegexOptions.IgnoreCase);
    private static readonly Regex BonusRegex = new($"^{Prefix} {PerkCommand} bonus$", RegexOptions.IgnoreCase);
    private static readonly Regex ScaleRegex = new($"^{Prefix} {PerkCommand} scale ([a-z0-9_.-]+)$", RegexOptions.IgnoreCase);

    public string CommandPrefix => Prefix;

    public List<string> Commands =>
        coreConfig.Features.ChatbotFeatures.CommandoFeatures.GiveCommandEnabled ? [CoinCommand, PerkCommand] : [];

    public string GetCommandHelp(string command) =>
        command switch
        {
            CoinCommand => $"{Prefix} {CoinCommand}\n========\nSends prestige coins to the player through the message system.\n\n\t{Prefix} {CoinCommand} [quantity]\n\t\tEx: {Prefix} {CoinCommand} 10\n\t\tQuantity defaults to 1, max {MaxCoinAmount}.\n\t{Prefix} {CoinCommand} boss [bots]\t\tgenerate each boss [bots] times (default {BossCoinCheck.DefaultBots}, max {BossCoinCheck.MaxBots}) and count prestige coins and GP coins",
            PerkCommand => $"{Prefix} {PerkCommand}\n========\nDev commands for prestige perks.\n\n" +
                            $"\t{Prefix} {PerkCommand} status\n" +
                            $"\t{Prefix} {PerkCommand} roll\t\tfree rerolls only (paid rerolls use inventory coins on the perk page)\n" +
                            $"\t{Prefix} {PerkCommand} lock <slot>\t\tslot starts at 1\n" +
                            $"\t{Prefix} {PerkCommand} unlock <slot>\n" +
                            $"\t{Prefix} {PerkCommand} free <count>\t\tset free rerolls left (0-{MaxFreeRerolls})\n" +
                            $"\t{Prefix} {PerkCommand} set <slot> <perk>\t\tput a perk (number or id, e.g. 24 or mastery) in a slot (swaps if another slot has it). Reopen the perk page or main menu to apply it in game\n" +
                            $"\t{Prefix} {PerkCommand} tier <perk> <0-3>\t\tset the owned tier of a perk (number or id) without coins (0 removes it from the collection)\n" +
                            $"\t{Prefix} {PerkCommand} collection reset\t\tclear the collection (fixed slots become reroll slots)\n" +
                            $"\t{Prefix} {PerkCommand} all on|off\t\tapply every perk at tier III regardless of slots (dev testing). Reopen the perk page or main menu to apply it in game\n" +
                            $"\t{Prefix} {PerkCommand} hideout\t\tshow server hideout times with artisan(17) applied (compare all on/off)\n" +
                            $"\t{Prefix} {PerkCommand} addons\t\tshow perk add-ons read from perks/ at server start and their warnings\n" +
                            $"\t{Prefix} {PerkCommand} bonus\t\tsum of PMC profile bonuses per add-on bonus type (perk bonuses in brackets)\n" +
                            $"\t{Prefix} {PerkCommand} scale <perk>\t\tserver effect ratio of a perk (number or id) for your PMC, as server add-ons read it (0 = off)\n" +
                            $"\t{Prefix} {PerkCommand} reset\t\tclear perk data: slots, locks, collection, free/paid rerolls (previous file kept as .bak)",
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
        if (IsCoinPreview(text))
        {
            return DescribeCoinPreview(sessionId);
        }

        if (IsBossCheck(text))
        {
            return ParseBossCheckBots(text) is { } bots
                ? bossCoinCheck.Describe(sessionId, bots)
                : $"Invalid use of {Prefix} {CoinCommand} boss. Bots must be 1-{BossCoinCheck.MaxBots}.";
        }

        var amount = ParseCoinAmount(text);
        if (amount is null)
        {
            return $"Invalid use of {Prefix} {CoinCommand}. Quantity must be 1-{MaxCoinAmount}. Use 'help' for more information.";
        }

        return coinMailer.TrySend(sessionId, amount.Value, $"PRESTIGE COIN DELIVERY: {amount.Value}", PrestigeCoinMailer.OneYearSeconds)
            ? null
            : "Prestige coin item is not registered. Check the server log for PrestigePerks errors.";
    }

    private string HandlePerk(MongoId sessionId, string? text)
    {
        if (ParseSetCommand(text) is var (slot, perk))
        {
            return FormatState(perkService.SetPerk(sessionId, slot - 1, perk));
        }

        if (ParseTierCommand(text) is var (tierPerk, tier))
        {
            return FormatState(perkService.SetOwnedTier(sessionId, tierPerk, tier));
        }

        if (CollectionResetRegex.IsMatch(text?.Trim() ?? ""))
        {
            return FormatState(perkService.ResetCollection(sessionId));
        }

        if (HideoutRegex.IsMatch(text?.Trim() ?? ""))
        {
            return DescribeHideout(sessionId);
        }

        if (ParseAllCommand(text) is { } all)
        {
            return FormatState(perkService.SetAllPerks(sessionId, all));
        }

        if (IsAddonsCommand(text))
        {
            return FormatAddons(PerkAddonLoader.Last);
        }

        if (BonusRegex.IsMatch(text?.Trim() ?? ""))
        {
            return DescribeBonuses(sessionId);
        }

        if (ScaleRegex.IsMatch(text?.Trim() ?? ""))
        {
            if (ParseScaleCommand(text) is not { } scalePerk)
            {
                return $"Unknown perk. Use a perk number or id ({Prefix} {PerkCommand} addons lists add-on perks).";
            }

            var pmc = saveServer.ProfileExists(sessionId) ? saveServer.GetProfile(sessionId).CharacterData?.PmcData : null;
            return $"{PerkCatalog.Describe(scalePerk)} scale {PerkScales.Of(pmc, scalePerk):0.##}";
        }

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

    /// <summary>"prestige coin preview".</summary>
    internal static bool IsCoinPreview(string? text) => CoinPreviewRegex.IsMatch(text?.Trim() ?? "");

    /// <summary>"prestige coin boss [n]".</summary>
    internal static bool IsBossCheck(string? text) => BossCheckRegex.IsMatch(text?.Trim() ?? "");

    /// <summary>"prestige coin boss [n]" 의 봇 수. 생략하면 <see cref="BossCoinCheck.DefaultBots"/>, 1~MaxBots 를 벗어나면 null.</summary>
    internal static int? ParseBossCheckBots(string? text)
    {
        var match = BossCheckRegex.Match(text?.Trim() ?? "");
        if (!match.Success)
        {
            return null;
        }

        if (!match.Groups[1].Success)
        {
            return BossCoinCheck.DefaultBots;
        }

        return int.TryParse(match.Groups[1].Value, out var bots) && bots is >= 1 and <= BossCoinCheck.MaxBots ? bots : null;
    }

    private string DescribeCoinPreview(MongoId sessionId)
    {
        if (!saveServer.ProfileExists(sessionId))
        {
            return "No profile.";
        }

        var (carried, cap, next) = coinService.Preview(saveServer.GetProfile(sessionId));
        return $"prestige to P{next} now: carried over {carried} coins (inventory, scav, unclaimed mail, BTR) + transfer window converted up to {cap} coins";
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

    /// <summary>"prestige perk set &lt;슬롯&gt; &lt;특전 번호 또는 ID&gt;". 슬롯 1~6, 특전은 목록에 있는 것. 틀리면 null.</summary>
    internal static (int Slot, string Perk)? ParseSetCommand(string? text)
    {
        var match = SetRegex.Match(text?.Trim() ?? "");
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var slot) || PerkCatalog.Resolve(match.Groups[2].Value) is not { } perk)
        {
            return null;
        }

        return slot is >= 1 and <= 6 ? (slot, perk) : null;
    }

    /// <summary>"prestige perk tier &lt;특전 번호 또는 ID&gt; &lt;0~3&gt;". 특전은 목록에 있는 것. 틀리면 null.</summary>
    internal static (string Perk, int Tier)? ParseTierCommand(string? text)
    {
        var match = TierRegex.Match(text?.Trim() ?? "");
        if (!match.Success || PerkCatalog.Resolve(match.Groups[1].Value) is not { } perk || !int.TryParse(match.Groups[2].Value, out var tier))
        {
            return null;
        }

        return tier is >= 0 and <= PerkConfig.MaxTier ? (perk, tier) : null;
    }

    /// <summary>
    /// 개발용: 서버가 지금 계산하는 은신처 시간 (17번 패치를 지난 실제 값). all on / all off 로 비교하면 ×0.8 인지 바로 보인다.
    /// 클라이언트 화면 시간은 레시피 화면을 연 뒤 BepInEx [dev] 로그의 17 줄로 확인한다.
    /// </summary>
    private string DescribeHideout(MongoId sessionId)
    {
        if (!saveServer.ProfileExists(sessionId) || saveServer.GetProfile(sessionId).CharacterData?.PmcData is not { Info: not null } pmc)
        {
            return "No PMC profile.";
        }

        var scale = PerkArtisan.Scale(pmc);
        var active = scale > 0;
        var text = new StringBuilder($"artisan(17) {(active ? $"ACTIVE x{PerkArtisan.Factor(scale):0.###}" : "inactive")} (server times in seconds)");
        var recipes = (hideoutTable.Production.Recipes ?? [])
            .Where(recipe => recipe.Id != HideoutHelper.BitcoinProductionId && recipe.ProductionTime > 0)
            .GroupBy(recipe => recipe.Continuous == true)
            .SelectMany(group => group.Take(group.Key ? 1 : 3));
        foreach (var recipe in recipes)
        {
            var adjusted = hideoutHelper.GetAdjustedCraftTimeWithSkills(pmc, recipe.Id, applyHideoutManagementBonus: recipe.Continuous == true);
            text.Append($"\ncraft {recipe.AreaType} {recipe.Id}{(recipe.Continuous == true ? " (continuous)" : "")}: base {recipe.ProductionTime:0} -> {adjusted:0.#}");
        }

        var scavRecipe = hideoutTable.Production.ScavRecipes?.FirstOrDefault(recipe => recipe.ProductionTime > 0);
        if (scavRecipe is not null)
        {
            var scavTime = typeof(HideoutController).GetMethod("GetScavCaseTime", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.Invoke(hideoutController, [pmc, scavRecipe.ProductionTime]);
            text.Append($"\nscav case {scavRecipe.Id}: base {scavRecipe.ProductionTime:0} -> {scavTime:0.#} (fence modifier included, crafting skill not)");
        }

        text.Append($"\ncultist 43200 (12h) -> {(active ? PerkArtisan.CraftTime(43200, scale) : 43200):0}");
        text.Append($"\nbitcoin progress per 100 s elapsed -> {(active ? PerkArtisan.BitcoinElapsed(100, scale) : 100)} s");
        return text.ToString();
    }

    /// <summary>"prestige perk scale &lt;특전 번호 또는 ID&gt;" 의 특전 ID. 목록에 없으면 null.</summary>
    internal static string? ParseScaleCommand(string? text)
    {
        var match = ScaleRegex.Match(text?.Trim() ?? "");
        return match.Success ? PerkCatalog.Resolve(match.Groups[1].Value) : null;
    }

    /// <summary>"prestige perk addons".</summary>
    internal static bool IsAddonsCommand(string? text) => AddonsRegex.IsMatch(text?.Trim() ?? "");

    /// <summary>
    /// add-ons: viper-extra, other
    /// perks: viper.medic bonus percent 20, viper.sprint code percent 10
    /// warning: perks/x/perks.json: ...
    /// </summary>
    internal static string FormatAddons(PerkAddonReport report)
    {
        var text = new StringBuilder($"add-ons: {(report.Sources.Count == 0 ? "none" : string.Join(", ", report.Sources))}");
        text.Append($"\nperks: {(report.Perks.Count == 0 ? "none" : string.Join(", ", report.Perks.Select(perk =>
            $"{perk.Id} {(perk.Kind == PerkKind.Patch ? PerkCatalogEntry.CodeKind : PerkCatalogEntry.BonusKind)} {perk.Unit.ToString().ToLowerInvariant()} {perk.Amount:0.##}")))}");
        foreach (var warning in report.Warnings)
        {
            text.Append($"\nwarning: {warning}");
        }

        return text.ToString();
    }

    /// <summary>개발용: 메모리 PMC Bonuses 의 add-on 허용 타입별 합 (특전 Bonus 만의 합은 괄호). 게임 확인에서 특전 값 × 단계 비율이 들어갔는지 본다.</summary>
    private string DescribeBonuses(MongoId sessionId)
    {
        if (!saveServer.ProfileExists(sessionId) || saveServer.GetProfile(sessionId).CharacterData?.PmcData is not { Info: not null } pmc)
        {
            return "No PMC profile.";
        }

        var bonuses = pmc.Bonuses ?? [];
        var text = new StringBuilder("PMC bonuses (memory), perk bonuses in brackets:");
        foreach (var type in PerkAddonFile.AllowedBonusTypes.Order())
        {
            var ofType = bonuses.Where(bonus => bonus.Type == type).ToList();
            var perk = ofType.Where(PerkBonusApplier.IsPerkBonus).ToList();
            text.Append($"\n{type}: {ofType.Sum(bonus => bonus.Value ?? 0):0.##} ({perk.Sum(bonus => bonus.Value ?? 0):0.##})");
            if (type == BonusType.SkillGroupLevelingBoost && ofType.Count > 0)
            {
                text.Append($" [{string.Join(", ", ofType.GroupBy(bonus => bonus.SkillType).Select(group => $"{group.Key}: {group.Sum(bonus => bonus.Value ?? 0):0.##}"))}]");
            }
        }

        return text.ToString();
    }

    /// <summary>"prestige perk all on|off". 틀리면 null.</summary>
    internal static bool? ParseAllCommand(string? text)
    {
        var match = AllRegex.Match(text?.Trim() ?? "");
        return match.Success ? match.Groups[1].Value.Equals("on", StringComparison.OrdinalIgnoreCase) : null;
    }

    /// <summary>
    /// P3 | tier cap II | active: experience(1) II, scav(8) I
    /// slots:  [1] experience(1) II (fixed)  [2] empty  [3] scav(8) I (locked)
    /// collection 1 / 20: experience(1) II
    /// free rerolls 2 | paid rerolls 0 | next reroll: free
    /// </summary>
    internal static string FormatState(PerkState state)
    {
        var text = new StringBuilder();
        if (state.Error is not null)
        {
            text.Append($"Error: {state.Error}\n");
        }

        string Tier(string? perk) => PerkService.TierName(perk is not null && state.ActiveTiers.TryGetValue(perk, out var tier) ? tier : 0);
        text.Append($"P{state.PrestigeLevel} | tier cap {PerkService.TierName(state.TierCap)} | active: ");
        text.Append(state.ActivePerks.Count == 0 ? "none" : string.Join(", ", state.ActivePerks.Select(perk => $"{PerkCatalog.Describe(perk)} {Tier(perk)}")));
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
            text.Append(slot.IsEmpty
                ? $"  [{i + 1}] empty"
                : $"  [{i + 1}] {PerkCatalog.Describe(slot.Perk)} {Tier(slot.Perk)}{(slot.Fixed ? " (fixed)" : slot.Locked ? " (locked)" : "")}");
        }

        text.Append($"\ncollection {state.Owned.Count} / {PerkCatalog.Ids.Count}");
        if (state.Owned.Count > 0)
        {
            text.Append(": ").Append(string.Join(", ", state.Owned.OrderBy(pair => PerkCatalog.NumberOf(pair.Key) is > 0 and var number ? number : int.MaxValue).Select(pair => $"{PerkCatalog.Describe(pair.Key)} {PerkService.TierName(pair.Value)}")));
        }

        text.Append($"\nfree rerolls {state.FreeRerollsLeft} | paid rerolls {state.PaidRerolls} | next reroll: ");
        text.Append(state.NextRerollCost > 0 ? $"{state.NextRerollCost} coins" : "free");
        return text.ToString();
    }
}
#endif
