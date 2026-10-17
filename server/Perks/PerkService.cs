using PrestigePerks.Items;
using PrestigePerks.Saving;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Profile;
using SPTarkov.Server.Core.Servers;

namespace PrestigePerks.Perks;

/// <summary>
/// 특전 화면 흐름 (열기·잠금·리롤·확정). 규칙은 <see cref="PerkRules"/>, 저장은 <see cref="PerkDataStore"/>.
/// HTTP 라우트(<c>PerkStaticRouter</c>)와 개발용 Commando 명령이 같이 쓴다.
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PerkService(
    ISptLogger<PerkService> logger,
    PerkDataStore store,
    PerkConfigLoader configLoader,
    SaveServer saveServer,
    PrestigeCoinMailer coinMailer)
{
    public const string ConfirmMailMessage = "PRESTIGE PERKS: remaining coins";

    private readonly Random random = Random.Shared;

    private PerkConfig Config => configLoader.Config;

    /// <summary>게임 시작: 필요하면 화면을 열고, 특전 Bonus 를 다시 넣는다 (다른 모드가 Bonuses 를 새로 만들었을 때 대비).</summary>
    public void OnGameStart(MongoId sessionId)
    {
        if (!TryGetProfile(sessionId, out var profile) || profile.ProfileInfo?.IsWiped == true)
        {
            return;
        }

        lock (store.GetLock(sessionId))
        {
            OpenIfNeeded(sessionId, profile);
            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
        }
    }

    /// <summary>현재 상태. 보유 슬롯 &lt; 단계면 화면을 연다.</summary>
    public PerkState GetState(MongoId sessionId) =>
        Run(sessionId, (profile, data, level) =>
        {
            OpenIfNeeded(sessionId, profile);
            return BuildState(data, level);
        });

    /// <summary>슬롯이 꽉 차 있어도 화면을 연다 (재추첨권·개발용). 자동 추첨 없이 무료 리롤부터.</summary>
    public PerkState Open(MongoId sessionId, string reason) =>
        Run(sessionId, (_, data, level) =>
        {
            var error = PerkRules.OpenSession(data, level, reason, Config, random);
            if (error == PerkError.None)
            {
                store.Save(sessionId, data);
            }

            return BuildState(data, level, error);
        });

    public PerkState SetLock(MongoId sessionId, int slotIndex, bool locked) =>
        Run(sessionId, (_, data, level) =>
        {
            var error = PerkRules.SetLock(data, slotIndex, locked);
            if (error == PerkError.None)
            {
                store.Save(sessionId, data);
            }

            return BuildState(data, level, error);
        });

    public PerkState Roll(MongoId sessionId) =>
        Run(sessionId, (_, data, level) =>
        {
            var outcome = PerkRules.Roll(data, level, Config, random);
            if (outcome.Success)
            {
                store.Save(sessionId, data);
            }

            return BuildState(data, level, outcome.Error) with { RollCost = outcome.Cost };
        });

    /// <summary>확정: 특전을 저장하고, 남은 코인을 메일로 보내고, Bonus 를 다시 넣는다.</summary>
    public PerkState Confirm(MongoId sessionId) =>
        Run(sessionId, (profile, data, level) =>
        {
            var error = PerkRules.Confirm(data, out var coins);
            if (error != PerkError.None)
            {
                return BuildState(data, level, error);
            }

            // 잔액 0 을 먼저 저장해 메일이 두 번 나가지 않게 한다
            store.Save(sessionId, data);
            if (coins > 0 && !coinMailer.TrySend(sessionId, coins, ConfirmMailMessage))
            {
                logger.Error($"[PrestigePerks] {sessionId}: 확정 후 코인 {coins}개를 메일로 보내지 못했습니다 (코인 아이템 미등록). 잔액으로 되돌립니다");
                data.CoinBalance = coins;
                store.Save(sessionId, data);
                coins = 0;
            }

            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
            logger.Info($"[PrestigePerks] {sessionId}: 특전 확정 [{string.Join(", ", data.Perks)}], 코인 {coins}개 메일 지급");
            return BuildState(data, level) with { CoinsMailed = coins };
        });

    /// <summary>개발용: 코인 잔액을 정한다 (프레스티지 환산이 아직 없음).</summary>
    public PerkState SetBalance(MongoId sessionId, int balance) =>
        Run(sessionId, (_, data, level) =>
        {
            data.CoinBalance = Math.Max(balance, 0);
            store.Save(sessionId, data);
            return BuildState(data, level);
        });

    /// <summary>개발용: 특전 데이터를 지우고 Bonus 를 뺀다. 직전 파일은 .bak 으로 남는다.</summary>
    public PerkState Reset(MongoId sessionId) =>
        Run(sessionId, (profile, _, level) =>
        {
            store.Delete(sessionId);
            PerkSaveLoadRouter.ApplyPerkBonuses(profile, store);
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

    private void OpenIfNeeded(MongoId sessionId, SptProfile profile)
    {
        var data = store.Get(sessionId);
        var level = GetPrestigeLevel(profile);
        if (data.LoadFailed || !PerkRules.NeedsSession(data, level))
        {
            return;
        }

        if (PerkRules.OpenSession(data, level, PerkSessionReason.Prestige, Config, random) == PerkError.None)
        {
            store.Save(sessionId, data);
            logger.Info($"[PrestigePerks] {sessionId}: 특전 화면 열림 (P{level}, 보유 {data.Perks.Count}칸)");
        }
    }

    private PerkState BuildState(PerkData data, int level, PerkError error = PerkError.None) =>
        new()
        {
            PrestigeLevel = level,
            Perks = data.Perks.ToList(),
            ActivePerks = PerkRules.ActivePerks(data, level),
            CoinBalance = data.CoinBalance,
            Session = data.Session,
            NextRerollCost = data.Session is null ? null : PerkRules.NextRerollCost(Config, level, data.Session),
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
