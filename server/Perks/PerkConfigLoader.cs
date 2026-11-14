using System.Text.Json;
using PrestigePerks.Items;
using PrestigePerks.Logging;
using PrestigePerks.Prestiges;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;

namespace PrestigePerks.Perks;

/// <summary>모드 폴더의 config/config.json 을 처음 쓸 때 한 번 읽는다. 없거나 잘못되면 기본값을 쓰고 경고한다.</summary>
[Injectable(InjectionType.Singleton)]
public class PerkConfigLoader(ISptLogger<PerksFileLogger> logger, ModHelper modHelper)
{
    public const string FileName = "config.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Lazy<ModConfig> config = new(() => Load(logger, modHelper));

    private readonly object amountsLock = new();
    private bool amountsApplied;

    public PerkConfig Config => config.Value.Perks;

    public ActivePerkConfig ActivePerks => config.Value.ActivePerks;

    public PrestigeConfig Prestige => config.Value.Prestige;

    public CoinConfig Coin => config.Value.Coin;

    public SafeCoinConfig SafeCoins => config.Value.SafeCoins;

    public BossCoinConfig BossCoins => config.Value.BossCoins;

    /// <summary>
    /// config perkAmounts 를 내장 특전 수치로 넣는다 (<see cref="PerkCatalog.Configure"/>). 처음 한 번만 하고 다음 호출은 아무것도 하지 않는다.
    /// 같은 순서(Preload + 10)의 IOnLoad 가 둘(<see cref="PrestigePerksMod"/>, <see cref="ActivePerkBuffs"/>)이라 수치를 읽는 쪽이 먼저 부른다.
    /// 확인용으로 기본값과 다른 수치를 로그에 남긴다.
    /// </summary>
    public void ApplyPerkAmounts()
    {
        lock (amountsLock)
        {
            if (amountsApplied)
            {
                return;
            }

            amountsApplied = true;
            var result = PerkAmountConfig.Resolve(config.Value.PerkAmounts, warning => logger.Warning($"[PrestigePerks] config/{FileName}: {warning}"));
            PerkCatalog.Configure(result.Builtin, result.BuiltinActive);
            logger.Info(result.Changed.Count == 0
                ? "[PrestigePerks] perk amounts: defaults"
                : $"[PrestigePerks] perk amounts changed by config ({result.Changed.Count}): {string.Join(", ", result.Changed)}");
        }
    }

    private static ModConfig Load(ISptLogger<PerksFileLogger> logger, ModHelper modHelper)
    {
        var path = Path.Combine(modHelper.GetAbsolutePathToModFolder(typeof(PerkConfigLoader).Assembly), "config", FileName);
        if (!File.Exists(path))
        {
            logger.Warning($"[PrestigePerks] config/{FileName} 이 없어 기본값을 씁니다");
            return new ModConfig();
        }

        try
        {
            return Validated(ParseModConfig(File.ReadAllText(path)), warning => logger.Warning($"[PrestigePerks] config/{FileName}: {warning}"));
        }
        catch (JsonException ex)
        {
            logger.Warning($"[PrestigePerks] config/{FileName} 을 읽지 못해 기본값을 씁니다: {ex.Message}");
        }

        return new ModConfig();
    }

    /// <summary>범위를 벗어난 슬롯 수·무료 리롤은 끝값으로 맞추고, 항목(perks, prestige, coin, safeCoins, bossCoins)마다 검사해 잘못된 항목만 기본값으로 바꾼다. activePerks 가 null 이면 기본값(켬).</summary>
    internal static ModConfig Validated(ModConfig config, Action<string> warn)
    {
        var perks = (config.Perks ?? new PerkConfig()).Clamped(warn);
        if (perks.LegacyTierEffectRatio is not null)
        {
            warn("perks.tierEffectRatio 는 더 이상 쓰지 않습니다 (무시합니다). 단계별 수치는 perkAmounts 에 특전마다 [I, II, III] 로 적습니다");
            perks = perks with { LegacyTierEffectRatio = null };
        }

        var prestige = config.Prestige ?? new PrestigeConfig();
        if (perks.Validate() is { } perkError)
        {
            warn($"{perkError}. perks 항목은 기본값을 씁니다");
            perks = new PerkConfig();
        }

        if (prestige.Validate() is { } prestigeError)
        {
            warn($"{prestigeError}. prestige 항목은 기본값을 씁니다");
            prestige = new PrestigeConfig();
        }

        var coin = config.Coin ?? new CoinConfig();
        if (coin.Validate() is { } coinError)
        {
            warn($"{coinError}. coin 항목은 기본값을 씁니다");
            coin = new CoinConfig();
        }

        // 금고·보스 스택은 코인 한 칸(coin.stackSize)을 넘을 수 없다. 넘으면 stackSize 로 맞춘다
        var stackSize = coin.StackSize;
        var safeCoins = config.SafeCoins ?? new SafeCoinConfig();
        if (safeCoins.MaxStack > stackSize)
        {
            warn($"safeCoins.maxStack({safeCoins.MaxStack}) 이 coin.stackSize({stackSize}) 보다 커서 {stackSize} 로 맞춥니다");
            safeCoins = safeCoins with { MinStack = Math.Min(safeCoins.MinStack, stackSize), MaxStack = stackSize };
        }

        if (safeCoins.Validate(stackSize) is { } safeCoinError)
        {
            warn($"{safeCoinError}. safeCoins 항목은 기본값을 씁니다");
            var fallback = new SafeCoinConfig();
            safeCoins = fallback with { MinStack = Math.Min(fallback.MinStack, stackSize), MaxStack = Math.Min(fallback.MaxStack, stackSize) };
        }

        var bossCoins = config.BossCoins ?? new BossCoinConfig();
        if (bossCoins.MaxStack > stackSize)
        {
            warn($"bossCoins.maxStack({bossCoins.MaxStack}) 이 coin.stackSize({stackSize}) 보다 커서 {stackSize} 로 맞춥니다");
            bossCoins = bossCoins with { MinStack = Math.Min(bossCoins.MinStack, stackSize), MaxStack = stackSize };
        }

        if (bossCoins.Validate(stackSize) is { } bossCoinError)
        {
            warn($"{bossCoinError}. bossCoins 항목은 기본값을 씁니다");
            var fallback = new BossCoinConfig();
            bossCoins = fallback with { MinStack = Math.Min(fallback.MinStack, stackSize), MaxStack = Math.Min(fallback.MaxStack, stackSize) };
        }

        return config with { Perks = perks, ActivePerks = (config.ActivePerks ?? new ActivePerkConfig()).Clamped(warn), Prestige = prestige, Coin = coin, SafeCoins = safeCoins, BossCoins = bossCoins };
    }

    internal static ModConfig ParseModConfig(string json) => JsonSerializer.Deserialize<ModConfig>(json, Options) ?? new ModConfig();

    internal static PerkConfig Parse(string json) => ParseModConfig(json).Perks ?? new PerkConfig();
}
