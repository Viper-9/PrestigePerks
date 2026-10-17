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

    private readonly Lazy<PerkConfig> config = new(() => Load(logger, modHelper));

    public PerkConfig Config => config.Value;

    private static PerkConfig Load(ISptLogger<PerkConfigLoader> logger, ModHelper modHelper)
    {
        var path = Path.Combine(modHelper.GetAbsolutePathToModFolder(typeof(PerkConfigLoader).Assembly), "config", FileName);
        if (!File.Exists(path))
        {
            logger.Warning($"[PrestigePerks] config/{FileName} 이 없어 기본값을 씁니다");
            return new PerkConfig();
        }

        try
        {
            var perks = Parse(File.ReadAllText(path));
            var error = perks.Validate();
            if (error is null)
            {
                return perks;
            }

            logger.Warning($"[PrestigePerks] config/{FileName}: {error}. 기본값을 씁니다");
        }
        catch (JsonException ex)
        {
            logger.Warning($"[PrestigePerks] config/{FileName} 을 읽지 못해 기본값을 씁니다: {ex.Message}");
        }

        return new PerkConfig();
    }

    internal static PerkConfig Parse(string json) =>
        (JsonSerializer.Deserialize<ModConfig>(json, Options) ?? new ModConfig()).Perks ?? new PerkConfig();
}
