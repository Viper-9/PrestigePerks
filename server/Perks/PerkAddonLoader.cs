namespace PrestigePerks.Perks;

/// <summary>add-on 읽기 결과. Sources 는 perks.json 을 읽은 add-on 폴더 이름, Warnings 는 그동안 낸 경고 전체 (개발용 명령 prestige perk addons).</summary>
public sealed record PerkAddonReport(
    IReadOnlyList<PerkDefinition> Perks,
    IReadOnlyDictionary<string, byte[]> Icons,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Sources)
{
    public static PerkAddonReport Empty { get; } = new([], new Dictionary<string, byte[]>(), [], []);
}

/// <summary>
/// 서버 폴더 perks/&lt;add-on&gt;/perks.json (+ 아이콘 PNG) 를 읽는다 (dev-docs 95). 서버 시작 때 한 번이고 핫 리로드는 없다.
/// 폴더 이름 순(Ordinal)으로 읽고, 앞선 add-on 과 ID 가 겹치면 뒤의 것을 건너뛴다. 아이콘만 잘못됐으면 아이콘만 빼고 특전은 살린다.
/// </summary>
public static class PerkAddonLoader
{
    public const int MaxIconBytes = 256 * 1024;

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47];

    /// <summary>서버 시작 때 읽은 결과 (개발용 명령이 읽는다).</summary>
    public static PerkAddonReport Last { get; set; } = PerkAddonReport.Empty;

    public static PerkAddonReport Load(string perksFolder, Action<string> warn)
    {
        if (!Directory.Exists(perksFolder))
        {
            return PerkAddonReport.Empty;
        }

        var warnings = new List<string>();
        void Warn(string message)
        {
            warnings.Add(message);
            warn(message);
        }

        var perks = new List<PerkDefinition>();
        var icons = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var sources = new List<string>();
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var folder in Directory.GetDirectories(perksFolder).OrderBy(Path.GetFileName, StringComparer.Ordinal))
        {
            var source = Path.GetFileName(folder);
            var file = Path.Combine(folder, "perks.json");
            if (!File.Exists(file))
            {
                Warn($"perks/{source}: perks.json 이 없습니다. 이 add-on 은 건너뜁니다");
                continue;
            }

            string json;
            try
            {
                json = File.ReadAllText(file);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Warn($"perks/{source}/perks.json: 읽지 못했습니다 ({error.Message}). 이 add-on 은 건너뜁니다");
                continue;
            }

            sources.Add(source);
            foreach (var perk in PerkAddonFile.Parse(json, source, Warn))
            {
                if (!ids.Add(perk.Id))
                {
                    Warn($"perks/{source}/perks.json: {perk.Id}: 앞선 add-on 과 ID 가 겹칩니다. 이 특전은 건너뜁니다");
                    continue;
                }

                perks.Add(perk);
                if (perk.Addon?.IconPath is not { } iconPath)
                {
                    continue;
                }

                if (ReadIcon(folder, iconPath, out var reason) is { } png)
                {
                    icons[perk.Id] = png;
                }
                else
                {
                    Warn($"perks/{source}/perks.json: {perk.Id}: 아이콘 {iconPath} {reason}. 아이콘만 빼고 기본 아이콘을 씁니다");
                }
            }
        }

        return new PerkAddonReport(perks, icons, warnings, sources);
    }

    // add-on 폴더 안의 PNG (256KB 이하) 만 받는다
    private static byte[]? ReadIcon(string folder, string path, out string reason)
    {
        reason = "";
        var root = Path.GetFullPath(folder) + Path.DirectorySeparatorChar;
        string full;
        try
        {
            full = Path.GetFullPath(Path.Combine(folder, path));
        }
        catch (Exception error) when (error is ArgumentException or NotSupportedException or PathTooLongException)
        {
            reason = "경로가 잘못됐습니다";
            return null;
        }

        if (Path.IsPathRooted(path) || !full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            reason = "는 add-on 폴더 안의 상대 경로여야 합니다";
            return null;
        }

        try
        {
            var info = new FileInfo(full);
            if (!info.Exists)
            {
                reason = "파일이 없습니다";
                return null;
            }

            if (info.Length > MaxIconBytes)
            {
                reason = $"이 {MaxIconBytes / 1024}KB 를 넘습니다";
                return null;
            }

            var bytes = File.ReadAllBytes(full);
            if (bytes.Length < PngSignature.Length || !bytes.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            {
                reason = "가 PNG 가 아닙니다";
                return null;
            }

            return bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            reason = $"을 읽지 못했습니다 ({error.Message})";
            return null;
        }
    }
}
