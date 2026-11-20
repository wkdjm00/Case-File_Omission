using System.Collections.Generic;
using System.Linq;
using CaseFileOmission.Core;
using NUnit.Framework;

namespace CaseFileOmission.Tests
{
    /// <summary>「무작위」 2026-11-13 AI 제공자 중립 연결 및 실패 처리 계약: 실패 유형 구분, 요청 ID로 늦은·중복 응답 무시, 3회 한도.</summary>
    public class AiFailureContractTests
    {
        private sealed class ScriptedProvider : IAiContentProvider, IAiContentProviderWithRequestId
        {
            private readonly Queue<System.Func<string, AiProviderResponse>> _script;
            public ScriptedProvider(params System.Func<string, AiProviderResponse>[] steps) => _script = new Queue<System.Func<string, AiProviderResponse>>(steps);
            public AiProviderResponse Request(AiStage stage, string requestId, string requestJson) => _script.Dequeue()(requestId);

            public bool TryRequest(AiStage stage, string requestJson, out string responseJson)
            {
                responseJson = null;
                return false; // 요청 ID 경로만 쓴다
            }
        }

        private static (bool, string) Parse(string json) => (AiStageRunner.ReadStringField(json, "ok") == "yes", json);

        [Test]
        public void StaleResponse_IsIgnored_ThenFreshResponseAccepted()
        {
            var provider = new ScriptedProvider(
                id => new AiProviderResponse { RequestId = "old-request", Json = "{\"ok\":\"yes\"}" },
                id => new AiProviderResponse { RequestId = id, Json = "{\"ok\":\"yes\"}" });
            var logger = new GameLogger();

            var result = AiStageRunner.Run(provider, AiStage.CoreTruth, "{}", Parse, logger);

            Assert.IsTrue(result.Success);
            Assert.AreEqual(2, result.Attempts, "다른 요청 ID의 응답은 쓰지 않고 다시 요청한다");
            Assert.That(logger.Entries.Any(e => e.ErrorCode == "AI_STALE_RESPONSE"));
        }

        [Test]
        public void FailureKinds_AreRecordedSeparately_AndStopAfterThreeAttempts()
        {
            var provider = new ScriptedProvider(
                id => new AiProviderResponse { RequestId = id, Failure = AiFailureKind.Timeout },
                id => new AiProviderResponse { RequestId = id, Failure = AiFailureKind.RateLimited },
                id => new AiProviderResponse { RequestId = id, Failure = AiFailureKind.SchemaError });
            var logger = new GameLogger();

            var result = AiStageRunner.Run(provider, AiStage.CaseMaterials, "{}", Parse, logger);

            Assert.IsFalse(result.Success);
            Assert.AreEqual(3, result.Attempts);
            Assert.AreEqual("AI_SCHEMA_ERROR", result.FailureCode);
            Assert.AreEqual(3, result.RequestIds.Distinct().Count(), "시도마다 다른 요청 ID");
            CollectionAssert.IsSubsetOf(new[] { "AI_TIMEOUT", "AI_RATE_LIMITED", "AI_SCHEMA_ERROR" }, logger.Entries.Select(e => e.ErrorCode).ToList());
        }

        [Test]
        public void ValidButRejectedResponse_IsValidationFailure_NotUsed()
        {
            var provider = new ScriptedProvider(
                id => new AiProviderResponse { RequestId = id, Json = "{\"ok\":\"no\"}" },
                id => new AiProviderResponse { RequestId = id, Json = "{\"ok\":\"no\"}" },
                id => new AiProviderResponse { RequestId = id, Json = "{\"ok\":\"no\"}" });

            var result = AiStageRunner.Run(provider, AiStage.ReportText, "{}", Parse);

            Assert.IsFalse(result.Success);
            Assert.IsNull(result.Value, "검증에 실패한 응답은 값으로 쓰지 않는다");
            Assert.AreEqual("AI_VALIDATION_FAILED", result.FailureCode);
        }
    }
}
