using ECommons.Automation.NeoTaskManager;

namespace Automaton.Services;
public class Service
{
    public static Provider Provider { get; private set; } = null!;
    public static AutoRetainerApi AutoRetainerApi { get; private set; } = null!;
    public static AutoRetainerIPC AutoRetainerIPC { get; private set; } = null!;
    public static BossModIPC BossMod { get; private set; } = null!;
    public static DeliverooIPC Deliveroo { get; private set; } = null!;
    public static GearsetterIPC Gearsetter { get; private set; } = null!;
    public static LifestreamIPC Lifestream { get; private set; } = null!;
    public static NavmeshIPC Navmesh { get; private set; } = null!;
    public static QuestionableIPC Questionable { get; private set; } = null!;

    public static AddonObserver AddonObserver { get; private set; } = null!;
    public static Automation Automation { get; private set; } = null!;
    public static Memory Memory { get; private set; } = null!;
    public static TaskManager TaskManager { get; private set; } = null!;
}
