using System;
using System.Collections.Generic;

namespace CaseFileOmission.Combat
{
    /// <summary>
    /// 06 2026-11-09 확정: 사격 이벤트는 시각·위치·표적·명중 여부·표적 상태 변화·목격자·소리 도달 범위·흔적·
    /// 각 NPC의 실제 정보 획득 여부를 기록한다. 사격·목격·흔적 발견·정보 전달은 서로 다른 이벤트이며,
    /// 사격만으로 경찰 의심도를 올리지 않는다. 목격자 판정과 소리 전파 규칙은 원문에 없어 호출자가 채운다.
    /// </summary>
    [Serializable]
    public sealed class ShotEvent
    {
        public string EventId;
        public string ShooterId;
        public string TargetId;
        public bool Hit;

        /// <summary>사격이 일어난 방.</summary>
        public string RoomId;

        public DateTime OccurredAtUtc;

        public string TargetStateChangeNote;
        public readonly List<string> WitnessNpcIds = new List<string>();
        public float SoundReachRadius;
        public bool LeftTrace;

        /// <summary>이 사격으로 실제로 정보를 얻은 NPC.</summary>
        public readonly List<string> InfoAcquiredByNpcIds = new List<string>();
    }
}
