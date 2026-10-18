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

            if (state.Dev)
            {
                LogCalculated(controller);
            }
        }

        private static readonly ESkillClass[] LoggedClasses = { ESkillClass.Physical, ESkillClass.Combat, ESkillClass.Practical, ESkillClass.Mental };

        /// <summary>
        /// 개발용: 게임이 실제로 계산한 효과를 남긴다. 입력 100 에 대한 결과라서 120 이면 ×1.2 (스킬 성장 특전 2·3·4, 경험치 1 확인용).
        /// </summary>
        private static void LogCalculated(BonusController controller)
        {
            try
            {
                var parts = LoggedClasses.Select(c => $"{c} {controller.Calculate(c, 100):0.##}")
                    .Append($"ExperienceRate {controller.Calculate(EBonusType.ExperienceRate, 100):0.##}");
                Log.Info($"[dev] bonus calc (input 100): {string.Join(", ", parts)}");
            }
            catch (Exception e)
            {
                Log.Warn($"[dev] bonus calc failed: {e.Message}");
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
