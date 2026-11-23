using System;

namespace AgentEscape.VoiceAI
{
    /// <summary>이번 턴에 요원이 어떻게 반응해야 하는지에 대한 게임 쪽 결정.</summary>
    public sealed class TurnDirective
    {
        public bool ForceMisunderstanding;
        /// <summary>agent_system.txt에 정의된 오해 유형 이름. 오해 턴이 아니면 null.</summary>
        public string MisunderstandingType;
    }

    /// <summary>
    /// LLM은 기본적으로 말을 너무 잘 알아듣는다. 그래서 '발암' 빈도는 LLM에게 맡기지 않고
    /// 코드가 정해서 알려준다. 연속 실패가 한도에 닿으면 반드시 알아듣게 해서 포기를 막는다.
    /// </summary>
    public sealed class FrustrationDirector
    {
        public static readonly string[] Types = { "literal", "wrong_target", "partial", "overdo", "mishear", "ask_back" };

        // 인덱스 = 혼란 레벨
        private static readonly double[] MisunderstandChance = { 0.0, 0.2, 0.4, 0.6 };
        private static readonly int[] MaxConsecutiveFailures = { 0, 1, 2, 3 };

        private readonly Random _random;
        private int _consecutiveFailures;
        private string _lastType;

        public FrustrationDirector(int? seed = null)
        {
            _random = seed.HasValue ? new Random(seed.Value) : new Random();
        }

        public int ConsecutiveFailures => _consecutiveFailures;

        public TurnDirective Decide(int confusionLevel)
        {
            int level = Math.Max(0, Math.Min(MisunderstandChance.Length - 1, confusionLevel));

            if (_consecutiveFailures >= MaxConsecutiveFailures[level] ||
                _random.NextDouble() >= MisunderstandChance[level])
            {
                return new TurnDirective();
            }

            string type;
            do { type = Types[_random.Next(Types.Length)]; } while (type == _lastType);
            _lastType = type;

            return new TurnDirective { ForceMisunderstanding = true, MisunderstandingType = type };
        }

        /// <summary>LLM이 보고한 이해 결과로 연속 실패 횟수를 갱신한다.</summary>
        public void Report(string comprehension)
        {
            if (comprehension == AgentVocabulary.Understood) _consecutiveFailures = 0;
            else _consecutiveFailures++;
        }

        public void Reset()
        {
            _consecutiveFailures = 0;
            _lastType = null;
        }
    }
}
