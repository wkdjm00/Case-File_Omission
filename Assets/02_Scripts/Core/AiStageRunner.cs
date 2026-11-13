using System;

namespace CaseFileOmission.Core
{
    /// <summary>04 「무작위」 계약 v1 §5의 AI 단계.</summary>
    public enum AiStage
    {
        CoreTruth,
        CaseMaterials,
        ReportText,
        ReportAnswerValidation
    }

    /// <summary>
    /// AI 제공자 연결부. 실제 제공자·URL·비밀키·사용량 제한은 출시 배포 환경에서 설정한다(기획 정리본 2026-11-13) — 여기서는 인터페이스만 둔다.
    /// false는 타임아웃·연결 실패처럼 응답 자체를 받지 못한 경우다.
    /// </summary>
    public interface IAiContentProvider
    {
        bool TryRequest(AiStage stage, string requestJson, out string responseJson);
    }

    public sealed class AiStageResult<T>
    {
        public bool Success;
        public T Value;
        public int Attempts;

        /// <summary>실패 시: AI_CONNECTION_FAILED 또는 AI_RESPONSE_INVALID(형식·스키마·검증 실패).</summary>
        public string FailureCode;
    }

    /// <summary>
    /// 04 확정: 각 AI 단계는 최초 호출 포함 최대 3회. 연결 실패·잘못된 JSON·스키마/검증 실패는 다시 요청하고, 3회를 넘기면 실패로 돌려준다.
    /// 잘못된 응답은 값으로 쓰지 않는다 — 실제 진실·확정 데이터를 덮어쓰지 않도록 호출자는 Success일 때만 결과를 반영한다.
    /// 실패는 GameLogger에 남기고 플레이어에게는 노출하지 않는다(§15).
    /// </summary>
    public static class AiStageRunner
    {
        public const int MaxAttemptsPerStage = 3;

        public static AiStageResult<T> Run<T>(
            IAiContentProvider provider,
            AiStage stage,
            string requestJson,
            Func<string, (bool Valid, T Value)> parseAndValidate,
            GameLogger logger = null,
            string contractId = null)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (parseAndValidate == null) throw new ArgumentNullException(nameof(parseAndValidate));

            var result = new AiStageResult<T>();
            for (int attempt = 1; attempt <= MaxAttemptsPerStage; attempt++)
            {
                result.Attempts = attempt;
                string response;
                bool received;
                try
                {
                    received = provider.TryRequest(stage, requestJson, out response);
                }
                catch (Exception e)
                {
                    received = false;
                    response = null;
                    logger?.LogError("AI_CONNECTION_FAILED", "AI", GamePhase.Idle, $"{stage} 호출 예외: {e.Message}", recoverable: true, contractId);
                }

                if (!received || string.IsNullOrWhiteSpace(response))
                {
                    result.FailureCode = "AI_CONNECTION_FAILED";
                    continue;
                }

                (bool valid, T value) parsed;
                try
                {
                    parsed = parseAndValidate(response);
                }
                catch (Exception)
                {
                    parsed = (false, default);
                }

                if (parsed.valid)
                {
                    result.Success = true;
                    result.Value = parsed.value;
                    result.FailureCode = null;
                    return result;
                }
                result.FailureCode = "AI_RESPONSE_INVALID";
            }

            logger?.LogError(result.FailureCode, "AI", GamePhase.Idle, $"{stage} 단계가 {MaxAttemptsPerStage}회 안에 유효한 응답을 받지 못했습니다.", recoverable: true, contractId);
            return result;
        }

        /// <summary>단순 JSON에서 문자열 필드 하나를 읽는다(중첩 객체·이스케이프 없는 짧은 응답용). 없으면 null.</summary>
        public static string ReadStringField(string json, string field)
        {
            if (json == null) return null;
            string key = "\"" + field + "\"";
            int k = json.IndexOf(key, StringComparison.Ordinal);
            if (k < 0) return null;
            int colon = json.IndexOf(':', k + key.Length);
            if (colon < 0) return null;
            int start = json.IndexOf('"', colon + 1);
            if (start < 0) return null;
            int end = json.IndexOf('"', start + 1);
            return end < 0 ? null : json.Substring(start + 1, end - start - 1);
        }
    }
}
