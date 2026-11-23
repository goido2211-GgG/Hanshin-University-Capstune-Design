using System;
using System.IO;
using System.Text;

namespace AgentEscape.VoiceAI
{
    /// <summary>multipart/form-data 본문 생성기. 같은 이름의 필드를 반복할 수 있다(languages[] 등).</summary>
    public sealed class MultipartFormData
    {
        private readonly MemoryStream _body = new MemoryStream();
        private readonly string _boundary = "----VoiceAI" + Guid.NewGuid().ToString("N");

        public string ContentType => "multipart/form-data; boundary=" + _boundary;

        public void AddField(string name, string value)
        {
            WriteText("--" + _boundary + "\r\n");
            WriteText("Content-Disposition: form-data; name=\"" + name + "\"\r\n\r\n");
            WriteText(value ?? "");
            WriteText("\r\n");
        }

        public void AddFile(string name, string fileName, string contentType, byte[] data)
        {
            WriteText("--" + _boundary + "\r\n");
            WriteText("Content-Disposition: form-data; name=\"" + name + "\"; filename=\"" + fileName + "\"\r\n");
            WriteText("Content-Type: " + contentType + "\r\n\r\n");
            _body.Write(data, 0, data.Length);
            WriteText("\r\n");
        }

        public byte[] ToArray()
        {
            using (var result = new MemoryStream())
            {
                _body.WriteTo(result);
                byte[] closing = Encoding.ASCII.GetBytes("--" + _boundary + "--\r\n");
                result.Write(closing, 0, closing.Length);
                return result.ToArray();
            }
        }

        private void WriteText(string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            _body.Write(bytes, 0, bytes.Length);
        }
    }
}
