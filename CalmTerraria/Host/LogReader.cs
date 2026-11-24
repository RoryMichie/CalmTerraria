using log4net;
using Srui;

namespace CalmTerraria.Host;

/// <summary>Writes every utterance to the mod's log as it is spoken,
/// so what the player heard can be read back from client.log after a
/// session - the transcript is the interface, and this is the only
/// record of it outside a test.</summary>
internal sealed class LogReader : IReader
{
    private readonly ILog _log;
    private readonly SpeechVerbosity _verbosity = new();

    private readonly Action<string> _heard;

    public LogReader(ILog log, Action<string> heard)
    {
        _log = log;
        _heard = heard;
    }

    public void OnTick(IReadOnlyList<AccessibilityEvent> events)
    {
        foreach (var text in SpeechRenderer.Default.RenderTick(events, _verbosity))
        {
            _log.Info($"Spoke: {text}");
            _heard(text);
        }
    }

    public void OnInterrupt()
    {
    }
}
