using System;
using System.Collections.Generic;

namespace CaseFileOmission.Combat
{
    /// <summary>
    /// 04페이지 확정: 사격 이벤트는 위치·표적·명중여부·표적상태변화·목격자·소리도달범위·흔적·
    /// 각 NPC의 실제 정보 획득 여부를 기록한다. 목격자 판정과 소리 전파는 공간·NPC 시스템이
    /// 아직 없어 호출자가 입력으로 제공하며, 여기서는 그 결과를 "기록"만 한다.
    /// </summary>
    [Serializable]
    public sealed class ShotEvent
    {
        public string EventId;
        public string ShooterId;
        public string TargetId;
        public bool Hit;
        public string TargetStateChangeNote;
        public readonly List<string> WitnessNpcIds = new List<string>();
        public float SoundReachRadius;
        public bool LeftTrace;
    }
}
