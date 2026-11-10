using System;

namespace CaseFileOmission.Combat
{
    /// <summary>
    /// 06페이지 2026-11-09 확정: 기본 무기는 권총 1종, 계약당 탄약 3발, 재장전은 행동 1회를 소모한다.
    /// "계약당 3발"은 한 의뢰에서 쓸 수 있는 총량이다 — 재장전은 남은 탄약을 장전할 뿐 총량을 늘리지 않는다.
    /// 장탄수(한 번에 장전되는 수)는 원문에 없어 <see cref="MagazineCapacity"/>(임시값 3)로 둔다. 장탄수가 3이면
    /// 의뢰 시작 시 전부 장전되어 있으므로 재장전이 실제로 필요해지는 경우는 장탄수가 확정된 뒤에 생긴다.
    /// </summary>
    public sealed class FirearmState
    {
        public const int AmmoPerContract = 3;

        /// <summary>호환용 별칭.</summary>
        public const int StartingAmmo = AmmoPerContract;

        /// <summary>임시값(원문에 없음). 한 번에 장전되는 탄 수.</summary>
        public int MagazineCapacity { get; }

        /// <summary>지금 장전되어 바로 쏠 수 있는 탄.</summary>
        public int CurrentAmmo { get; private set; }

        /// <summary>이번 의뢰에서 아직 장전하지 않은 탄.</summary>
        public int ReserveAmmo { get; private set; }

        public bool CanFire => CurrentAmmo > 0;

        public bool CanReload => ReserveAmmo > 0 && CurrentAmmo < MagazineCapacity;

        public FirearmState(int magazineCapacity = AmmoPerContract)
        {
            if (magazineCapacity < 1) throw new ArgumentOutOfRangeException(nameof(magazineCapacity));
            MagazineCapacity = magazineCapacity;
            ResetForContract();
        }

        /// <summary>새 의뢰를 시작할 때 호출한다. 그 의뢰의 탄약 3발이 주어진다.</summary>
        public void ResetForContract()
        {
            CurrentAmmo = Math.Min(MagazineCapacity, AmmoPerContract);
            ReserveAmmo = AmmoPerContract - CurrentAmmo;
        }

        public void Fire()
        {
            if (!CanFire)
                throw new InvalidOperationException("탄약이 없습니다.");
            CurrentAmmo--;
        }

        /// <summary>
        /// 남은 탄약을 장전한다. 행동 1회 소모(시간 비용)는 상위 행동 시스템이 적용한다. 장전할 탄이 없으면 false.
        /// </summary>
        public bool Reload()
        {
            if (!CanReload)
                return false;

            int load = Math.Min(MagazineCapacity - CurrentAmmo, ReserveAmmo);
            CurrentAmmo += load;
            ReserveAmmo -= load;
            return true;
        }

        /// <summary>저장/불러오기 전용.</summary>
        public void RestoreAmmo(int current, int reserve)
        {
            CurrentAmmo = Math.Max(0, Math.Min(MagazineCapacity, current));
            ReserveAmmo = Math.Max(0, Math.Min(AmmoPerContract - CurrentAmmo, reserve));
        }
    }
}
