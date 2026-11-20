using System;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace CaseFileOmission.Core
{
    /// <summary>
    /// OpenAI-compatible Chat Completions transport for the existing AI-stage contract.
    /// Credentials and endpoint are read from environment variables, never stored in project files.
    /// </summary>
    public sealed class OpenAiCompatibleAiContentProvider : IAiContentProviderWithRequestId
    {
        public const string EndpointEnvironmentVariable = "CASEFILE_AI_ENDPOINT";
        public const string ApiKeyEnvironmentVariable = "CASEFILE_AI_API_KEY";
        public const string ModelEnvironmentVariable = "CASEFILE_AI_MODEL";

        private const int DefaultTimeoutMilliseconds = 8000;

        private readonly string _endpoint;
        private readonly string _apiKey;
        private readonly string _model;
        private readonly int _timeoutMilliseconds;

        public OpenAiCompatibleAiContentProvider(string endpoint, string apiKey, string model, int timeoutMilliseconds = DefaultTimeoutMilliseconds)
        {
            if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("AI endpoint is required.", nameof(endpoint));
            if (string.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("AI API key is required.", nameof(apiKey));
            if (string.IsNullOrWhiteSpace(model)) throw new ArgumentException("AI model is required.", nameof(model));
            if (timeoutMilliseconds < 1000) throw new ArgumentOutOfRangeException(nameof(timeoutMilliseconds));

            _endpoint = NormalizeEndpoint(endpoint);
            _apiKey = apiKey.Trim();
            _model = model.Trim();
            _timeoutMilliseconds = timeoutMilliseconds;
        }

        /// <summary>
        /// Creates a configured provider when all environment variables are present.
        /// Returns null when the deployment has not supplied AI configuration, leaving the deterministic fallback active.
        /// </summary>
        public static OpenAiCompatibleAiContentProvider CreateFromEnvironmentOrNull()
        {
            string endpoint = Environment.GetEnvironmentVariable(EndpointEnvironmentVariable);
            string apiKey = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            string model = Environment.GetEnvironmentVariable(ModelEnvironmentVariable);

            if (string.IsNullOrWhiteSpace(endpoint) || string.IsNullOrWhiteSpace(apiKey) || string.IsNullOrWhiteSpace(model))
                return null;

            try
            {
                return new OpenAiCompatibleAiContentProvider(endpoint, apiKey, model);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        public AiProviderResponse Request(AiStage stage, string requestId, string requestJson)
        {
            if (string.IsNullOrWhiteSpace(requestId) || string.IsNullOrWhiteSpace(requestJson))
                return new AiProviderResponse { RequestId = requestId, Failure = AiFailureKind.InvalidJson };

            try
            {
                var request = (HttpWebRequest)WebRequest.Create(_endpoint);
                request.Method = "POST";
                request.ContentType = "application/json; charset=utf-8";
                request.Accept = "application/json";
                request.Timeout = _timeoutMilliseconds;
                request.ReadWriteTimeout = _timeoutMilliseconds;
                request.Headers[HttpRequestHeader.Authorization] = "Bearer " + _apiKey;

                byte[] payload = Encoding.UTF8.GetBytes(BuildPayload(stage, requestId, requestJson));
                request.ContentLength = payload.Length;

                using (var requestStream = request.GetRequestStream())
                    requestStream.Write(payload, 0, payload.Length);

                using (var response = (HttpWebResponse)request.GetResponse())
                using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                {
                    string responseJson = reader.ReadToEnd();
                    if (!TryExtractContent(responseJson, out string modelContent))
                        return new AiProviderResponse { RequestId = requestId, Failure = AiFailureKind.InvalidJson };
                    return new AiProviderResponse { RequestId = requestId, Failure = AiFailureKind.None, Json = modelContent };
                }
            }
            catch (WebException e)
            {
                return new AiProviderResponse
                {
                    RequestId = requestId,
                    Failure = ClassifyWebFailure(e)
                };
            }
            catch (TimeoutException)
            {
                return new AiProviderResponse { RequestId = requestId, Failure = AiFailureKind.Timeout };
            }
            catch (IOException)
            {
                return new AiProviderResponse { RequestId = requestId, Failure = AiFailureKind.ConnectionFailed };
            }
            catch (Exception)
            {
                return new AiProviderResponse { RequestId = requestId, Failure = AiFailureKind.ConnectionFailed };
            }
        }

        private string BuildPayload(AiStage stage, string requestId, string requestJson)
        {
            string systemText =
                "You are the structured content service for the Case File Omission game. " +
                "Follow the JSON request exactly. Return only the JSON value required by that request; do not use Markdown fences.";
            string userText =
                "REQUEST_ID: " + requestId + "\n" +
                "STAGE: " + stage + "\n" +
                "REQUEST_JSON:\n" + requestJson;

            return "{"
                + "\"model\":\"" + EscapeJson(_model) + "\","
                + "\"messages\":["
                + "{\"role\":\"system\",\"content\":\"" + EscapeJson(systemText) + "\"},"
                + "{\"role\":\"user\",\"content\":\"" + EscapeJson(userText) + "\"}"
                + "],"
                + "\"temperature\":0.2,"
                + "\"response_format\":{\"type\":\"json_object\"}"
                + "}";
        }

        private static string NormalizeEndpoint(string endpoint)
        {
            string value = endpoint.Trim().TrimEnd('/');
            if (value.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase))
                return value;
            if (value.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
                return value + "/chat/completions";
            return value + "/v1/chat/completions";
        }

        private static AiFailureKind ClassifyWebFailure(WebException exception)
        {
            if (exception.Status == WebExceptionStatus.Timeout)
                return AiFailureKind.Timeout;

            var response = exception.Response as HttpWebResponse;
            if (response == null)
                return AiFailureKind.ConnectionFailed;

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.Unauthorized || response.StatusCode == HttpStatusCode.Forbidden)
                    return AiFailureKind.Unauthorized;
                if ((int)response.StatusCode == 429)
                    return AiFailureKind.RateLimited;
                if (response.StatusCode == HttpStatusCode.RequestTimeout || (int)response.StatusCode == 504)
                    return AiFailureKind.Timeout;
                return AiFailureKind.ConnectionFailed;
            }
        }

        private static string EscapeJson(string value)
        {
            var builder = new StringBuilder(value.Length + 16);
            foreach (char ch in value)
            {
                switch (ch)
                {
                    case '"': builder.Append("\\\""); break;
                    case '\\': builder.Append("\\\\"); break;
                    case '\b': builder.Append("\\b"); break;
                    case '\f': builder.Append("\\f"); break;
                    case '\n': builder.Append("\\n"); break;
                    case '\r': builder.Append("\\r"); break;
                    case '\t': builder.Append("\\t"); break;
                    default:
                        if (ch < 0x20)
                            builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                        else
                            builder.Append(ch);
                        break;
                }
            }
            return builder.ToString();
        }

        private static bool TryExtractContent(string responseJson, out string content)
        {
            content = null;
            if (string.IsNullOrWhiteSpace(responseJson))
                return false;

            int choices = responseJson.IndexOf("\"choices\"", StringComparison.Ordinal);
            if (choices >= 0)
            {
                int message = responseJson.IndexOf("\"message\"", choices, StringComparison.Ordinal);
                int contentKey = responseJson.IndexOf("\"content\"", message >= 0 ? message : choices, StringComparison.Ordinal);
                if (contentKey >= 0 && TryReadJsonString(responseJson, contentKey, out content))
                    return !string.IsNullOrWhiteSpace(content);
            }

            int outputText = responseJson.IndexOf("\"output_text\"", StringComparison.Ordinal);
            if (outputText >= 0 && TryReadJsonString(responseJson, outputText, out content))
                return !string.IsNullOrWhiteSpace(content);

            return false;
        }

        private static bool TryReadJsonString(string json, int propertyIndex, out string value)
        {
            value = null;
            int colon = json.IndexOf(':', propertyIndex);
            if (colon < 0)
                return false;

            int start = colon + 1;
            while (start < json.Length && char.IsWhiteSpace(json[start])) start++;
            if (start >= json.Length || json[start] != '"')
                return false;

            start++;
            var builder = new StringBuilder();
            for (int i = start; i < json.Length; i++)
            {
                char ch = json[i];
                if (ch == '"')
                {
                    value = builder.ToString();
                    return true;
                }

                if (ch != '\\')
                {
                    builder.Append(ch);
                    continue;
                }

                if (++i >= json.Length)
                    return false;

                switch (json[i])
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (i + 4 >= json.Length) return false;
                        string hex = json.Substring(i + 1, 4);
                        if (!int.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int codePoint))
                            return false;
                        builder.Append((char)codePoint);
                        i += 4;
                        break;
                    default:
                        return false;
                }
            }

            return false;
        }
    }
}
