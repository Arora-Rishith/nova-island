using Windows.Media.SpeechRecognition;

namespace NovaIsland;

public partial class MainWindow
{
    private SpeechRecognizer? speechRecognizer;
    private bool voiceListening;
    private string voiceStatus = "Voice commands are off";
    private static readonly string[] VoiceCommands =
    {
        "play music", "pause music", "next track", "previous track",
        "open quick settings", "open app shortcuts", "show notifications",
        "show devices", "open ai agent", "refresh agent status", "open calculator", "open files", "open notes", "close island"
    };

    private object GetVoiceState() => new
    {
        available = true,
        listening = voiceListening,
        status = voiceStatus,
        commands = VoiceCommands
    };

    private async Task StartVoiceCommandsAsync()
    {
        if (voiceListening) return;
        try
        {
            speechRecognizer ??= await CreateSpeechRecognizerAsync();
            voiceListening = true;
            voiceStatus = "Listening…";
            await speechRecognizer.ContinuousRecognitionSession.StartAsync();
        }
        catch (Exception ex)
        {
            voiceListening = false;
            voiceStatus = HasPackageIdentity
                ? "Microphone or speech language is unavailable."
                : "Install the MSIX to enable microphone voice commands.";
            throw new InvalidOperationException(voiceStatus, ex);
        }
        finally { PublishWebState(); }
    }

    private async Task<SpeechRecognizer> CreateSpeechRecognizerAsync()
    {
        var recognizer = new SpeechRecognizer();
        recognizer.Constraints.Add(new SpeechRecognitionListConstraint(VoiceCommands, "NOVA commands"));
        var compilation = await recognizer.CompileConstraintsAsync();
        if (compilation.Status != SpeechRecognitionResultStatus.Success)
        {
            recognizer.Dispose();
            throw new InvalidOperationException("Speech constraints could not be compiled: " + compilation.Status);
        }
        recognizer.ContinuousRecognitionSession.ResultGenerated += VoiceResultGenerated;
        recognizer.ContinuousRecognitionSession.Completed += VoiceSessionCompleted;
        return recognizer;
    }

    private async void VoiceResultGenerated(SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionResultGeneratedEventArgs args)
    {
        if (args.Result.Status != SpeechRecognitionResultStatus.Success) return;
        await Dispatcher.InvokeAsync(() => ExecuteVoiceCommand(args.Result.Text));
    }

    private void VoiceSessionCompleted(SpeechContinuousRecognitionSession sender,
        SpeechContinuousRecognitionCompletedEventArgs args)
    {
        Dispatcher.BeginInvoke(new Action(() =>
        {
            voiceListening = false;
            voiceStatus = args.Status == SpeechRecognitionResultStatus.Success ? "Voice commands are off" : "Voice session ended";
            PublishWebState();
        }));
    }

    private async Task StopVoiceCommandsAsync()
    {
        if (speechRecognizer == null || !voiceListening) return;
        try { await speechRecognizer.ContinuousRecognitionSession.StopAsync(); }
        finally
        {
            voiceListening = false;
            voiceStatus = "Voice commands are off";
            PublishWebState();
        }
    }

    private async void ExecuteVoiceCommand(string phrase)
    {
        switch (phrase.Trim().ToLowerInvariant())
        {
            case "play music":
            case "pause music":
                if (currentSession != null) await currentSession.TryTogglePlayPauseAsync();
                break;
            case "next track": if (currentSession != null) await currentSession.TrySkipNextAsync(); break;
            case "previous track": if (currentSession != null) await currentSession.TrySkipPreviousAsync(); break;
            case "open quick settings": NavigateToPage(3); AnimateIsland(true); break;
            case "open app shortcuts": NavigateToPage(3); AnimateIsland(true); break;
            case "show notifications": NavigateToPage(4); AnimateIsland(true); break;
            case "show devices": NavigateToPage(5); AnimateIsland(true); break;
            case "open ai agent": NavigateToPage(6); AnimateIsland(true); break;
            case "refresh agent status": await RefreshAiAsync(); break;
            case "open calculator": OpenAppShortcut("calculator"); break;
            case "open files": OpenAppShortcut("files"); break;
            case "open notes": OpenAppShortcut("notes"); break;
            case "close island": AnimateIsland(false); break;
        }
        voiceStatus = "Heard: " + phrase;
        PublishWebState();
    }

    private void DisposeVoiceCommands()
    {
        try { speechRecognizer?.ContinuousRecognitionSession?.CancelAsync().AsTask().GetAwaiter().GetResult(); }
        catch { }
        speechRecognizer?.Dispose();
        speechRecognizer = null;
    }
}
