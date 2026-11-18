using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine.Networking;

namespace AgentEscape.VoiceAI
{
    /// <summary>OpenAI HTTP 호출 담당. 메인 스레드에서 호출한다.</summary>
    public sealed class OpenAIClient
    {
        private const string BaseUrl = "https://api.openai.com/v1/";
        private const int MaxAttempts = 2;
        private const int TimeoutSeconds = 30;

        private readonly string _apiKey;

        public OpenAIClient(string apiKey)
        {
            _apiKey = apiKey;
        }

        public async Task<JObject> CreateResponseAsync(JObject request, CancellationToken cancellation)
        {
            byte[] body = Encoding.UTF8.GetBytes(request.ToString(Newtonsoft.Json.Formatting.None));
            string text = await SendAsync("responses", body, "application/json", cancellation);
            return ReadJson(text);
        }

        public async Task<string> TranscribeAsync(byte[] wav, string model, string language, string prompt,
            string[] keywords, CancellationToken cancellation)
        {
            var form = new MultipartFormData();
            form.AddFile("file", "speech.wav", "audio/wav", wav);
            form.AddField("model", model);
            form.AddField("languages[]", language);
            if (!string.IsNullOrEmpty(prompt)) form.AddField("prompt", prompt);
            foreach (string keyword in keywords.Select(CleanKeyword).Where(k => k.Length > 0).Distinct().Take(32))
            {
                form.AddField("keywords[]", keyword);
            }

            string text = await SendAsync("audio/transcriptions", form.ToArray(), form.ContentType, cancellation);
            return ((string)ReadJson(text)["text"] ?? "").Trim();
        }

        private static JObject ReadJson(string text)
        {
            try { return JObject.Parse(text); }
            catch (Newtonsoft.Json.JsonException) { throw new AgentApiException("OpenAI 응답을 읽지 못했습니다."); }
        }

        // 키워드에 이 문자가 있으면 API가 요청 전체를 거부한다.
        private static string CleanKeyword(string keyword)
        {
            return new string((keyword ?? "").Where(c => c != '<' && c != '>' && c != '\r' && c != '\n').ToArray()).Trim();
        }

        private async Task<string> SendAsync(string path, byte[] body, string contentType, CancellationToken cancellation)
        {
            for (int attempt = 1; ; attempt++)
            {
                cancellation.ThrowIfCancellationRequested();
                using (var request = new UnityWebRequest(BaseUrl + path, UnityWebRequest.kHttpVerbPOST))
                {
                    request.uploadHandler = new UploadHandlerRaw(body) { contentType = contentType };
                    request.downloadHandler = new DownloadHandlerBuffer();
                    request.timeout = TimeoutSeconds;
                    request.SetRequestHeader("Authorization", "Bearer " + _apiKey);
                    request.SetRequestHeader("Content-Type", contentType);

                    var completed = new TaskCompletionSource<bool>();
                    request.SendWebRequest().completed += _ => completed.TrySetResult(true);
                    using (cancellation.Register(() => request.Abort()))
                    {
                        await completed.Task;
                    }
                    cancellation.ThrowIfCancellationRequested();

                    if (request.result == UnityWebRequest.Result.Success)
                    {
                        return request.downloadHandler.text;
                    }

                    long status = request.responseCode;
                    string errorCode = ReadErrorCode(request.downloadHandler?.text);
                    bool quotaExhausted = errorCode == "insufficient_quota";
                    bool retryable = !quotaExhausted && (status == 0 || status == 408 || status == 429 || status >= 500);

                    if (!retryable || attempt >= MaxAttempts)
                    {
                        throw new AgentApiException(DescribeError(status, errorCode));
                    }
                }

                await Task.Delay(500 * attempt * attempt, cancellation);
            }
        }

        private static string ReadErrorCode(string body)
        {
            if (string.IsNullOrEmpty(body)) return null;
            try { return (string)(JObject.Parse(body)["error"] as JObject)?["code"]; }
            catch (Newtonsoft.Json.JsonException) { return null; }
        }

        private static string DescribeError(long status, string errorCode)
        {
            if (errorCode == "insufficient_quota") return "OpenAI API 할당량 또는 결제 잔액을 확인해 주세요.";
            if (status == 401) return "OpenAI API 키 인증에 실패했습니다.";
            if (status == 403 || status == 404 || errorCode == "model_not_found")
                return "설정한 OpenAI 모델을 사용할 수 없습니다. 모델 ID와 API 프로젝트의 접근 권한을 확인해 주세요.";
            if (status == 429) return "OpenAI 요청 한도에 도달했습니다. 잠시 후 다시 지시해 주세요.";
            if (status == 0 || status == 408) return "OpenAI 연결이 끊겼거나 응답 시간이 초과됐습니다.";
            if (status == 400) return "OpenAI가 요청 형식을 받지 못했습니다. 모델의 구조화 출력과 요청 설정을 확인해 주세요.";
            if (status >= 500) return "OpenAI 서비스에 일시적인 문제가 있습니다.";
            return "OpenAI 요청에 실패했습니다. (HTTP " + status + ")";
        }
    }
}
