using PrestigePerks.Perks;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Spt.Tables;

namespace PrestigePerks.Prestiges;

/// <summary>
/// 모드 로드 때(Preload + 10, 백포트 모드 + 3 뒤) 프레스티지 조건을 설정값으로 바꾸고 전달 창 문구를 덮어쓴다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PrestigeDataLoader(ISptLogger<PrestigeDataLoader> logger, TemplateTable templateTable, LocaleTable localeTable, PerkConfigLoader configLoader)
{
    public void Load()
    {
        ApplyConditions();
        ApplyLocales();
    }

    private void ApplyConditions()
    {
        var result = PrestigeConditionRules.Apply(templateTable.Prestige, templateTable.Quests, configLoader.Prestige);
        foreach (var skipped in result.Skipped)
        {
            logger.Warning($"[PrestigePerks] prestige condition skipped: {skipped}");
        }

        logger.Info($"[PrestigePerks] prestige conditions: {result.Changed} changed, {result.Removed} removed, {result.Added} quests added, {result.Skipped.Count} skipped");
        var elements = templateTable.Prestige.Elements ?? [];
        for (var i = 0; i < PrestigeConditionRules.ElementIds.Length; i++)
        {
            var element = elements.FirstOrDefault(e => e.Id == PrestigeConditionRules.ElementIds[i]);
            logger.Info($"[PrestigePerks]   P{i + 1}: {(element is null ? "missing" : PrestigeConditionRules.Describe(element))}");
        }

        logger.Info($"[PrestigePerks]   New Beginning start levels: {PrestigeConditionRules.DescribeNewBeginningLevels(templateTable.Quests)}");
    }

    private void ApplyLocales()
    {
        var applied = new List<string>();
        foreach (var (language, texts) in PrestigeLocales.Texts)
        {
            if (!localeTable.Global.TryGetValue(language, out var locale))
            {
                logger.Warning($"[PrestigePerks] locale {language} not found, prestige transfer texts not changed");
                continue;
            }

            locale.AddTransformer(data =>
            {
                if (data is not null)
                {
                    foreach (var (key, text) in texts)
                    {
                        data[key] = text;
                    }
                }

                return data;
            });
            applied.Add(language);
        }

        logger.Info($"[PrestigePerks] prestige transfer window texts set for: {string.Join(", ", applied)} ({string.Join(", ", PrestigeLocales.Texts["en"].Keys)})");
    }
}
