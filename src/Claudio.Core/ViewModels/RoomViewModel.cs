using System.Text;
using Claudio.Core.Design;
using Claudio.Core.Models;

namespace Claudio.Core.ViewModels;

/// <summary>
/// What the agent room keeps across launches, as Claudy keeps it in <c>UserDefaults</c>: the rooms
/// put away and the weather chosen for the window. The App keeps it in its settings file; the
/// tests in memory.
/// </summary>
public interface IRoomStore
{
    /// <summary>The rooms put away, by name. Claudy's <c>roomHidden</c>.</summary>
    IReadOnlyList<string> HiddenRooms { get; set; }

    /// <summary>One of <see cref="RoomWeathers.Id"/>, or <see cref="RoomWeathers.AsItComes"/>. Claudy's <c>roomWeather</c>.</summary>
    string WeatherChoice { get; set; }
}

/// <summary>
/// Drives the agent room, as Claudy's <c>RoomViewModel</c>: one room per project, the sessions open
/// in it as Claudy at the centre desk, their agents at the desks around. It reads what
/// <see cref="AgentsViewModel"/> already knows and adds what a room needs: who sits where, who is
/// walking, who is done and resting on the sofa, which area is changing work.
///
/// <para><b>Threading.</b> Used from the UI thread, as <see cref="AgentsViewModel"/> is: it
/// follows that model's <see cref="AgentsViewModel.Changed"/>, and raises its own
/// <see cref="Changed"/> on the same thread.</para>
/// </summary>
public sealed class RoomViewModel : IDisposable
{
    /// <summary>The sessions open in one folder, and every agent they run.</summary>
    public sealed record Project(string Id, ValueList<ClaudeSession> Sessions, ValueList<RunningAgent> Agents)
    {
        /// <summary>The session that speaks for the project: the one waiting first, as the card sorts them.</summary>
        public ClaudeSession? Lead => Sessions.Count > 0 ? Sessions[0] : null;

        public bool IsWaiting => Sessions.Any(session => session.IsWaiting && !session.IsBackground);

        public int RunningCount => Agents.Count(agent => !agent.IsFinished);
    }

    private sealed record Walk(IReadOnlyList<RoomPoint> Route, DateTimeOffset StartedAt)
    {
        public double Duration => RoomGeometry.Length(Route) / RoomGeometry.Pace;

        public RoomPoint? Position(DateTimeOffset date)
        {
            var elapsed = (date - StartedAt).TotalSeconds;
            return elapsed < Duration ? RoomGeometry.Point(Route, Math.Max(0, elapsed) * RoomGeometry.Pace) : null;
        }
    }

    private sealed record Flip(WorkFamily? From, DateTimeOffset StartedAt);

    /// <summary>A Claudy on its way out: it is no longer listed, so it carries what the scene needs.</summary>
    private sealed record Leave(string Project, Rgba Tint, Walk Walk);

    /// <summary>An agent that just crossed the room stays put this long, whatever its next calls are.</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromSeconds(20);

    /// <summary>How long an area takes to change work.</summary>
    public static readonly TimeSpan FlipTime = TimeSpan.FromSeconds(0.7);

    /// <summary>How long a screen lights up when its agent's work changes.</summary>
    public static readonly TimeSpan FlashTime = TimeSpan.FromSeconds(0.4);

    /// <summary>Past this, the session's last call no longer says what it is doing.</summary>
    public static readonly TimeSpan FreshCall = TimeSpan.FromSeconds(120);

    private readonly AgentsViewModel _agents;
    private readonly IRoomStore _store;
    private readonly Func<DateTimeOffset> _clock;
    private readonly Func<bool> _reducesMotion;

    /// <summary>Every project with a session open, put away or not.</summary>
    private List<Project> _open = [];
    private HashSet<string> _hiddenIds;
    private Dictionary<string, RoomPlan> _plans = new(StringComparer.Ordinal);

    /// <summary>Who sits where on each project's sofa.</summary>
    private Dictionary<string, Dictionary<string, int>> _lounges = new(StringComparer.Ordinal);

