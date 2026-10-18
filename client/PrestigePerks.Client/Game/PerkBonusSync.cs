using System;
using System.Collections.Generic;
using System.Linq;
using EFT;
using PrestigePerks.Model;

namespace PrestigePerks.Game
{
    /// <summary>
    /// 서버가 보낸 특전 Bonus 를 메인 세션 PMC 의 BonusController 에 반영한다. 재접속 없이 효과가 바로 바뀐다 (dev-docs 2_특전.md 6절 "클라이언트 즉시 반영").
    /// bonusIds 에 해당하는 Bonus 를 모두 빼고 bonuses 를 넣으므로 여러 번 불러도 결과가 같다.
    /// </summary>
    internal static class PerkBonusSync
    {
        public static void Apply(PerkStateDto state)
        {
            var controller = PerkGame.Profile?.BonusController;
            if (controller == null || state == null)
            {
                return;
            }

            var ids = new HashSet<string>(state.BonusIds);
            foreach (var descriptor in controller.ToDescriptor().Where(d => ids.Contains(d.Id.ToString())).ToList())
            {
                try
                {
                    controller.RemoveBonus(descriptor.ToBonus());
                }
                catch (Exception e)
                {
                    Log.Warn($"remove bonus {descriptor.Id} failed: {e.Message}");
                }
            }

            foreach (var bonus in state.Bonuses)
            {
                var descriptor = ToDescriptor(bonus);
                if (descriptor == null)
                {
                    Log.Warn($"unknown bonus from server: {bonus.Type} {bonus.SkillType} ({bonus.Id})");
                    continue;
                }

                try
                {
                    controller.AddBonus(descriptor.ToBonus());
                }
                catch (Exception e)
                {
                    Log.Warn($"add bonus {bonus.Id} failed: {e.Message}");
                }
            }
        }

        private static BonusDescriptor ToDescriptor(PerkBonusDto bonus)
        {
            if (string.IsNullOrEmpty(bonus.Id) || !Enum.TryParse(bonus.Type, out EBonusType type))
            {
                return null;
            }

            ESkillClass? skill = null;
            if (!string.IsNullOrEmpty(bonus.SkillType))
            {
                if (!Enum.TryParse(bonus.SkillType, out ESkillClass parsed))
                {
                    return null;
                }

                skill = parsed;
            }

            return new BonusDescriptor
            {
                BonusType = type,
                Id = new MongoID(bonus.Id),
                Value = bonus.Value,
                IsVisible = bonus.Visible ?? true,
                Passive = bonus.Passive ?? true,
                Production = bonus.Production ?? false,
                SkillType = skill,
            };
        }
    }
}
