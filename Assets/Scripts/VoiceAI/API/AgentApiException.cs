using System;

namespace AgentEscape.VoiceAI
{
    public sealed class AgentApiException : Exception
    {
        public AgentApiException(string message, Exception inner = null) : base(message, inner) { }
    }
}
