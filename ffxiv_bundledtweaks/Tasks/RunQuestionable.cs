using FFXIVClientStructs.FFXIV.Client.Game.UI;
using System.Threading.Tasks;

namespace ComplexTweaks.Tasks;

public sealed class RunQuestionable(List<string> questIds, bool returnHome) : CommonTasks {
    protected override async Task Execute() {
        foreach (var quest in questIds) {
            Status = $"Doing quest #{quest}";
            if (Service.Questionable.StartSingleQuest(quest))
                await WaitWhile(() => !IsQuestComplete(quest), $"QuestionableWaitForFinish{quest}", 120);
            else
                Error($"Failed to start quest #{quest}");
        }
        if (returnHome) {
            Status = "Going home";
            Service.Lifestream.ExecuteCommand("auto");
            await WaitUntilThenFalse(() => Service.Lifestream.IsBusy(), "LifestreamWaitForFinish");
        }
    }

    private unsafe bool IsQuestComplete(string questId) {
        if (uint.TryParse(questId, out var id) && Game.IsQuestComplete(id))
            return true;
        if (questId.StartsWith('U') && ushort.TryParse(questId.AsSpan(1), out var unlockLinkId) && UIState.Instance()->IsUnlockLinkUnlocked(unlockLinkId))
            return true;
        return false;
    }
}
