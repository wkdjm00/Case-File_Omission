using System;
using System.IO;
using CaseFileOmission.Core;
using UnityEngine;

namespace CaseFileOmission.SaveLoad
{
    /// <summary>
    /// 06 「2026-11-13 저장/불러오기·버전·복구 구현 계약」: 수동 저장 3슬롯 + 자동 저장 1슬롯(서로 덮어쓰지 않음).
    /// 쓰기는 임시 파일 → 교체(<see cref="SaveGameFileStore"/>), 교체 전 기존 파일을 백업(.bak)으로 남기고,
    /// 읽기에서 본 파일이 손상되면 백업으로 복구한다. 슬롯 번호: 자동 = 0, 수동 = 1~3.
    /// </summary>
    public sealed class SaveSlotStore
    {
        public const int AutoSlot = 0;
        public const int ManualSlotCount = 3;
        public const int CurrentSchemaVersion = 3;
        public const string CurrentContentVersion = "case-file-omission-2026-11-20-r2";

        private readonly string _directory;

        public SaveSlotStore(string directory)
        {
            if (string.IsNullOrEmpty(directory)) throw new ArgumentException("directory는 비어 있을 수 없습니다.", nameof(directory));
            _directory = directory;
        }

        public static bool IsValidSlot(int slot) => slot >= AutoSlot && slot <= ManualSlotCount;

        public string PathOf(int slot)
        {
            if (!IsValidSlot(slot)) throw new ArgumentOutOfRangeException(nameof(slot));
            return Path.Combine(_directory, slot == AutoSlot ? "save_auto.json" : $"save_{slot}.json");
        }

        public bool Exists(int slot) => IsValidSlot(slot) && (File.Exists(PathOf(slot)) || File.Exists(PathOf(slot) + ".bak"));

        /// <summary>메타데이터(슬롯·버전·시퀀스·시각)를 채워 저장한다. 같은 슬롯만 덮어쓴다.</summary>
        public void Write(int slot, SaveGameData data, string runId, int saveSequence)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            string path = PathOf(slot);
            data.Slot = slot;
            data.SchemaVersion = CurrentSchemaVersion;
            data.ContentVersion = CurrentContentVersion;
            data.BuildVersion = Application.version;
            data.RunId = runId;
            data.SaveSequence = saveSequence;
            data.SavedAtUtc = DateTime.UtcNow.ToString("o");

            if (File.Exists(path))
                File.Copy(path, path + ".bak", overwrite: true);
            SaveGameFileStore.Write(path, data);
        }

        /// <summary>슬롯을 읽는다. 본 파일이 없거나 손상되면 백업을 시도한다. 둘 다 실패하면 false(기록 남김).</summary>
        public bool TryRead(int slot, out SaveGameData data, GameLogger logger = null)
        {
            data = null;
            if (!IsValidSlot(slot)) return false;
            string path = PathOf(slot);
            if (File.Exists(path) && SaveGameFileStore.TryRead(path, out data, logger) && data.SchemaVersion <= CurrentSchemaVersion)
                return true;

            if (File.Exists(path + ".bak") && SaveGameFileStore.TryRead(path + ".bak", out data, logger) && data.SchemaVersion <= CurrentSchemaVersion)
            {
                logger?.LogError("SAVE_RESTORED_FROM_BACKUP", "SaveLoad", GamePhase.Idle, $"슬롯 {slot} 본 파일을 읽지 못해 백업으로 불러옵니다.", recoverable: true);
                return true;
            }
            data = null;
            return false;
        }

        /// <summary>슬롯 목록 표시용 요약(없으면 null).</summary>
        public SaveGameData Peek(int slot) => TryRead(slot, out var d) ? d : null;
    }
}
