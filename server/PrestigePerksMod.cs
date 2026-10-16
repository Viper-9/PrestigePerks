using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;

namespace PrestigePerks;

// ContentBackportPrestiges 가 Preload + 3 에서 프레스티지 데이터를 교체하므로 그보다 뒤에 돈다.
[Injectable(TypePriority = OnLoadOrder.Preload + 10)]
public class PrestigePerksMod(ISptLogger<PrestigePerksMod> logger) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken cancellationToken)
    {
        logger.Success($"[PrestigePerks] v{ModMetadata.AssemblyVersion} loaded.");
        return Task.CompletedTask;
    }
}
