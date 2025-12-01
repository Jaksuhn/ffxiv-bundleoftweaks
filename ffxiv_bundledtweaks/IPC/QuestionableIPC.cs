using ECommons.EzIpcManager;

namespace ComplexTweaks.IPC;
#nullable disable
[Ipc(Ipc.Questionable)]
public class QuestionableIPC : BaseIPC {
    public override string Name => "Questionable";
    public override string Repo => Punish;
    public QuestionableIPC() => EzIPC.Init(this, Name);

    [EzIPC] public Func<bool> IsRunning;
    [EzIPC] public Func<string> GetCurrentQuestId;
    [EzIPC] public Func<List<string>> GetCurrentlyActiveEventQuests;
    [EzIPC] public Func<string, bool> StartQuest;
    [EzIPC] public Func<string, bool> StartSingleQuest;
}