    /// <summary>Agents that were done with no place left on the sofa: they went out and are not shown again.</summary>
    private HashSet<string> _departed = new(StringComparer.Ordinal);
    private Dictionary<string, Leave> _leaving = new(StringComparer.Ordinal);
    private Dictionary<string, Walk> _walks = new(StringComparer.Ordinal);
    private Dictionary<string, Dictionary<int, Flip>> _flips = new(StringComparer.Ordinal);
    private Dictionary<string, DateTimeOffset> _lastMove = new(StringComparer.Ordinal);
    private Dictionary<string, AgentWork?> _lastWork = new(StringComparer.Ordinal);
    private Dictionary<string, DateTimeOffset> _flashes = new(StringComparer.Ordinal);
    private Dictionary<string, Rgba> _tints = new(StringComparer.Ordinal);

    private string? _selectedId;
    private bool _isSummaryOpen;
    private string? _selectedAgentId;
    private bool _isOverview;
    private bool _isVisible;

    public RoomViewModel(AgentsViewModel agents, IRoomStore store, Func<DateTimeOffset>? clock = null, Func<bool>? reducesMotion = null)
    {
        ArgumentNullException.ThrowIfNull(agents);
        ArgumentNullException.ThrowIfNull(store);
        _agents = agents;
        _store = store;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _reducesMotion = reducesMotion ?? (() => false);
        _hiddenIds = new HashSet<string>(store.HiddenRooms, StringComparer.Ordinal);
        _agents.Changed += Sync;
    }

    /// <summary>Raised after anything the room shows changes, on the thread that changed it.</summary>
    public event Action? Changed;

    /// <summary>The rooms on show: every project with a session open, less the ones put away.</summary>
    public ValueList<Project> Projects { get; private set; } = ValueList<Project>.Empty;

    /// <summary>The rooms put away, in the order their projects are listed: the menu brings one back.</summary>
    public IReadOnlyList<string> Hidden { get; private set; } = [];

    public string? SelectedId
    {
        get => _selectedId;
        set => Set(ref _selectedId, value);
    }

    public bool IsSummaryOpen
    {
        get => _isSummaryOpen;
        set => Set(ref _isSummaryOpen, value);
    }

    /// <summary>The agent whose card is open over the room.</summary>
    public string? SelectedAgentId
    {
        get => _selectedAgentId;
        set => Set(ref _selectedAgentId, value);
    }

    /// <summary>Every project at once, each a small room, instead of one room.</summary>
    public bool IsOverview
    {
        get => _isOverview;
        set => Set(ref _isOverview, value);
    }

    /// <summary>The weather in the window: one of <see cref="RoomWeathers.Id"/>, or each day's own.</summary>
    public string WeatherChoice
    {
        get => _store.WeatherChoice;
        set
        {
            if (_store.WeatherChoice != value)
            {
                _store.WeatherChoice = value;
                Changed?.Invoke();
            }
        }
    }

