namespace PrestigePerks.Perks;

/// <summary>
/// 베타 배포용 임시 규칙. 아직 코인을 얻을 방법이 없어 특전 시스템만 써 볼 수 있게 한다.
/// <c>-p:Beta=true</c> 로 빌드할 때만 켜진다 (tools\package.ps1 -Beta). 정식 배포 전에 이 파일과 호출부를 지운다.
/// - 슬롯: 프레스티지 단계와 상관없이 6개
/// - 리롤: 무료 횟수가 줄지 않는다 (무제한)
/// </summary>
public static class PerkBeta
{
#if BETA
    public static readonly bool Enabled = true;
#else
    public static readonly bool Enabled = false;
#endif

    public const int SlotLevel = 6;

    /// <summary>슬롯·특전 계산에 쓰는 단계. 베타는 6.</summary>
    public static int Level(int prestigeLevel) => Level(prestigeLevel, Enabled);

    internal static int Level(int prestigeLevel, bool enabled) => enabled ? SlotLevel : prestigeLevel;
}
