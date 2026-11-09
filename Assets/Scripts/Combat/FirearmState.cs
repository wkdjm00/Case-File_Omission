using System;

namespace CaseFileOmission.Combat
{
    /// <summary>06페이지 2026-11-09 확정: 기본 무기는 권총 1종, 계약당 탄약 3발.</summary>
    public sealed class FirearmState
    {
        public const int StartingAmmo = 3;

        public int CurrentAmmo { get; private set; } = StartingAmmo;

        public bool CanFire => CurrentAmmo > 0;

        public void Fire()
        {
            if (!CanFire)
                throw new InvalidOperationException("탄약이 없습니다.");
            CurrentAmmo--;
        }

        /// <summary>재장전은 행동 1회를 소모한다(확정). 행동 소모량 자체는 상위 턴/행동 시스템이 적용한다.</summary>
        public void Reload()
        {
            CurrentAmmo = StartingAmmo;
        }
    }
}
