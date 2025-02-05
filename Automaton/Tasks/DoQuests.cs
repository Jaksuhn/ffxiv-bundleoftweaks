using System.Threading.Tasks;

namespace Automaton.Tasks;
public sealed class DoQuests(List<string> questIds) : CommonTasks
{
    protected override async Task Execute()
    {
        foreach (var quest in questIds)
        {
            if (Service.Questionable.StartSingleQuest(quest))
                await WaitUntilThenFalse(() => Service.Questionable.IsRunning(), $"QuestionableWaitForFinish{quest}", 120);
            else
                Error($"Failed to start quest #{quest}");
        }
    }
}
