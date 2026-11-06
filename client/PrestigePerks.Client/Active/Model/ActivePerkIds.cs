// 클라이언트는 nullable 을 끄고 빌드한다. 이 파일을 함께 컴파일하는 테스트(nullable 켬)에서도 같게 본다
#nullable disable

namespace PrestigePerks.Model
{
    /// <summary>
    /// 내장 액티브 특전 ID (server PerkCatalog.ActiveIds 순서). 서버 state 를 받기 전에 만드는 F12 설정(개발자 시험 퍽 목록)에만 쓴다.
    /// 화면·레이드는 state 의 activeCatalog 를 쓴다. 서버 목록과 같은지 단위 테스트가 본다.
    /// </summary>
    public static class ActivePerkIds
    {
        public static readonly string[] All =
        {
            "firstaid", "secondwind", "sonarpulse", "steadfast", "focus",
            "emergencysurgery", "composure", "grit", "resupply", "fieldrepair",
        };
    }
}