    /// <summary>
    /// The room is on screen. Opening it seats everyone at once: nobody walks in for a room that
    /// was already at work.
    /// </summary>
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value)
            {
                return;
            }
            _isVisible = value;
            _walks = new(StringComparer.Ordinal);
            _flips = new(StringComparer.Ordinal);
            _leaving = new(StringComparer.Ordinal);
            if (value)
            {
                Sync();
            }
        }
    }

    public Project? Selected => Projects.FirstOrDefault(project => project.Id == SelectedId) ?? (Projects.Count > 0 ? Projects[0] : null);

    /// <summary>The agent whose card is open, while it is still listed in the room on screen.</summary>
    public RunningAgent? SelectedAgent => Selected?.Agents.FirstOrDefault(agent => agent.Id == SelectedAgentId);

    /// <summary>Shows the room of the project an agent works in, its card open.</summary>
    public void Show(string agentId)
    {
        if (_open.FirstOrDefault(project => project.Agents.Any(agent => agent.Id == agentId)) is not { } project)
        {
            return;
        }
        if (_hiddenIds.Contains(project.Id))
        {
            Restore(project.Id);
        }
        _selectedId = project.Id;
        _selectedAgentId = agentId;
        _isOverview = false;
        _isSummaryOpen = false;
        Changed?.Invoke();
    }

    /// <summary>Enters the room of one project.</summary>
    public void Enter(string projectId)
    {
        _selectedId = projectId;
        _isOverview = false;
        _selectedAgentId = null;
        Changed?.Invoke();
    }

    /// <summary>Puts a room away: it leaves the list until it is asked back, or until its sessions close.</summary>
    public void Hide(string projectId)
    {
        _hiddenIds.Add(projectId);
        Remember();
        Sync();
    }

    /// <summary>Brings back a room that was put away.</summary>
    public void Restore(string projectId)
    {
        _hiddenIds.Remove(projectId);
        Remember();
        Sync();
    }

    public void ToggleAgent(string id)
    {
        _selectedAgentId = _selectedAgentId == id ? null : id;
        if (_selectedAgentId is not null)
        {
            _isSummaryOpen = false;
        }
        Changed?.Invoke();
    }

    private bool Animates => IsVisible && !_reducesMotion();

    private void Remember() => _store.HiddenRooms = [.. _hiddenIds.Order(StringComparer.Ordinal)];

    // Seating

    /// <summary>Reads the sessions and agents again and seats everyone.</summary>
    public void Sync()
    {
        var now = _clock();
        _open = ProjectsOf(_agents.Groups);
        // A room put away is forgotten once its sessions close: the folder opened again is a room
        // again. Nothing is forgotten on an empty reading, the first one at launch.
        if (_open.Count > 0)
        {
            var stillOpen = _hiddenIds.Where(id => _open.Any(project => project.Id == id)).ToHashSet(StringComparer.Ordinal);
            if (!stillOpen.SetEquals(_hiddenIds))
            {
                _hiddenIds = stillOpen;
                Remember();
            }
        }
        var found = _open.Where(project => !_hiddenIds.Contains(project.Id)).ToList();
        var away = _open.Select(project => project.Id).Where(_hiddenIds.Contains).ToList();
        if (!away.SequenceEqual(Hidden, StringComparer.Ordinal))
        {
            Hidden = away;
        }
        var shownProjects = new ValueList<Project>(found);
        if (!shownProjects.Equals(Projects))
        {
            Projects = shownProjects;
        }
        if (SelectedId is null || !found.Any(project => project.Id == SelectedId))
        {
            _selectedId = found.FirstOrDefault()?.Id;
        }

        var listed = found.SelectMany(project => project.Agents).Select(agent => agent.Id).ToHashSet(StringComparer.Ordinal);
        SeeOut(listed, now);
        _walks = _walks.Where(pair => listed.Contains(pair.Key) && pair.Value.Position(now) is not null).ToDictionary(StringComparer.Ordinal);
        _leaving = _leaving.Where(pair => pair.Value.Walk.Position(now) is not null).ToDictionary(StringComparer.Ordinal);
        _departed = _departed.Where(listed.Contains).ToHashSet(StringComparer.Ordinal);
        _lastMove = _lastMove.Where(pair => listed.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal);
        _lastWork = _lastWork.Where(pair => listed.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal);
        _flashes = _flashes.Where(pair => listed.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal);
        _tints = _tints.Where(pair => listed.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal);
        var shown = found.Select(project => project.Id).ToHashSet(StringComparer.Ordinal);
        _plans = _plans.Where(pair => shown.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal);
        _flips = _flips.Where(pair => shown.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal);
        _lounges = _lounges.Where(pair => shown.Contains(pair.Key)).ToDictionary(StringComparer.Ordinal);
        if (_selectedAgentId is { } chosen && !listed.Contains(chosen))
        {
            _selectedAgentId = null;
        }
        if (found.Count < 2)
        {
            _isOverview = false;
        }

        foreach (var project in found)
        {
            Seat(project, now);
        }
        // Seats are not published one by one: every pass tells the views to read them again.
        Changed?.Invoke();
    }

    /// <summary>An agent that left the list while it sat on the sofa gets up and goes out by the door.</summary>
    private void SeeOut(HashSet<string> listed, DateTimeOffset now)
    {
        foreach (var (project, sofa) in _lounges)
        {
            foreach (var (id, slot) in sofa.Where(pair => !listed.Contains(pair.Key)).ToList())
            {
                sofa.Remove(id);
                if (!Animates)
                {
                    continue;
                }
                _leaving[id] = new Leave(project, _tints.GetValueOrDefault(id, Theme.Idle), new Walk(RoomGeometry.RouteFromSofa(slot), now));
            }
        }
    }

    private void Seat(Project project, DateTimeOffset now)
    {
        var isNew = !_plans.TryGetValue(project.Id, out var plan);
        plan ??= new RoomPlan();
        var before = (Seats: plan.Seats, Families: plan.Families);
        plan.Seat(project.Agents.Where(agent => !agent.IsFinished).Select(agent =>
        {
            var isSettling = _lastMove.TryGetValue(agent.Id, out var moved) && now - moved < SettleTime;
            return (agent.Id, isSettling ? null : agent.Family(now));
        }).ToList());
        _plans[project.Id] = plan;

        var moves = Animates && !isNew;
        foreach (var agent in project.Agents)
        {
            _tints[agent.Id] = _agents.Tint(agent);
            AgentWork? work = agent.LastAction?.Work;
            if (_lastWork.TryGetValue(agent.Id, out var known) && known != work)
            {
                _flashes[agent.Id] = now;
            }
            _lastWork[agent.Id] = work;

            RoomSeat? previous = before.Seats.TryGetValue(agent.Id, out var old) ? old : null;
            if (agent.IsFinished)
            {
                Rest(agent, project.Id, previous, moves, now);
                continue;
            }
            if (!plan.Seats.TryGetValue(agent.Id, out var seat) || seat == previous)
            {
                continue;
            }
            _lastMove[agent.Id] = now;
            if (moves)
            {
                _walks[agent.Id] = new Walk(RoomGeometry.Route(previous, seat), now);
            }
        }
        if (!moves)
        {
            return;
        }
        for (var zone = 0; zone < plan.Families.Count; zone++)
        {
            if (plan.Families[zone] != before.Families[zone])
            {
                if (!_flips.TryGetValue(project.Id, out var turning))
                {
                    turning = [];
                    _flips[project.Id] = turning;
                }
                turning[zone] = new Flip(before.Families[zone], now);
            }
        }
    }

    /// <summary>
    /// An agent that is done leaves its desk for the sofa, where it stays until it leaves the list.
    /// With the sofa full it goes straight out by the door.
    /// </summary>
    private void Rest(RunningAgent agent, string project, RoomSeat? desk, bool moves, DateTimeOffset now)
    {
        if (!_lounges.TryGetValue(project, out var sofa))
        {
            sofa = new Dictionary<string, int>(StringComparer.Ordinal);
            _lounges[project] = sofa;
        }
        if (sofa.ContainsKey(agent.Id) || _departed.Contains(agent.Id))
        {
            return;
        }
        var taken = sofa.Values.ToHashSet();
        int? free = Enumerable.Range(0, RoomGeometry.SofaSeats).Where(slot => !taken.Contains(slot)).Select(slot => (int?)slot).FirstOrDefault();
        if (free is { } slot)
        {
            sofa[agent.Id] = slot;
            if (moves && desk is { } from)
            {
                _walks[agent.Id] = new Walk(RoomGeometry.RouteToSofa(from, slot), now);
            }
        }
        else
        {
            _departed.Add(agent.Id);
            if (moves && desk is { } from)
            {
                _leaving[agent.Id] = new Leave(project, _agents.Tint(agent), new Walk(RoomGeometry.RouteOut(from), now));
            }
        }
    }

    /// <summary>One project per folder, in the order the card lists their sessions. Claudy's <c>projects(from:)</c>.</summary>
    public static List<Project> ProjectsOf(IReadOnlyList<SessionGroup> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        var order = new List<string>();
        var sessions = new Dictionary<string, List<ClaudeSession>>(StringComparer.Ordinal);
        var agents = new Dictionary<string, List<RunningAgent>>(StringComparer.Ordinal);
        foreach (var group in groups)
        {
            var name = group.Session.FolderName ?? group.Session.Name;
            if (!sessions.TryGetValue(name, out var list))
            {
                order.Add(name);
                list = [];
                sessions[name] = list;
                agents[name] = [];
            }
            list.Add(group.Session);
            agents[name].AddRange(group.Agents);
        }
        return [.. order.Select(name => new Project(name, new ValueList<ClaudeSession>(sessions[name]),
            new ValueList<RunningAgent>(agents[name].OrderBy(agent => agent.StartedAt).ThenBy(agent => agent.Id, StringComparer.Ordinal))))];
    }

    // The scene

    /// <summary>Where a seat is in a scene's list of desks.</summary>
    public static int DeskIndex(RoomSeat seat) => (seat.Zone * RoomPlan.SeatsPerZone) + seat.Index;

    /// <summary>
    /// Where an agent of the room on screen is, in the scene's units: the top left corner of its
    /// desk, or its place on the sofa. <c>null</c> while it walks or once it has gone out.
    /// </summary>
    public RoomPoint? Place(string agentId)
    {
        if (Selected is not { } project || (_walks.TryGetValue(agentId, out var walk) && walk.Position(_clock()) is not null))
        {
            return null;
        }
        if (_lounges.TryGetValue(project.Id, out var sofa) && sofa.TryGetValue(agentId, out var slot))
        {
            var seat = RoomGeometry.Sofa(slot);
            return new RoomPoint(seat.X - 8, seat.Y - RoomSprite.Rows);
        }
        return _plans.TryGetValue(project.Id, out var plan) && plan.Seats.TryGetValue(agentId, out var desk) ? RoomGeometry.Desk(desk) : null;
    }

    /// <summary>
    /// The room of a project as it stands at <paramref name="date"/>. <paramref name="quota"/> is the
    /// session quota, 0…1. <paramref name="withHelp"/> is for the pass that builds what a pointer is
    /// told: the drawing does without.
    /// </summary>
    public RoomScene Scene(Project? project, DateTimeOffset date, double? quota, bool withHelp = true)
    {
        var plan = project is not null && _plans.TryGetValue(project.Id, out var known) ? known : new RoomPlan();
        var sofa = project is not null && _lounges.TryGetValue(project.Id, out var seats) ? seats : [];
        var turning = project is not null && _flips.TryGetValue(project.Id, out var flips) ? flips : [];
        var zones = Enumerable.Range(0, plan.Families.Count).Select(zone =>
        {
            var progress = turning.TryGetValue(zone, out var flip) ? (date - flip.StartedAt).TotalSeconds / FlipTime.TotalSeconds : 1;
            var isTurning = progress < 1;
            return new RoomZone
            {
                Family = isTurning ? flip!.From : plan.Families[zone],
                Incoming = plan.Families[zone],
                Progress = isTurning ? Math.Max(0, progress) : 0,
                Count = plan.Count(zone),
            };
        }).ToList();
        var desks = new RoomOccupant?[RoomPlan.Homes.Count * RoomPlan.SeatsPerZone];
        var walkers = new List<RoomWalker>();
        var loungers = new List<RoomLounger>();
        int? selectedDesk = null;
        foreach (var agent in project?.Agents ?? ValueList<RunningAgent>.Empty)
        {
            var tint = _agents.Tint(agent);
            var glyph = agent.LastAction is { } last ? RoomGlyph.Symbol(last.Work) : null;
            if (_walks.TryGetValue(agent.Id, out var walk) && walk.Position(date) is { } position)
            {
                walkers.Add(new RoomWalker(agent.Id, position, tint, agent.IsFinished ? RoomGlyph.Done : glyph));
            }
            else if (sofa.TryGetValue(agent.Id, out var slot))
            {
                loungers.Add(new RoomLounger(agent.Id, slot, tint, withHelp ? Help(agent) : ""));
            }
            else if (plan.Seats.TryGetValue(agent.Id, out var seat))
            {
                var index = DeskIndex(seat);
                desks[index] = Occupant(agent, glyph, date, withHelp);
                if (agent.Id == SelectedAgentId)
                {
                    selectedDesk = index;
                }
            }
        }
        foreach (var (id, leave) in _leaving)
        {
            if (leave.Project == project?.Id && leave.Walk.Position(date) is { } position)
            {
                walkers.Add(new RoomWalker(id, position, leave.Tint));
            }
        }
        return new RoomScene
        {
            Zones = new(zones),
            Desks = new(desks),
            Walkers = new(walkers),
            Loungers = new(loungers),
            Chief = Chief(project, quota, date),
            SelectedDesk = selectedDesk,
        };
    }

    private RoomOccupant Occupant(RunningAgent agent, string? glyph, DateTimeOffset date, bool withHelp)
    {
        var state = agent.Status(date) switch
        {
            AgentStatus.Working => RoomOccupantState.Working,
            AgentStatus.Idle => RoomOccupantState.Idle,
            AgentStatus.Stalled => RoomOccupantState.Stalled,
            _ => RoomOccupantState.Done,
        };
        var isFlashing = _flashes.TryGetValue(agent.Id, out var flashed) && date - flashed < FlashTime;
        var phase = 0;
        foreach (var rune in agent.Id.EnumerateRunes())
        {
            phase = unchecked((phase * 31) + rune.Value) & 0xFF;
        }
        return new RoomOccupant
        {
            Id = agent.Id,
            Tint = _agents.Tint(agent),
            State = state,
            Glyph = glyph ?? RoomGlyph.Thinking,
            Phase = phase,
            IsFlashing = isFlashing,
            Help = withHelp ? Help(agent) : "",
        };
    }

    private string Help(RunningAgent agent)
    {
        var lines = new List<string> { agent.Title, $"{CapitalizedFirst(_agents.State(agent))} · {agent.WorkLabel} · {_agents.Elapsed(agent)}" };
        if (agent.EditedFiles.Count > 0)
        {
            lines.Add("Wrote: " + string.Join(", ", agent.EditedFiles));
        }
        return string.Join('\n', lines);
    }

    /// <summary>
    /// Claudy at the centre desk. While the session works, its first screen shows its own last call
    /// when that call is fresh; otherwise that it runs agents, or that it thinks.
    /// </summary>
    private static RoomChief Chief(Project? project, double? quota, DateTimeOffset date)
    {
        if (project?.Lead is not { } lead)
        {
            return new RoomChief { State = RoomChiefState.Away, Quota = quota };
        }
        switch (lead.Status)
        {
            case SessionState.Waiting:
                var reason = lead.WaitingReason is { } why ? $": {why.ToLowerInvariant()}" : "";
                return new RoomChief { State = RoomChiefState.Waiting, Glyph = RoomGlyph.Waiting, Quota = quota, Help = $"{lead.Name} is waiting for you{reason}" };
            case SessionState.Busy:
                var own = lead.LastAction is { } action && date - action.Date < FreshCall ? action : null;
                var isDelegating = project.RunningCount > 0 && (own is null || own.Work == AgentWork.Delegating);
                var glyph = isDelegating ? RoomGlyph.Delegating : own is not null ? RoomGlyph.Symbol(own.Work) : RoomGlyph.Thinking;
                var doing = isDelegating ? "running its agents"
                    : own is not null ? string.Join(" · ", new[] { own.Work.Label(), own.Detail ?? own.File }.OfType<string>())
                    : "thinking";
                return new RoomChief { State = RoomChiefState.Working, Glyph = glyph, Quota = quota, Help = $"{lead.Name} · {doing}" };
            default:
                return new RoomChief { State = RoomChiefState.Idle, Quota = quota, Help = $"{lead.Name} is at its prompt" };
        }
    }

    private static string CapitalizedFirst(string text)
    {
        if (text.Length == 0)
        {
            return text;
        }
        var first = Rune.GetRuneAt(text, 0);
        return Rune.ToUpperInvariant(first).ToString() + text[first.Utf16SequenceLength..];
    }

    private void Set<T>(ref T field, T value)
    {
        if (!EqualityComparer<T>.Default.Equals(field, value))
        {
            field = value;
            Changed?.Invoke();
        }
    }

    public void Dispose() => _agents.Changed -= Sync;
}
