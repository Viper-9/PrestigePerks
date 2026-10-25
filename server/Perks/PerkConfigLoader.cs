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

    public PerkConfig Config => config.Value.Perks;

    public PrestigeConfig Prestige => config.Value.Prestige;

    public SafeCoinConfig SafeCoins => config.Value.SafeCoins;

    public BossCoinConfig BossCoins => config.Value.BossCoins;

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

    /// <summary>범위를 벗어난 슬롯 수·무료 리롤은 끝값으로 맞추고, 항목(perks, prestige, safeCoins, bossCoins)마다 검사해 잘못된 항목만 기본값으로 바꾼다.</summary>
    internal static ModConfig Validated(ModConfig config, Action<string> warn)
    {
        var perks = (config.Perks ?? new PerkConfig()).Clamped(warn);
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

        var safeCoins = config.SafeCoins ?? new SafeCoinConfig();
        if (safeCoins.Validate() is { } safeCoinError)
        {
            warn($"{safeCoinError}. safeCoins 항목은 기본값을 씁니다");
            safeCoins = new SafeCoinConfig();
        }

        var bossCoins = config.BossCoins ?? new BossCoinConfig();
        if (bossCoins.Validate() is { } bossCoinError)
        {
            warn($"{bossCoinError}. bossCoins 항목은 기본값을 씁니다");
            bossCoins = new BossCoinConfig();
        }

        return config with { Perks = perks, Prestige = prestige, SafeCoins = safeCoins, BossCoins = bossCoins };
    }

    internal static ModConfig ParseModConfig(string json) => JsonSerializer.Deserialize<ModConfig>(json, Options) ?? new ModConfig();

    internal static PerkConfig Parse(string json) => ParseModConfig(json).Perks ?? new PerkConfig();
}
