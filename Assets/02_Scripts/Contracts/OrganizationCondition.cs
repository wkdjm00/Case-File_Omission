using System;

namespace CaseFileOmission.Contracts
{
    /// <summary>
    /// 조직 조건(= 조직 목표). 의뢰 수락 전에 공개되고 의뢰 시작 후에는 변경할 수 없다(05페이지 확정).
    /// <see cref="Lock"/> 이후 조건 내용(종류·대상·전달 요구)을 바꾸려 하면 예외를 던진다. 달성 여부는 사건 종료 시 판정하므로 잠금 대상이 아니다.
    /// </summary>
    [Serializable]
    public sealed class OrganizationCondition
    {
        private OrganizationConditionType _type = OrganizationConditionType.None;
        private string _targetId;
        private bool _requiresDeliveryToOrganization;

        public OrganizationConditionType Type
        {
            get => _type;
            set { EnsureUnlocked(); _type = value; }
        }

        /// <summary>회수 대상(단서 ID)이나 공식 범인 지정 대상(NPC ID). 미특정이면 null — 조사 중에 찾아야 한다.</summary>
        public string TargetId
        {
            get => _targetId;
            set { EnsureUnlocked(); _targetId = value; }
        }

        private readonly System.Collections.Generic.List<string> _targetIds = new System.Collections.Generic.List<string>();

        /// <summary>대상이 여럿인 조건(범인 특정 단서 2개 은폐)의 대상 단서 ID 목록. 잠금 뒤 변경 불가.</summary>
        public System.Collections.Generic.IReadOnlyList<string> TargetIds => _targetIds;

        public void SetTargetIds(System.Collections.Generic.IEnumerable<string> ids)
        {
            EnsureUnlocked();
            _targetIds.Clear();
            foreach (var id in ids ?? System.Linq.Enumerable.Empty<string>())
                if (!string.IsNullOrEmpty(id) && !_targetIds.Contains(id)) _targetIds.Add(id);
        }

        /// <summary>회수만 요구하는지, 조직에 전달까지 요구하는지 구분.</summary>
        public bool RequiresDeliveryToOrganization
        {
            get => _requiresDeliveryToOrganization;
            set { EnsureUnlocked(); _requiresDeliveryToOrganization = value; }
        }

        /// <summary>null = 아직 판정 전, true = 달성, false = 미달성.</summary>
        public bool? Achieved;

        /// <summary>의뢰가 시작되어 조건이 고정되었는지.</summary>
        public bool IsLocked { get; private set; }

        public bool HasCondition => Type != OrganizationConditionType.None;

        /// <summary>의뢰 수락 시 호출한다. 이후 조건 내용은 바뀌지 않는다.</summary>
        public void Lock() => IsLocked = true;

        private void EnsureUnlocked()
        {
            if (IsLocked)
                throw new InvalidOperationException("의뢰가 시작된 뒤에는 조직 조건을 변경할 수 없습니다.");
        }
    }
}
