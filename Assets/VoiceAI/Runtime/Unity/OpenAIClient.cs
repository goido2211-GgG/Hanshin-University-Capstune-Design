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
        private const int MaxAttempts = 3;
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
            return JObject.Parse(text);
        }

        public async Task<string> TranscribeAsync(byte[] wav, string model, string language, string prompt,
            string[] keywords, CancellationToken cancellation)
        {
            var form = new MultipartFormData();
            form.AddFile("file", "speech.wav", "audio/wav", wav);
            form.AddField("model", model);
            form.AddField("languages[]", language);
            if (!string.IsNullOrEmpty(prompt)) form.AddField("prompt", prompt);
            foreach (string keyword in keywords.Select(CleanKeyword).Where(k => k.Length > 0).Distinct())
            {
                form.AddField("keywords[]", keyword);
            }

            string text = await SendAsync("audio/transcriptions", form.ToArray(), form.ContentType, cancellation);
            return ((string)JObject.Parse(text)["text"] ?? "").Trim();
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
                    string responseText = request.downloadHandler?.text ?? "";
                    bool quotaExhausted = responseText.Contains("insufficient_quota");
                    bool retryable = !quotaExhausted && (status == 0 || status == 408 || status == 429 || status >= 500);

                    if (!retryable || attempt >= MaxAttempts)
                    {
                        throw new AgentApiException(DescribeError(status, responseText, request.error, quotaExhausted));
                    }
                }

                await Task.Delay(500 * attempt * attempt, cancellation);
            }
        }

        private static string DescribeError(long status, string body, string transportError, bool quotaExhausted)
        {
            if (quotaExhausted) return "OpenAI 충전 잔액이 부족합니다.";
            if (status == 401) return "API 키가 올바르지 않습니다.";
            if (status == 0) return "네트워크 연결 실패: " + transportError;

            string detail = body;
            try { detail = (string)JObject.Parse(body)["error"]?["message"] ?? body; }
            catch (Newtonsoft.Json.JsonException) { }
            return "OpenAI 오류 " + status + ": " + detail;
        }
    }
}
