using PrestigePerks.Logging;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Helpers.Profile;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Prestige;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Utils;

namespace PrestigePerks.Items;

/// <summary>
/// 프레스티지 때 전달 아이템 대신 코인을 준다 (dev-docs 3_프레스티지_코인.md 6절). PrestigeCoinPatch 가 ProcessPendingPrestige 앞뒤에서 부른다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PrestigeCoinService(
    ISptLogger<PerksFileLogger> logger,
    TemplateTable templateTable,
    HandbookHelper handbookHelper,
    TimeUtil timeUtil,
    PrestigeCoinMailer coinMailer)
{
    /// <summary>Harmony 패치가 쓰는 인스턴스. 모드 로드 때 넣는다.</summary>
    internal static PrestigeCoinService? Instance { get; set; }

    /// <summary>
    /// 이월·환산을 계산하고 바닐라 전달 메일이 나가지 않게 prestige.Items 를 비운다.
    /// PendingPrestige 없이 단계만 강제한 생성(SptForcePrestigeLevel)이거나 코인 아이템이 없으면 null (바닐라대로).
    /// </summary>
    public PrestigeCoinResult? Prepare(SptProfile oldProfile, PendingPrestige prestige)
    {
        var profileId = oldProfile.ProfileInfo?.ProfileId;
        // 강제 단계 생성은 CreateProfile 이 새 PendingPrestige 를 만들어 넘긴다. 이전 프로필의 것과 같은 객체일 때만 실제 프레스티지다
        if (!ReferenceEquals(oldProfile.SptData?.PendingPrestige, prestige))
        {
            logger.Info($"[PrestigePerks] prestige coins for {profileId}: forced prestige level {prestige.PrestigeLevel} (no pending prestige), skipped");
            return null;
        }

        if (!coinMailer.IsAvailable)
        {
            logger.Warning($"[PrestigePerks] prestige coins for {profileId}: coin item not registered, vanilla item transfer kept");
            return null;
        }

        var requests = (prestige.Items ?? []).Select(request => request.Id).ToList();
        var cells = TransferCells(prestige.PrestigeLevel);
        var result = PrestigeCoinConversion.Calculate(oldProfile, requests, cells, Price, timeUtil.GetTimeStamp(), out var details);
        logger.Info($"[PrestigePerks] prestige to P{prestige.PrestigeLevel} for {profileId}: {details}");
        LogTransferItems(oldProfile, requests);
        prestige.Items = new ObtainPrestigeRequestList();
        logger.Info($"[PrestigePerks] prestige coins for {profileId}: carried over {result.CarriedOver}, converted {result.Converted}, lost {result.Lost}; vanilla transfer mail suppressed");
        return result;
    }

    /// <summary>새 프로필(ID 는 프레스티지 전과 같음)에 코인 메일 1통. 이월 + 환산이 0 이면 보내지 않는다.</summary>
    public void Deliver(SptProfile newProfile, PrestigeCoinResult result)
    {
        var profileId = newProfile.ProfileInfo?.ProfileId;
        if (profileId is not { } sessionId)
        {
            logger.Error($"[PrestigePerks] prestige coin mail: new profile has no id ({result.Total} coins not sent)");
            return;
        }

        if (result.Total <= 0)
        {
            logger.Info($"[PrestigePerks] prestige coin mail for {sessionId}: 0 coins, not sent");
            return;
        }

        var message = Message(result);
        if (coinMailer.TrySend(sessionId, result.Total, message, PrestigeCoinMailer.OneYearSeconds))
        {
            logger.Info($"[PrestigePerks] prestige coin mail for {sessionId}: {result.Total} coins in {(result.Total + 99) / 100} stacks, kept {PrestigeCoinMailer.OneYearSeconds / 86400} days, text \"{message}\"");
        }
        else
        {
            logger.Error($"[PrestigePerks] prestige coin mail failed for {sessionId} ({result.Total} coins)");
        }
    }

    /// <summary>전달 아이템마다 한 줄: 템플릿, 스택, 하위 아이템 수, 하위 포함 핸드북 가치.</summary>
    private void LogTransferItems(SptProfile oldProfile, List<MongoId> requests)
    {
        var items = oldProfile.CharacterData?.PmcData?.Inventory?.Items ?? [];
        foreach (var id in requests)
        {
            var group = PrestigeCoinConversion.TransferredItems(items, [id]);
            if (group.Count == 0)
            {
                logger.Warning($"[PrestigePerks]   transfer {id}: not found in PMC inventory (0 roubles)");
                continue;
            }

            var root = group.First(item => item.Id == id);
            var value = group.Sum(item => Price(item.Template) * (item.Upd?.StackObjectsCount ?? 1));
            logger.Info($"[PrestigePerks]   transfer {root.Template} x{root.Upd?.StackObjectsCount ?? 1} (+{group.Count - 1} children): {value:N0} roubles");
        }
    }

    private double Price(MongoId tpl) => handbookHelper.GetTemplatePrice(tpl);

    /// <summary>개발용 미리보기: 지금 프로필로 다음 프레스티지를 하면 이월되는 코인과 그 단계 환산 상한. 전달 창 내용은 알 수 없어 환산은 0.</summary>
    public (int CarriedOver, int Cap, int NextLevel) Preview(SptProfile profile)
    {
        var next = (profile.CharacterData?.PmcData?.Info?.PrestigeLevel ?? 0) + 1;
        var carried = PrestigeCoinConversion.Calculate(profile, [], 0, Price, timeUtil.GetTimeStamp()).CarriedOver;
        return (carried, PrestigeCoinConversion.Cap(TransferCells(next), Price), next);
    }

    /// <summary>메일 본문 (영어, dev-docs 3_프레스티지_코인.md 2절).</summary>
    internal static string Message(PrestigeCoinResult result)
    {
        var text = $"Prestige coins: carried over {result.CarriedOver} + converted {result.Converted}";
        return result.Lost > 0 ? $"{text} ({result.Lost} coins lost over the conversion cap)" : text;
    }

    /// <summary>이번 단계 전달 창 칸 수. 원소는 바닐라와 같게 PrestigeLevel − 1 로 고른다.</summary>
    private int TransferCells(int? prestigeLevel)
    {
        var elements = templateTable.Prestige.Elements ?? [];
        if (elements.Count == 0)
        {
            return 0;
        }

        var stash = elements[Math.Clamp((prestigeLevel ?? 1) - 1, 0, elements.Count - 1)].TransferConfigs?.StashConfig;
        return (stash?.XCellCount ?? 0) * (stash?.YCellCount ?? 0);
    }
}
