using System.Collections.Concurrent;
using PrestigePerks.Logging;
using SPTarkov.Common.Models.Logging;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.Models.Common;

namespace PrestigePerks.Perks;

/// <summary>
/// 프로필별 특전 데이터 (user/profileData/&lt;프로필ID&gt;/PrestigePerks.json). 메모리에 캐시하고, 바꿀 때는 <see cref="GetLock"/> 안에서 바꾼 뒤 <see cref="Save"/> 한다.
/// SaveServer 가 SaveLoadRouter 를 거쳐 이 클래스를 쓰므로 SPT 서비스에 의존하지 않는다 (순환 의존 방지).
/// </summary>
[Injectable(InjectionType.Singleton)]
public class PerkDataStore(ISptLogger<PerksFileLogger> logger)
{
    public const string FileName = "PrestigePerks.json";
    private const string ProfileDataDirectory = "user/profileData";

    private readonly ConcurrentDictionary<MongoId, PerkData> cache = new();
    private readonly ConcurrentDictionary<MongoId, object> locks = new();

    public static string GetPath(MongoId profileId) => Path.Combine(ProfileDataDirectory, profileId.ToString(), FileName);

    public object GetLock(MongoId profileId) => locks.GetOrAdd(profileId, _ => new object());

    /// <summary>캐시에서 꺼내고, 없으면 파일에서 동기로 읽는다. 파일이 없으면 빈 데이터 (파일은 만들지 않음).</summary>
    public PerkData Get(MongoId profileId) => cache.GetOrAdd(profileId, Load);

    public void Save(MongoId profileId, PerkData data)
    {
        if (data.LoadFailed)
        {
            throw new InvalidOperationException($"{profileId}: 특전 데이터를 읽지 못한 상태라 덮어쓰지 않습니다. 서버 로그를 확인하세요");
        }

        lock (GetLock(profileId))
        {
            PerkDataFile.Write(GetPath(profileId), data);
            cache[profileId] = data;
        }
    }

    /// <summary>
    /// 개발용 reset: 빈 데이터로 덮어쓴다. 직전 데이터는 .bak 으로 남아 되돌릴 수 있다.
    /// 파일을 지우기만 하면 다음 읽기가 본 파일이 없다고 .bak 에서 복구해 reset 이 무효가 된다.
    /// </summary>
    public void Delete(MongoId profileId)
    {
        lock (GetLock(profileId))
        {
            var data = new PerkData();
            PerkDataFile.Write(GetPath(profileId), data);
            cache[profileId] = data;
        }
    }

    private PerkData Load(MongoId profileId)
    {
        var path = GetPath(profileId);
        try
        {
            var result = PerkDataFile.Read(path);
            foreach (var warning in result.Warnings)
            {
                logger.Warning($"[PrestigePerks] {profileId}: {warning}");
            }

            switch (result.Source)
            {
                case PerkDataSource.Backup:
                    logger.Warning($"[PrestigePerks] {profileId}: 특전 데이터를 {FileName}{PerkDataFile.BackupSuffix} 에서 복구했습니다");
                    break;
                case PerkDataSource.Reset:
                    logger.Error($"[PrestigePerks] {profileId}: 특전 데이터와 백업이 모두 손상되어 빈 데이터로 시작합니다. 손상 파일: {result.CorruptCopyPath}");
                    break;
            }

            return result.Data;
        }
        catch (Exception ex)
        {
            // 로드 실패로 프로필 전체가 못 뜨면 안 된다. 이번 실행에서는 특전 없이 진행한다
            // 파일은 그대로 두고, 이 데이터로는 저장하지 않는다 (LoadFailed)
            logger.Error($"[PrestigePerks] {profileId}: {path} 을 읽는 중 오류. 특전 없이 진행합니다: {ex}");
            return new PerkData { LoadFailed = true };
        }
    }
}
