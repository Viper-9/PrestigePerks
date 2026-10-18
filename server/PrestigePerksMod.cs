using PrestigePerks.Patches;
using PrestigePerks.Perks;
using PrestigePerks.Saving;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Utils;

namespace PrestigePerks;

// ContentBackportPrestiges 가 Preload + 3 에서 프레스티지 데이터를 교체하므로 그보다 뒤에 돈다.
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public class PrestigePerksMod(ISptLogger<PrestigePerksMod> logger, JsonUtil jsonUtil, PerkDataStore perkDataStore, ArtisanPatcher artisanPatcher) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        CheckProfileConverters();
        PerkArtisan.Store = perkDataStore;
        artisanPatcher.PatchAll();
        logger.Success($"[PrestigePerks] v{ModMetadata.AssemblyVersion} loaded.");
        return Task.CompletedTask;
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
            logger.Error("[PrestigePerks] SPT JSON 옵션을 찾지 못했습니다. 특전 Bonus 가 프로필 파일에 저장될 수 있습니다");
            return;
        }

        var profileConverters = converters.Where(converter => converter.CanConvert(typeof(SptProfile))).ToList();
        var ours = profileConverters.FindIndex(converter => converter is PerkProfileConverter);
        if (ours < 0)
        {
            logger.Error("[PrestigePerks] 프로필 저장 변환기가 등록되지 않았습니다. 특전 Bonus 가 프로필 파일에 저장될 수 있습니다 (다음 로드 때 다시 정리됨)");
            return;
        }

        foreach (var other in profileConverters.Where(converter => converter is not PerkProfileConverter))
        {
            logger.Warning(ours == 0
                ? $"[PrestigePerks] 다른 모드의 프로필 변환기({other.GetType().FullName})가 있습니다. PrestigePerks 변환기가 먼저 쓰여 그 변환기는 저장 때 무시됩니다"
                : $"[PrestigePerks] 다른 모드의 프로필 변환기({other.GetType().FullName})가 먼저 등록되어 저장 때 특전 Bonus 를 빼지 못합니다 (로드 때 다시 정리됨)");
        }
    }
}
