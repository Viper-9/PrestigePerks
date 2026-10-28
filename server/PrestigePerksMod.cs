using PrestigePerks.Items;
using PrestigePerks.Logging;
using PrestigePerks.Patches;
using PrestigePerks.Perks;
using PrestigePerks.Prestiges;
using PrestigePerks.Saving;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Helpers.Server;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils;

namespace PrestigePerks;

// ContentBackportPrestiges 가 Preload + 3 에서 프레스티지 데이터를 교체하므로 그보다 뒤에 돈다.
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public class PrestigePerksMod(
    ISptLogger<PrestigePerksMod> logger,
    ISptLogger<PerksFileLogger> fileLogger,
    JsonUtil jsonUtil,
    ModHelper modHelper,
    PerkDataStore perkDataStore,
    ServerPatcher serverPatcher,
    PrestigeDataLoader prestigeDataLoader,
    PerkConfigLoader perkConfigLoader,
    PrestigeCoinService prestigeCoinService) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        CheckProfileConverters();
        LoadAddons();
        PerkScales.Store = perkDataStore;
        PerkScales.Config = perkConfigLoader.Config;
        PrestigeCoinService.Instance = prestigeCoinService;
        prestigeDataLoader.Load();
        serverPatcher.PatchAll();
        logger.Success($"[PrestigePerks] v{ModMetadata.AssemblyVersion} loaded.");
        return Task.CompletedTask;
    }

    /// <summary>
    /// perks/ 의 add-on 특전을 카탈로그에 설치한다. 프로필 로드(Bonus 넣기, 데이터 파일 정리)보다 먼저여야
    /// add-on 특전이 모르는 특전으로 정리되지 않는다 (Preload + 10 &lt; SaveCallbacks).
    /// </summary>
    private void LoadAddons()
    {
        var folder = Path.Combine(modHelper.GetAbsolutePathToModFolder(typeof(PrestigePerksMod).Assembly), "perks");
        var report = PerkAddonLoader.Load(folder, warning => fileLogger.Warning($"[PrestigePerks] {warning}"));
        var installWarnings = PerkCatalog.Install(report.Perks);
        foreach (var warning in installWarnings)
        {
            fileLogger.Warning($"[PrestigePerks] {warning}");
        }

        PerkCatalog.InstallIcons(report.Icons);
        PerkAddonLoader.Last = report with { Perks = PerkCatalog.Addons, Warnings = [.. report.Warnings, .. installWarnings] };
        if (PerkCatalog.Addons.Count > 0)
        {
            logger.Info($"[PrestigePerks] add-on perks ({PerkCatalog.Addons.Count}): {string.Join(", ", PerkCatalog.Addons.Select(perk => perk.Id))}");
        }
    }

    /// <summary>
    /// 저장 때 특전 Bonus 를 빼는 변환기가 등록됐는지, 다른 모드의 SptProfile 변환기와 겹치지 않는지 확인한다.
    /// System.Text.Json 은 목록에서 처음 맞는 변환기 하나만 쓰므로 겹치면 한쪽이 무시된다.
    /// </summary>
    private void CheckProfileConverters()
    {
        _ = jsonUtil; // JsonUtil 생성자가 옵션을 만든다
        var converters = JsonUtil.JsonSerializerOptionsNoIndent?.Converters;
        if (converters is null)
        {
            fileLogger.Error("[PrestigePerks] SPT JSON 옵션을 찾지 못했습니다. 특전 Bonus 가 프로필 파일에 저장될 수 있습니다");
            return;
        }

        var profileConverters = converters.Where(converter => converter.CanConvert(typeof(SptProfile))).ToList();
        var ours = profileConverters.FindIndex(converter => converter is PerkProfileConverter);
        if (ours < 0)
        {
            fileLogger.Error("[PrestigePerks] 프로필 저장 변환기가 등록되지 않았습니다. 특전 Bonus 가 프로필 파일에 저장될 수 있습니다 (다음 로드 때 다시 정리됨)");
            return;
        }

        foreach (var other in profileConverters.Where(converter => converter is not PerkProfileConverter))
        {
            fileLogger.Warning(ours == 0
                ? $"[PrestigePerks] 다른 모드의 프로필 변환기({other.GetType().FullName})가 있습니다. PrestigePerks 변환기가 먼저 쓰여 그 변환기는 저장 때 무시됩니다"
                : $"[PrestigePerks] 다른 모드의 프로필 변환기({other.GetType().FullName})가 먼저 등록되어 저장 때 특전 Bonus 를 빼지 못합니다 (로드 때 다시 정리됨)");
        }
    }
}
