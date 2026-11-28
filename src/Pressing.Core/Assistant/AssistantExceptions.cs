namespace Pressing.Core.Assistant;

public class AssistantException(string message, Exception? innerException = null)
    : Exception(message, innerException);

public sealed class AssistantEngineUnavailableException(string message, Exception? innerException = null)
    : AssistantException(message, innerException);

public sealed class AssistantAudioException(string message, Exception? innerException = null)
    : AssistantException(message, innerException);

public sealed class AssistantContractException(string message, Exception? innerException = null)
    : AssistantException(message, innerException);
