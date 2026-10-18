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
/// 특전 흐름 (새 단계 진입·잠금·리롤). 규칙은 <see cref="PerkRules"/>, 저장은 <see cref="PerkDataStore"/>.
/// HTTP 라우트(<c>PerkStaticRouter</c>), 리롤 아이템 이벤트(<c>PerkItemEventRouter</c>), 개발용 Commando 명령이 같이 쓴다.
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

    /// <summary>
    /// 흔들 수 있는 리롤: 리롤한 시각과 돌린(잠기지 않은) 슬롯. 리롤마다 한 번, <see cref="PerkConfig.ShakeWindowSeconds"/> 안에만.
    /// 릴이 도는 몇 초 동안만 의미가 있어 저장하지 않는다 (서버를 다시 켜면 사라진다).
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<MongoId, (DateTime At, HashSet<int> Slots)> pendingShakes = new();

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
            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
        }
    }

    /// <summary>현재 상태. 새 단계면 처리한다. <paramref name="markNewPerksSeen"/> 면 새 특전 알림을 이번 응답으로 넘기고 지운다 (클라이언트 페이지용).</summary>
    public PerkState GetState(MongoId sessionId, bool markNewPerksSeen = false) =>
        Run(sessionId, (profile, data, level) =>
        {
            EnterLevel(sessionId, profile);
            var state = BuildState(data, level);
            if (markNewPerksSeen && data.UnseenNewPerks.Count > 0)
            {
                data.UnseenNewPerks.Clear();
                store.Save(sessionId, data);
            }

            return state;
        });

    public PerkState SetLock(MongoId sessionId, int slotIndex, bool locked) =>
        Run(sessionId, (_, data, level) =>
        {
            var error = PerkRules.SetLock(data, level, slotIndex, locked);
            if (error == PerkError.None)
            {
                store.Save(sessionId, data);
            }

            return BuildState(data, level, error);
        });

    /// <summary>
    /// 리롤 (아이템 이벤트). 유료면 인벤토리 코인을 바닐라 결제(<see cref="PaymentService.AddPaymentToOutput(PmcData, MongoId, double, MongoId, ItemEventRouterResponse, IReadOnlySet{MongoId})"/>)로
    /// 빼서 <paramref name="output"/> 에 담는다. 클라이언트는 응답의 ProfileChanges 로 스태시를 바닐라처럼 고친다.
    /// </summary>
    public ItemEventRouterResponse RollWithCoins(MongoId sessionId, PmcData pmcData, PerkRollRequest request, ItemEventRouterResponse output)
    {
        var state = Run(sessionId, (profile, data, level) =>
        {
            EnterLevel(sessionId, profile);
            var error = PerkRules.CheckRoll(data, level);
            if (error != PerkError.None)
            {
                return BuildState(data, level, error);
            }

            var cost = PerkRules.NextRerollCost(Config, level, data);
            if (cost != request.ExpectedCost)
            {
                return BuildState(data, level, PerkError.CostChanged);
            }

            if (cost > 0)
            {
                var warningsBefore = output.Warnings?.Count ?? 0;
                var preferred = (request.Items ?? []).Where(id => id.IsValidMongoId()).Select(id => new MongoId(id)).ToHashSet();
                paymentService.AddPaymentToOutput(pmcData, new MongoId(PrestigeCoinItem.TemplateId), cost, sessionId, output, preferred);
                if ((output.Warnings?.Count ?? 0) > warningsBefore)
                {
                    // 결제 함수가 이미 경고를 붙였다 (코인 부족). 인벤토리는 바뀌지 않았다
                    return BuildState(data, level, PerkError.NotEnoughCoins);
                }
            }

            var outcome = PerkRules.Roll(data, level, Config, random);
            store.Save(sessionId, data);
            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
            OfferShake(sessionId, data, level);
            logger.Info($"[PrestigePerks] {sessionId}: 리롤 ({(cost == 0 ? "무료" : $"코인 {cost}")}) → [{string.Join(", ", PerkRules.ActivePerks(data, level))}]");
            return BuildState(data, level) with { RollCost = outcome.Cost };
        });

        if (state.Error is { } failure && failure != nameof(PerkError.NotEnoughCoins))
        {
            httpResponseUtil.AppendErrorToOutput(output, $"PrestigePerks: {failure}");
        }

        return output;
    }

    /// <summary>
    /// 흔들기: 직전 리롤에서 돌린 슬롯 하나(멈추려는 릴)를 확률로 한 칸 더(다음 칸 특전) 보내거나, 드물게 리버스(다른 특전)한다.
    /// 리롤마다 한 번, <see cref="PerkConfig.ShakeWindowSeconds"/> 안에만.
    /// </summary>
    public PerkState Shake(MongoId sessionId, int slotIndex, int nextPerk) =>
        Run(sessionId, (profile, data, level) =>
        {
            if (!pendingShakes.TryRemove(sessionId, out var pending)
                || DateTime.UtcNow - pending.At > TimeSpan.FromSeconds(Config.ShakeWindowSeconds)
                || !pending.Slots.Contains(slotIndex))
            {
                return BuildState(data, level, PerkError.NoShake);
            }

            var before = data.Slots[slotIndex].Perk;
            var outcome = PerkRules.Shake(data, level, slotIndex, nextPerk, Config.ShakeChance, Config.ShakeReverseChance, random);
            if (outcome != ShakeOutcome.Stay)
            {
                store.Save(sessionId, data);
                PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
            }

            logger.Info($"[PrestigePerks] {sessionId}: 흔들기 슬롯 {slotIndex + 1} → {outcome} ({before} → {data.Slots[slotIndex].Perk})");
            return BuildState(data, level) with { ShakeOutcome = outcome.ToString() };
        });

    private void OfferShake(MongoId sessionId, PerkData data, int level)
    {
        var rolled = PerkRules.ActiveSlots(data, level).Select((slot, index) => (slot, index)).Where(pair => !pair.slot.Locked).Select(pair => pair.index).ToHashSet();
        pendingShakes[sessionId] = (DateTime.UtcNow, rolled);
    }

    /// <summary>개발용: 슬롯에 특전을 직접 넣는다 (특전 효과를 비교해 보려고). 다른 슬롯에 있던 특전이면 서로 바꾼다.</summary>
    public PerkState SetPerk(MongoId sessionId, int slotIndex, int perk) =>
        Run(sessionId, (profile, data, level) =>
        {
            EnterLevel(sessionId, profile);
            var error = PerkRules.SetPerk(data, level, slotIndex, perk);
            if (error == PerkError.None)
            {
                store.Save(sessionId, data);
                PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
                logger.Info($"[PrestigePerks] {sessionId}: 개발용 set 슬롯 {slotIndex + 1} → {perk}, [{string.Join(", ", PerkRules.ActivePerks(data, level))}]");
            }

            return BuildState(data, level, error);
        });

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
                PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
                OfferShake(sessionId, data, level);
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

    /// <summary>개발용: 특전 데이터를 빈 데이터로 덮어쓰고 Bonus 를 뺀다 (슬롯·잠금·무료/유료 리롤 모두). 직전 파일은 .bak 으로 남는다. 다음 state·게임 시작 때 새로 뽑는다.</summary>
    public PerkState Reset(MongoId sessionId) =>
        Run(sessionId, (profile, _, level) =>
        {
            store.Delete(sessionId);
            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
            // 바로 지금 단계로 다시 들어가 슬롯을 뽑는다 (무료 리롤 3회, 새 특전은 다음 state 에서 알림)
            EnterLevel(sessionId, profile);
            return BuildState(store.Get(sessionId), level);
        });

    /// <summary>개발용: 메모리 PMC 프로필에 지금 들어 있는 특전 Bonus ("type value" 목록). 저장 파일에는 없어야 한다.</summary>
    public IReadOnlyList<string> GetAppliedBonuses(MongoId sessionId) =>
        TryGetProfile(sessionId, out var profile)
            ? (profile.CharacterData!.PmcData!.Bonuses ?? [])
                .Where(PerkBonusApplier.IsPerkBonus)
                .Select(bonus => $"{bonus.Type}{(bonus.SkillType is { } skill ? $"({skill})" : "")} {bonus.Value:+0;-0}")
                .ToList()
            : [];

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

    /// <summary>새 단계면 횟수를 되돌리고 빈 슬롯을 뽑는다. 바뀌었으면 저장하고 Bonus 를 다시 넣는다. 잠금 안에서 부른다.</summary>
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
        PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
        logger.Info($"[PrestigePerks] {sessionId}: P{level} 진입 (무료 리롤 {data.Rerolls.FreeLeft}회){(newPerks.Count > 0 ? $", 새 특전 [{string.Join(", ", newPerks)}]" : "")}");
    }

    private PerkState BuildState(PerkData data, int level, PerkError error = PerkError.None) =>
        new()
        {
            PrestigeLevel = level,
            Slots = PerkRules.ActiveSlots(data, level).Select(slot => new PerkSlot { Perk = slot.Perk, Locked = slot.Locked }).ToList(),
            ActivePerks = PerkRules.ActivePerks(data, level),
            FreeRerollsLeft = data.Rerolls.FreeLeft,
            PaidRerolls = data.Rerolls.Paid,
            NextRerollCost = PerkRules.NextRerollCost(Config, level, data),
            CostFixed = data.Rerolls.FreeLeft <= 0 && data.Rerolls.Paid + 1 >= Config.PaidCostCapRound,
            NewPerks = data.UnseenNewPerks.ToList(),
            Bonuses = PerkBonusApplier.CreateBonuses(PerkRules.ActivePerks(data, level)),
            BonusIds = BonusIds,
            ShakeChance = Config.ShakeChance,
            Dev = DevBuild,
            Error = error == PerkError.None ? null : error.ToString(),
        };

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
