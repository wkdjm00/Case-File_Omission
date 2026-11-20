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

    /// <summary>「무작위」 2026-11-13 AI 제공자 중립 연결 및 실패 처리 구현 계약: 실패 유형을 구분해 기록한다.</summary>
    public enum AiFailureKind
    {
        None,
        Timeout,
        ConnectionFailed,
        Unauthorized,
        RateLimited,
        InvalidJson,
        SchemaError,
        ValidationFailed
    }

    /// <summary>요청 ID를 돌려주는 제공자 응답. 요청 ID가 지금 기다리는 요청과 다르면(늦게 온·중복 응답) 버린다.</summary>
    public sealed class AiProviderResponse
    {
        public string RequestId;
        public AiFailureKind Failure;
        public string Json;
    }

    /// <summary>
    /// 실패 유형·요청 ID를 알려 주는 제공자(배포 환경에서 구현). <see cref="IAiContentProvider"/>와 함께 구현하면 러너가 이쪽을 쓴다.
    /// </summary>
    public interface IAiContentProviderWithRequestId
    {
        AiProviderResponse Request(AiStage stage, string requestId, string requestJson);
    }

    public sealed class AiStageResult<T>
    {
        public bool Success;
        public T Value;
        public int Attempts;

        /// <summary>
        /// 실패 시 마지막 실패 코드: AI_TIMEOUT, AI_CONNECTION_FAILED, AI_AUTH_FAILED, AI_RATE_LIMITED,
        /// AI_RESPONSE_INVALID(잘못된 JSON), AI_SCHEMA_ERROR, AI_VALIDATION_FAILED, AI_STALE_RESPONSE(다른 요청 ID).
        /// </summary>
        public string FailureCode;

        /// <summary>이번 단계 호출에 쓴 요청 ID들(시도마다 하나).</summary>
        public readonly System.Collections.Generic.List<string> RequestIds = new System.Collections.Generic.List<string>();
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
            string generationId = Guid.NewGuid().ToString("N");
            for (int attempt = 1; attempt <= MaxAttemptsPerStage; attempt++)
            {
                result.Attempts = attempt;
                string requestId = $"{generationId}:{stage}:{attempt}";
                result.RequestIds.Add(requestId);
                string response;
                bool received;
                try
                {
                    if (provider is IAiContentProviderWithRequestId withId)
                    {
                        var r = withId.Request(stage, requestId, requestJson);
                        if (r == null) { received = false; response = null; }
                        else if (r.RequestId != requestId)
                        {
                            // 늦게 온·중복 응답: 지금 요청의 결과가 아니므로 버리고 다시 요청한다(확정 데이터를 덮어쓰지 않음).
                            result.FailureCode = "AI_STALE_RESPONSE";
                            logger?.LogError("AI_STALE_RESPONSE", "AI", GamePhase.Idle, $"{stage} 응답의 요청 ID가 달라 버립니다.", recoverable: true, contractId);
                            continue;
                        }
                        else if (r.Failure != AiFailureKind.None)
                        {
                            result.FailureCode = CodeOf(r.Failure);
                            logger?.LogError(result.FailureCode, "AI", GamePhase.Idle, $"{stage} {attempt}회차 실패: {r.Failure}", recoverable: true, contractId);
                            continue;
                        }
                        else { received = true; response = r.Json; }
                    }
                    else
                        received = provider.TryRequest(stage, requestJson, out response);
                }
                catch (TimeoutException e)
                {
                    received = false;
                    response = null;
                    result.FailureCode = "AI_TIMEOUT";
                    logger?.LogError("AI_TIMEOUT", "AI", GamePhase.Idle, $"{stage} 시간 초과: {e.Message}", recoverable: true, contractId);
                    continue;
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
                string invalidCode = "AI_VALIDATION_FAILED";
                try
                {
                    parsed = parseAndValidate(response);
                }
                catch (Exception)
                {
                    parsed = (false, default);
                    invalidCode = "AI_RESPONSE_INVALID"; // 해석할 수 없는 형식(잘못된 JSON 등)
                }

                if (parsed.valid)
                {
                    result.Success = true;
                    result.Value = parsed.value;
                    result.FailureCode = null;
                    return result;
                }
                result.FailureCode = invalidCode;
            }

            logger?.LogError(result.FailureCode, "AI", GamePhase.Idle, $"{stage} 단계가 {MaxAttemptsPerStage}회 안에 유효한 응답을 받지 못했습니다.", recoverable: true, contractId);
            return result;
        }

        public static string CodeOf(AiFailureKind kind) => kind switch
        {
            AiFailureKind.Timeout => "AI_TIMEOUT",
            AiFailureKind.ConnectionFailed => "AI_CONNECTION_FAILED",
            AiFailureKind.Unauthorized => "AI_AUTH_FAILED",
            AiFailureKind.RateLimited => "AI_RATE_LIMITED",
            AiFailureKind.InvalidJson => "AI_RESPONSE_INVALID",
            AiFailureKind.SchemaError => "AI_SCHEMA_ERROR",
            AiFailureKind.ValidationFailed => "AI_VALIDATION_FAILED",
            _ => null
        };

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
