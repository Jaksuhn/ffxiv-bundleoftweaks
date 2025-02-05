using ECommons.EzIpcManager;

namespace Automaton.IPC;
#nullable disable
public class QuestionableIPC
{
    public const string Name = "Questionable";
    public const string Repo = "https://git.carvel.li/liza/";
    public QuestionableIPC() => EzIPC.Init(this, Name);

    [EzIPC] public Func<bool> IsRunning;
    [EzIPC] public Func<string> GetCurrentQuestId;
    [EzIPC] public Func<List<string>> GetCurrentlyActiveEventQuests;
    [EzIPC] public Func<string, bool> StartQuest;
    [EzIPC] public Func<string, bool> StartSingleQuest;
}
