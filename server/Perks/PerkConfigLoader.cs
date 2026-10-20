using System.Text.Json;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Server;

namespace PrestigePerks.Perks;

/// <summary>모드 폴더의 config/config.json 을 처음 쓸 때 한 번 읽는다. 없거나 잘못되면 기본값을 쓰고 경고한다.</summary>
[Injectable(InjectionType.Singleton)]
public class PerkConfigLoader(ISptLogger<PerkConfigLoader> logger, ModHelper modHelper)
{
    public const string FileName = "config.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Lazy<ModConfig> config = new(() => Load(logger, modHelper));

    public PerkConfig Config => config.Value.Perks;

    private static ModConfig Load(ISptLogger<PerkConfigLoader> logger, ModHelper modHelper)
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

    /// <summary>범위를 벗어난 슬롯 수·무료 리롤은 끝값으로 맞추고, 잘못된 항목은 기본값으로 바꾼다.</summary>
    internal static ModConfig Validated(ModConfig config, Action<string> warn)
    {
        var perks = (config.Perks ?? new PerkConfig()).Clamped(warn);
        if (perks.Validate() is { } perkError)
        {
            warn($"{perkError}. perks 항목은 기본값을 씁니다");
            perks = new PerkConfig();
        }

        return config with { Perks = perks };
    }

    internal static ModConfig ParseModConfig(string json) => JsonSerializer.Deserialize<ModConfig>(json, Options) ?? new ModConfig();

    internal static PerkConfig Parse(string json) => ParseModConfig(json).Perks ?? new PerkConfig();
}
