using PrestigePerks.Items;
using PrestigePerks.Saving;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common;
using SPTarkov.Server.Core.Models.Eft.ItemEvent;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;
using SPTarkov.Server.Core.Extensions;
using SPTarkov.Server.Core.Services.Commerce;
using SPTarkov.Server.Core.Utils;

namespace PrestigePerks.Perks;

/// <summary>
/// 특전 흐름 (새 단계 진입·잠금·리롤·보유하기·단계 구매·확정 넣기/빼기). 규칙은 <see cref="PerkRules"/>, 저장은 <see cref="PerkDataStore"/>.
/// HTTP 라우트(<c>PerkStaticRouter</c>), 코인 아이템 이벤트(<c>PerkItemEventRouter</c>), 개발용 Commando 명령이 같이 쓴다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PerkService(
    ISptLogger<PerkService> logger,
    PerkDataStore store,
    PerkConfigLoader configLoader,
    SaveServer saveServer,
    PaymentService paymentService,
    HttpResponseUtil httpResponseUtil)
{
    private static readonly IReadOnlyList<string> BonusIds = PerkCatalog.FilteredBonusIds.Select(id => id.ToString()).ToList();

    private readonly Random random = Random.Shared;

#if DEV_COMMANDS
    private const bool DevBuild = true;
#else
    private const bool DevBuild = false;
#endif

    private PerkConfig Config => configLoader.Config;

    /// <summary>게임 시작: 새 단계면 처리하고, 특전 Bonus 를 다시 넣는다 (다른 모드가 Bonuses 를 새로 만들었을 때 대비).</summary>
    public void OnGameStart(MongoId sessionId)
    {
        if (!TryGetProfile(sessionId, out var profile) || profile.ProfileInfo?.IsWiped == true)
        {
            return;
        }

        lock (store.GetLock(sessionId))
        {
            EnterLevel(sessionId, profile);
            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store, Config);
        }
    }

    /// <summary>
    /// 현재 상태. 새 단계면 처리한다. <paramref name="markNewPerksSeen"/> 면 새 특전·단계 상한 알림을 이번 응답으로 넘기고 지운다 (클라이언트 페이지용).
    /// </summary>
    public PerkState GetState(MongoId sessionId, bool markNewPerksSeen = false) =>
        Run(sessionId, (profile, data, level) =>
        {
            EnterLevel(sessionId, profile);
            var state = BuildState(data, level);
            if (markNewPerksSeen && (data.UnseenNewPerks.Count > 0 || data.UnseenTierCap is not null))
            {
                data.UnseenNewPerks.Clear();
                data.UnseenTierCap = null;
                store.Save(sessionId, data);
            }

            return state;
        });

    public PerkState SetLock(MongoId sessionId, int slotIndex, bool locked) =>
        Run(sessionId, (_, data, level) =>
        {
            var error = PerkRules.SetLock(data, Config.Slots(level), slotIndex, locked);
            if (error == PerkError.None)
            {
                store.Save(sessionId, data);
            }

            return BuildState(data, level, error);
        });

    /// <summary>확정 넣기: 보유 특전을 리롤 칸·빈 칸·확정 칸에 놓는다 (무료).</summary>
    public PerkState Equip(MongoId sessionId, int perk, int slotIndex) =>
        Change(sessionId, (data, level) => PerkRules.Equip(data, Config.Slots(level), perk, slotIndex), $"넣기 {PerkCatalog.Describe(perk)} → 슬롯 {slotIndex + 1}");

    /// <summary>확정 빼기: 그 칸은 빈 칸이 되어 다음 리롤 때 채워진다 (무료).</summary>
    public PerkState Unequip(MongoId sessionId, int slotIndex) =>
        Change(sessionId, (data, level) => PerkRules.Unequip(data, Config.Slots(level), slotIndex), $"빼기 슬롯 {slotIndex + 1}");

    /// <summary>
    /// 리롤 (아이템 이벤트). 유료면 인벤토리 코인을 바닐라 결제(<see cref="PaymentService.AddPaymentToOutput(PmcData, MongoId, double, MongoId, ItemEventRouterResponse, IReadOnlySet{MongoId})"/>)로
    /// 빼서 <paramref name="output"/> 에 담는다. 클라이언트는 응답의 ProfileChanges 로 스태시를 바닐라처럼 고친다.
    /// </summary>
    public ItemEventRouterResponse RollWithCoins(MongoId sessionId, PmcData pmcData, PerkRollRequest request, ItemEventRouterResponse output)
    {
        var state = Run(sessionId, (profile, data, level) =>
        {
            EnterLevel(sessionId, profile);
            var error = PerkRules.CheckRoll(data, Config.Slots(level));
            if (error != PerkError.None)
            {
                return BuildState(data, level, error);
            }

            var cost = PerkRules.NextRerollCost(Config, level, data);
            if (cost != request.ExpectedCost)
            {
                return BuildState(data, level, PerkError.CostChanged);
            }

            if (!Pay(sessionId, pmcData, cost, request.Items, output))
            {
                return BuildState(data, level, PerkError.NotEnoughCoins);
            }

            var outcome = PerkRules.Roll(data, level, Config, random);
            store.Save(sessionId, data);
            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store, Config);
            logger.Info($"[PrestigePerks] {sessionId}: 리롤 ({(cost == 0 ? "무료" : $"코인 {cost}")}) → [{DescribeSlots(data, level)}]");
            return BuildState(data, level) with { RollCost = outcome.Cost };
        });

        return Finish(state, output);
    }

    /// <summary>
    /// 보유하기(단계 I)·단계 구매 (아이템 이벤트). 리롤과 같이 인벤토리 코인으로 결제한다.
    /// 이미 그 단계를 가졌으면 결제 없이 성공 (두 번 눌림).
    /// </summary>
    public ItemEventRouterResponse BuyWithCoins(MongoId sessionId, PmcData pmcData, PerkBuyRequest request, ItemEventRouterResponse output)
    {
        var state = Run(sessionId, (profile, data, level) =>
        {
            EnterLevel(sessionId, profile);
            var error = PerkRules.CheckBuy(data, level, Config, request.Perk, request.Tier);
            if (error == PerkError.AlreadyOwned)
            {
                return BuildState(data, level) with { BuyCost = 0 };
            }

            if (error != PerkError.None)
            {
                return BuildState(data, level, error);
            }

            var cost = Config.TierPrice(request.Tier);
            if (cost != request.ExpectedCost)
            {
                return BuildState(data, level, PerkError.CostChanged);
            }

            if (!Pay(sessionId, pmcData, cost, request.Items, output))
            {
                return BuildState(data, level, PerkError.NotEnoughCoins);
            }

            PerkRules.Buy(data, level, Config, request.Perk, request.Tier);
            store.Save(sessionId, data);
            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store, Config);
            logger.Info($"[PrestigePerks] {sessionId}: 구매 {PerkCatalog.Describe(request.Perk)} 단계 {request.Tier} (코인 {cost}) → [{DescribeSlots(data, level)}]");
            return BuildState(data, level) with { BuyCost = cost };
        });

        return Finish(state, output);
    }

    /// <summary>개발용: 슬롯에 특전을 직접 넣는다 (특전 효과를 비교해 보려고). 다른 슬롯에 있던 특전이면 서로 바꾼다.</summary>
    public PerkState SetPerk(MongoId sessionId, int slotIndex, int perk) =>
        Change(sessionId, (data, level) => PerkRules.SetPerk(data, Config.Slots(level), slotIndex, perk), $"개발용 set 슬롯 {slotIndex + 1} → {perk}");

    /// <summary>개발용: 보유 단계를 정한다 (0 이면 도감에서 뺀다). 결제 없이.</summary>
    public PerkState SetOwnedTier(MongoId sessionId, int perk, int tier) =>
        Change(sessionId, (data, _) => PerkRules.SetOwnedTier(data, perk, tier), $"개발용 tier {PerkCatalog.Describe(perk)} → {tier}");

    /// <summary>개발용: 도감을 비운다 (확정 칸은 리롤 칸이 된다).</summary>
    public PerkState ResetCollection(MongoId sessionId) =>
        Change(sessionId, (data, _) =>
        {
            PerkRules.ResetCollection(data);
            return PerkError.None;
        }, "개발용 도감 초기화");

    /// <summary>개발용: 슬롯과 상관없이 모든 특전을 III 으로 켜거나 끈다 (패치 특전 부하·대상 확인용). 게임 반영은 다음 state.</summary>
    public PerkState SetAllPerks(MongoId sessionId, bool on) =>
        Change(sessionId, (data, _) =>
        {
            data.DevAllPerks = on;
            return PerkError.None;
        }, $"개발용 all {(on ? "on" : "off")}");

    /// <summary>개발용: 무료 리롤만 (서버는 인벤토리 코인을 뺄 수 없다).</summary>
    public PerkState RollFree(MongoId sessionId) =>
        Run(sessionId, (profile, data, level) =>
        {
            EnterLevel(sessionId, profile);
            if (data.Rerolls.FreeLeft <= 0)
            {
                return BuildState(data, level, PerkError.NoFreeRerolls);
            }

            var outcome = PerkRules.Roll(data, level, Config, random);
            if (outcome.Success)
            {
                store.Save(sessionId, data);
                PerkSaveLoadRouter.ApplyPerkBonuses(profile, store, Config);
            }

            return BuildState(data, level, outcome.Error);
        });

    /// <summary>개발용: 남은 무료 리롤 횟수를 정한다.</summary>
    public PerkState SetFreeRerolls(MongoId sessionId, int count) =>
        Run(sessionId, (profile, data, level) =>
        {
            EnterLevel(sessionId, profile);
            data.Rerolls.FreeLeft = Math.Max(count, 0);
            store.Save(sessionId, data);
            return BuildState(data, level);
        });

    /// <summary>개발용: 특전 데이터를 빈 데이터로 덮어쓰고 Bonus 를 뺀다 (슬롯·잠금·도감·무료/유료 리롤 모두). 직전 파일은 .bak 으로 남는다. 다음 state·게임 시작 때 새로 뽑는다.</summary>
    public PerkState Reset(MongoId sessionId) =>
        Run(sessionId, (profile, _, level) =>
        {
            store.Delete(sessionId);
            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store, Config);
            // 바로 지금 단계로 다시 들어가 슬롯을 뽑는다 (무료 리롤 freeRerolls 회, 새 특전은 다음 state 에서 알림)
            EnterLevel(sessionId, profile);
            return BuildState(store.Get(sessionId), level);
        });

    /// <summary>개발용: 메모리 PMC 프로필에 지금 들어 있는 특전 Bonus ("type value" 목록). 저장 파일에는 없어야 한다.</summary>
    public IReadOnlyList<string> GetAppliedBonuses(MongoId sessionId) =>
        TryGetProfile(sessionId, out var profile)
            ? (profile.CharacterData!.PmcData!.Bonuses ?? [])
                .Where(PerkBonusApplier.IsPerkBonus)
                .Select(bonus => $"{bonus.Type}{(bonus.SkillType is { } skill ? $"({skill})" : "")} {bonus.Value:+0.##;-0.##}")
                .ToList()
            : [];

    /// <summary>코인 결제. 0 이면 아무것도 하지 않는다. 결제 함수가 경고를 붙였으면(코인 부족) 인벤토리는 바뀌지 않았고 false.</summary>
    private bool Pay(MongoId sessionId, PmcData pmcData, int cost, List<string>? items, ItemEventRouterResponse output)
    {
        if (cost <= 0)
        {
            return true;
        }

        var warningsBefore = output.Warnings?.Count ?? 0;
        var preferred = (items ?? []).Where(id => id.IsValidMongoId()).Select(id => new MongoId(id)).ToHashSet();
        paymentService.AddPaymentToOutput(pmcData, new MongoId(PrestigeCoinItem.TemplateId), cost, sessionId, output, preferred);
        return (output.Warnings?.Count ?? 0) <= warningsBefore;
    }

    // 코인 부족은 결제 함수가 이미 경고를 붙였다. 다른 실패만 오류로 붙인다
    private ItemEventRouterResponse Finish(PerkState state, ItemEventRouterResponse output)
    {
        if (state.Error is { } failure && failure != nameof(PerkError.NotEnoughCoins))
        {
            httpResponseUtil.AppendErrorToOutput(output, $"PrestigePerks: {failure}");
        }

        return output;
    }

    /// <summary>코인이 들지 않는 변경: 새 단계를 처리하고, 바꾸고, 성공하면 저장하고 Bonus 를 다시 넣는다.</summary>
    private PerkState Change(MongoId sessionId, Func<PerkData, int, PerkError> change, string what) =>
        Run(sessionId, (profile, data, level) =>
        {
            EnterLevel(sessionId, profile);
            var error = change(data, level);
            if (error == PerkError.None)
            {
                store.Save(sessionId, data);
                PerkSaveLoadRouter.ApplyPerkBonuses(profile, store, Config);
                logger.Info($"[PrestigePerks] {sessionId}: {what} → [{DescribeSlots(data, level)}]");
            }

            return BuildState(data, level, error);
        });

    private PerkState Run(MongoId sessionId, Func<SptProfile, PerkData, int, PerkState> action)
    {
        if (!TryGetProfile(sessionId, out var profile))
        {
            return new PerkState { Error = "NoProfile" };
        }

        lock (store.GetLock(sessionId))
        {
            var data = store.Get(sessionId);
            if (data.LoadFailed)
            {
                return new PerkState { Error = "DataLoadFailed" };
            }

            return action(profile, data, GetPrestigeLevel(profile));
        }
    }

    /// <summary>새 단계면 횟수를 되돌리고(같은 단계에서 freeRerolls 를 바꿨으면 차이만큼 고치고) 빈 슬롯을 뽑는다. 바뀌었으면 저장하고 Bonus 를 다시 넣는다. 잠금 안에서 부른다.</summary>
    private void EnterLevel(MongoId sessionId, SptProfile profile)
    {
        var data = store.Get(sessionId);
        var level = GetPrestigeLevel(profile);
        if (data.LoadFailed)
        {
            return;
        }

        var (changed, newPerks) = PerkRules.EnterLevel(data, level, Config, random);
        if (!changed)
        {
            return;
        }

        store.Save(sessionId, data);
        PerkSaveLoadRouter.ApplyPerkBonuses(profile, store, Config);
        logger.Info($"[PrestigePerks] {sessionId}: P{level} 특전 데이터 갱신 (무료 리롤 {data.Rerolls.FreeLeft}/{data.Rerolls.FreeGranted}회){(newPerks.Count > 0 ? $", 새 특전 [{string.Join(", ", newPerks)}]" : "")}");
    }

    private PerkState BuildState(PerkData data, int level, PerkError error = PerkError.None) =>
        new()
        {
            PrestigeLevel = level,
            Slots = PerkRules.ActiveSlots(data, Config.Slots(level)).Select(slot => new PerkSlot { Perk = slot.Perk, Locked = slot.Locked, Fixed = slot.Fixed }).ToList(),
            ActivePerks = PerkRules.ActivePerks(data, Config.Slots(level)),
            ActiveTiers = PerkRules.ActiveTiers(data, level, Config),
            Owned = new Dictionary<int, int>(data.Owned),
            TierCap = Config.TierCap(level),
            TierOpensAt = [Config.TierOpensAt(level, 2), Config.TierOpensAt(level, 3)],
            TierPrices = Config.TierPrices,
            TierEffectRatio = Config.TierEffectRatio,
            FreeRerollsLeft = data.Rerolls.FreeLeft,
            FreeRerolls = Config.FreeRerolls,
            PaidRerolls = data.Rerolls.Paid,
            NextRerollCost = PerkRules.NextRerollCost(Config, level, data),
            CostFixed = data.Rerolls.FreeLeft <= 0 && data.Rerolls.Paid + 1 >= Config.PaidCostCapRound,
            NewPerks = data.UnseenNewPerks.ToList(),
            NewTierCap = data.UnseenTierCap,
            Bonuses = PerkBonusApplier.CreateBonuses(PerkRules.ActiveScales(data, level, Config)),
            BonusIds = BonusIds,
            SlotOpensAt = Config.SlotOpensAt(level),
            Dev = DevBuild,
            Error = error == PerkError.None ? null : error.ToString(),
        };

    /// <summary>"control(14) II fixed, empty, reload(29) I locked" (로그용).</summary>
    private string DescribeSlots(PerkData data, int level)
    {
        var tiers = PerkRules.ActiveTiers(data, level, Config);
        return string.Join(", ", PerkRules.ActiveSlots(data, Config.Slots(level)).Select(slot =>
            slot.IsEmpty ? "empty" : $"{PerkCatalog.Describe(slot.Perk)} {TierName(tiers.GetValueOrDefault(slot.Perk, 1))}{(slot.Fixed ? " fixed" : slot.Locked ? " locked" : "")}"));
    }

    internal static string TierName(int tier) => tier switch { 1 => "I", 2 => "II", 3 => "III", _ => "-" };

    private bool TryGetProfile(MongoId sessionId, out SptProfile profile)
    {
        profile = null!;
        if (sessionId.IsEmpty || !saveServer.ProfileExists(sessionId))
        {
            return false;
        }

        profile = saveServer.GetProfile(sessionId);
        return profile.CharacterData?.PmcData?.Info is not null;
    }

    private static int GetPrestigeLevel(SptProfile profile) => profile.CharacterData?.PmcData?.Info?.PrestigeLevel ?? 0;
}
