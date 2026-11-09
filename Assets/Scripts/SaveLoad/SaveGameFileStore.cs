using System;
using System.IO;
using CaseFileOmission.Core;
using UnityEngine;

namespace CaseFileOmission.SaveLoad
{
    /// <summary>
    /// 저장 파일 입출력. 쓰기는 임시 파일에 먼저 쓴 뒤 교체해 중간에 끊겨도 기존 저장이 깨지지 않게 하고,
    /// 읽기는 파일이 없거나 JSON이 손상되었으면 false를 반환하고 기록을 남긴다(§12 "잘못된 저장 데이터가
    /// 게임 상태를 망가뜨리지 않는다").
    /// </summary>
    public static class SaveGameFileStore
    {
        public static void Write(string path, SaveGameData data)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path는 비어 있을 수 없습니다.", nameof(path));
            if (data == null) throw new ArgumentNullException(nameof(data));

            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            string tempPath = path + ".tmp";
            File.WriteAllText(tempPath, SaveGameService.ToJson(data));

            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }

        public static bool TryRead(string path, out SaveGameData data, GameLogger logger = null)
        {
            data = null;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                logger?.LogError("SAVE_FILE_NOT_FOUND", "SaveLoad", GamePhase.Idle, $"저장 파일이 없습니다: {path}", recoverable: true);
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    logger?.LogError("SAVE_FILE_EMPTY", "SaveLoad", GamePhase.Idle, "저장 파일이 비어 있습니다.", recoverable: true);
                    return false;
                }

                data = JsonUtility.FromJson<SaveGameData>(json);
            }
            catch (Exception e) when (e is ArgumentException || e is IOException || e is UnauthorizedAccessException)
            {
                logger?.LogError("SAVE_FILE_CORRUPT", "SaveLoad", GamePhase.Idle, $"저장 파일을 읽을 수 없습니다: {e.Message}", recoverable: true);
                data = null;
                return false;
            }

            if (data == null)
            {
                logger?.LogError("SAVE_FILE_CORRUPT", "SaveLoad", GamePhase.Idle, "저장 파일 내용을 해석할 수 없습니다.", recoverable: true);
                return false;
            }

            return true;
        }
    }
}
