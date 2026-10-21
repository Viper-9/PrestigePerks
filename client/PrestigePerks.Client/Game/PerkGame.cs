using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Comfort.Common;
using EFT;
using EFT.UI;
using EFT.UI.DragAndDrop;
using PrestigePerks.Model;
using UnityEngine;

namespace PrestigePerks.Game
{
    /// <summary>게임 쪽 접근 (메인 세션, PMC 프로필, 코인 개수, 소리, 언어).</summary>
    internal static class PerkGame
    {
        /// <summary>프레스티지 코인 (server PrestigeCoinItem.TemplateId).</summary>
        public const string CoinTemplateId = "9fc8dc52dd7dbf55fc27ac09";

        public static IEftSession Session =>
            Singleton<ClientApplication<IEftSession>>.Instantiated
                ? Singleton<ClientApplication<IEftSession>>.Instance.GetClientBackEndSession()
                : null;

        /// <summary>메인 세션의 PMC 프로필.</summary>
        public static Profile Profile => Session?.Profile;

        /// <summary>인벤토리의 프레스티지 코인 수. 서버 결제(PaymentService)처럼 스태시·장비 전체를 센다. 표시용이고 실제 확인은 서버가 한다.</summary>
        public static int CountCoins()
        {
            try
            {
                return Profile?.Inventory?.GetAllItemByTemplate(CoinTemplateId).Sum(item => item.StackObjectsCount) ?? 0;
            }
            catch (Exception e)
            {
                Log.Warn($"coin count failed: {e.Message}");
                return 0;
            }
        }

        private static Sprite coinIcon;

        /// <summary>
        /// 프레스티지 코인 아이템 아이콘 (게임이 모델로 그린다). 인벤토리에 코인이 없으면 템플릿으로 화면용 아이템을 만들어 그린다
        /// (인벤토리에 넣지 않는다). 그래도 못 그리면 null.
        /// </summary>
        public static async Task<Sprite> CoinIcon()
        {
            if (coinIcon != null)
            {
                return coinIcon;
            }

            try
            {
                var coin = Profile?.Inventory?.GetAllItemByTemplate(CoinTemplateId).FirstOrDefault()
                           ?? (Singleton<ItemFactory>.Instantiated ? Singleton<ItemFactory>.Instance.CreateItem(MongoID.Generate(), CoinTemplateId, null) : null);
                if (coin == null)
                {
                    return null;
                }

                coinIcon = await ItemViewFactory.GetItemSpriteAsync(coin, 2);
                return coinIcon;
            }
            catch (Exception e)
            {
                Log.Warn($"coin icon failed: {e.Message}");
                return null;
            }
        }

        public static void Sound(EUISoundType sound)
        {
            try
            {
                if (Singleton<GUISounds>.Instantiated)
                {
                    Singleton<GUISounds>.Instance.PlayUISound(sound);
                }
            }
            catch (Exception)
            {
                // 소리는 없어도 된다
            }
        }

        /// <summary>게임 언어(LocalizationManager.Culture)의 문구. 파일이 없으면 en.</summary>
        public static PerkLocale LoadLocale()
        {
            string culture = null;
            try
            {
                culture = LocalizationManager.Instance?.Culture;
            }
            catch (Exception)
            {
                // 언어를 못 읽으면 en
            }

            var fallback = PerkLocale.ParseFile(ReadResource(PerkLocale.Fallback));
            var primary = string.IsNullOrEmpty(culture) ? null : PerkLocale.ParseFile(ReadResource(culture));
            return new PerkLocale(primary, fallback);
        }

        private static string ReadResource(string culture)
        {
            using (var stream = typeof(PerkGame).Assembly.GetManifestResourceStream($"PrestigePerks.Locales.{culture}.json"))
            {
                if (stream == null)
                {
                    return null;
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }
    }
}
