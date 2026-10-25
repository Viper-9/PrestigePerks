namespace PrestigePerks.Logging;

/// <summary>
/// ISptLogger&lt;PerksFileLogger&gt; 로 남긴 로그는 SPT 로그 파일(user/logs/spt)에만 쓰인다.
/// SPT 기본 sptLogger.json 의 콘솔 로거가 이름이 ".*FileLogger" 로 끝나는 로거를 빼기 때문이다.
/// </summary>
public sealed class PerksFileLogger;
