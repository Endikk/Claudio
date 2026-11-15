using Claudio.Core.Models;

namespace Claudio.Core.Design;

/// <summary>
/// Claudy seen from the front for the agent room, in the inks of the app's own sprite (the shared
/// mascot palette): <c>C</c> body, <c>T</c> lit top, <c>D</c> shade, <c>o</c> outline, <c>E</c>
/// eyes. 16 by 14. Seated poses have no arms: the desk draws them on the keyboard.
/// </summary>
/// <remarks>
/// Claudy keeps it beside its painter; it is here, with no Windows type in it, so the room's view
/// model (which places a lounger by <see cref="Rows"/>) can use it wherever it lives.
/// </remarks>
public static class RoomSprite
{
    public enum Pose
    {
        Rest,
        Blink,
        StepA,
        StepB,
        Sit,
        SitBlink,
    }

    public const int Columns = 16;
    public const int Rows = 14;

    private static readonly string[] Body =
    [
        "..oooooooooooo..",
        ".oTTTTTTTTTTTTo.",
        ".oTTTTTTTTTTTTo.",
        ".oCCCCCCCCCCCCo.",
        ".oCCECCCCCCECCo.",
        ".oCCECCCCCCECCo.",
        ".oCCCCCCCCCCCCo.",
        ".oCCCCCCCCCCCDo.",
        ".oCCCCCCCCCCCDo.",
        ".oDDDDDDDDDDDDo.",
        ".oooooooooooooo.",
    ];

    /// <summary>Rows 6 to 8 of a standing Claudy: an arm on each side.</summary>
    private static readonly string[] Arms =
    [
        "ooCCCCCCCCCCCCoo",
        "oCCCCCCCCCCCCDDo",
        "ooCCCCCCCCCCCDoo",
    ];

    private static readonly string[] LegsAtRest = [".oCooCo..oDooDo.", ".oCooCo..oDooDo.", ".oooooo..oooooo."];
    private static readonly string[] LegsStepA = [".oCooCo..oDooDo.", ".oCo.oo..oDo.oo.", ".ooo.....ooo...."];
    private static readonly string[] LegsStepB = [".oCooCo..oDooDo.", ".oo.oCo..oo.oDo.", "....ooo.....ooo."];

    private static readonly Dictionary<Pose, IReadOnlyList<string>> Frames =
        Enum.GetValues<Pose>().ToDictionary(pose => pose, Build);

    public static IReadOnlyList<string> Frame(Pose pose) => Frames.TryGetValue(pose, out var rows) ? rows : [];

    private static IReadOnlyList<string> Build(Pose pose)
    {
        var rows = Body.ToList();
        var isSeated = pose is Pose.Sit or Pose.SitBlink;
        if (!isSeated)
        {
            rows.RemoveRange(6, 3);
            rows.InsertRange(6, Arms);
        }
        if (pose is Pose.Blink or Pose.SitBlink)
        {
            rows[4] = rows[4].Replace('E', 'C');
        }
        rows.AddRange(pose switch
        {
            Pose.StepA => LegsStepA,
            Pose.StepB => LegsStepB,
            _ => LegsAtRest,
        });
        return rows;
    }
}

/// <summary>
/// The symbol on a screen of the agent room: one per kind of work, and one per state a screen can
/// be in. The names are Claudy's SF Symbols, so a scene reads the same in both apps; Claudio draws
/// each with its Segoe Fluent Icons counterpart.
/// </summary>
public static class RoomGlyph
{
    public const string Waiting = "hand.raised.fill";
    public const string Stalled = "pause.fill";
    public const string Done = "checkmark";
    public const string Thinking = "ellipsis";
    public const string Delegating = "person.2.fill";

    /// <summary>
    /// Every symbol the room can draw, and the colours and sizes it draws them in: the canvas
    /// resolves each pair once instead of on every frame.
    /// </summary>
    public static IReadOnlyList<string> Names { get; } =
        Enum.GetValues<AgentWork>().Select(Symbol).Concat([Waiting, Stalled, Done, Thinking, Delegating])
            .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    public static IReadOnlyList<(uint Color, double Points)> Inks { get; } =
    [
        (0xC8E4FF, 13), (0x5E7C93, 13), (0x1F1A17, 13), (0xF0604D, 13), (0x4CC38A, 13), (0x1F1A17, 12),
    ];

    public static string Tag(string name, uint color, double points) =>
        FormattableString.Invariant($"{name}|{color}|{(int)points}");

    public static string Symbol(AgentWork work) => work switch
    {
        AgentWork.Code => "chevron.left.forwardslash.chevron.right",
        AgentWork.Docs => "doc.text.fill",
        AgentWork.Plan => "checklist",
        AgentWork.Design => "paintbrush.pointed.fill",
        AgentWork.Search => "magnifyingglass",
        AgentWork.Review => "eye.fill",
        AgentWork.Web => "globe",
        AgentWork.Terminal => "terminal.fill",
        AgentWork.Tests => "testtube.2",
        AgentWork.Build => "hammer.fill",
        AgentWork.Benchmark => "speedometer",
        AgentWork.Install => "shippingbox.fill",
        AgentWork.Git => "arrow.triangle.branch",
        AgentWork.Data => "cylinder.split.1x2.fill",
        AgentWork.Infra => "cloud.fill",
        AgentWork.Deploy => "paperplane.fill",
        AgentWork.Security => "lock.shield.fill",
        AgentWork.Delegating => Delegating,
        AgentWork.Skill => "book.closed.fill",
        AgentWork.Mcp => "powerplug.fill",
        _ => Thinking,
    };
}
