using System;
using System.IO;
using UnityEngine;

namespace AgentEscape.VoiceAI
{
    /// <summary>
    /// API 키를 찾는다. 키는 코드, 씬, 프리팹에 넣지 않는다. 깃에 올라가면 바로 유출된다.
    /// 1순위: 프로젝트 폴더의 secrets/openai_api_key.txt (Assets 밖이라 빌드에 포함되지 않는다)
    /// 2순위: 환경 변수 OPENAI_API_KEY (Mac에서 Hub로 켠 Unity는 셸 환경 변수를 못 읽을 수 있다)
    /// </summary>
    public static class OpenAIKeyProvider
    {
        public static string RelativeKeyPath => Path.Combine("secrets", "openai_api_key.txt");

        public static string Load()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string path = Path.Combine(projectRoot, RelativeKeyPath);

            if (File.Exists(path))
            {
                string fromFile = File.ReadAllText(path).Trim();
                if (fromFile.Length > 0) return fromFile;
            }

            string fromEnvironment = Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (!string.IsNullOrWhiteSpace(fromEnvironment)) return fromEnvironment.Trim();

            throw new AgentApiException("OpenAI API 키가 없습니다. " + path + " 파일을 만들어 키를 넣어 주세요.");
        }
    }
}
