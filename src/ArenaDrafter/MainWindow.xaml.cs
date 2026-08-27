using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Interop;
using System.Runtime.InteropServices;

namespace ArenaDrafter;

public partial class MainWindow : Window
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(nint windowHandle, int attribute, ref int value, int valueSize);

    private readonly ObservableCollection<ChampionRow> champions = [];
    private readonly ObservableCollection<BattleHeroRow> battleHeroes = [];
    private readonly ObservableCollection<string> battleEvents = [];
    private readonly ObservableCollection<ChampionRow> teamCandidates = [];
    private readonly ObservableCollection<ArenaPoolRow> arenaPool = [];
    private readonly ObservableCollection<ArenaPoolRow> arenaLeaderPriority = [];
    private readonly ObservableCollection<ArenaCatalogRow> arenaCatalog = [];
    private readonly ObservableCollection<ArenaBanPriorityRow> arenaBanPriority = [];
    private readonly ObservableCollection<PresetLineupSlotRow> presetLineupSlots = [];
    private readonly ObservableCollection<ArenaPickRuleRow> arenaPickRules = [];
    private readonly ObservableCollection<ArenaCatalogRow> pickRuleEnemyChampions = [];
    private readonly ObservableCollection<ArenaCatalogRow> pickRulePlayerChampions = [];
    private readonly ObservableCollection<ArenaBanPriorityRow> simulatorPlayerRows = [];
    private readonly ObservableCollection<ArenaBanPriorityRow> simulatorOpponentRows = [];
    private readonly ObservableCollection<LiveArenaDisplayRow> liveArenaPlayerRows = [];
    private readonly ObservableCollection<LiveArenaDisplayRow> liveArenaEnemyRows = [];
    private readonly ObservableCollection<string> simulatorEvents = [];
    private readonly ObservableCollection<BattleOpenerStepRow> battleOpenerSteps = [];
    private readonly PortraitCache portraits = new();
    private readonly SkillIconCache skillIcons = new();
    private readonly BattleDiagnosticRecorder battleDiagnostics = new();
    private readonly RewardDiagnosticRecorder rewardDiagnostics = new();
    private readonly MythicalClickTraceRecorder mythicalClickTrace = new();
    private readonly HellHadesArenaCatalog arenaRoleCatalog;
    private readonly CancellationTokenSource lifetime = new();
    private readonly ICollectionView view;
    private readonly ICollectionView arenaCandidateView;
    private readonly ICollectionView arenaBanView;
    private readonly ICollectionView presetCandidateView;
    private readonly ICollectionView pickRuleEnemyView;
    private readonly ICollectionView pickRulePlayerView;
    private readonly ICollectionView pickRuleReplacementView;
    private readonly ICollectionView simulatorOpponentView;
    private readonly ICollectionView battleOpenerChampionView;
    private ProbeClient? probe;
    private int connectedRaidProcessId;
    private bool closing;
    private bool battleActive;
    private int lastBattleTurn = -1;
    private int lastActiveHeroId;
    private LiveArenaSnapshotMessage? lastLiveArena;
    private ArenaStrategyFile arenaStrategy = new(ArenaStrategyFile.CurrentVersion, [], [], ArenaDraftMode.AdaptiveDraft, ArenaStrategyFile.EmptyPresetLineup(), []);
    private ArenaDraftMode arenaDraftMode = ArenaDraftMode.AdaptiveDraft;
    private LiveArenaAutomationMode arenaMode;
    private string? lastArenaDecisionKey;
    private LiveArenaDecision? pendingArenaDecision;
    private DateTime pendingArenaDecisionAt;
    private bool continuousArenaSession;
    private bool liveDraftActive;
    private LiveArenaSessionDecision? pendingArenaSessionDecision;
    private DateTime pendingArenaSessionDecisionAt;
    private int deferredLiveArenaReturnAttempts;
    private bool deferredLiveArenaReturnRetryPending;
    private bool rewardBatchInProgress;
    private int rewardClaimBaseline;
    private DateTime rewardClaimAt;
    private readonly DispatcherTimer sessionDashboardTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private DateTime? arenaSessionStartedAt;
    private DateTime? arenaSessionEndedAt;
    private int arenaSessionBattleLimit = 5;
    private int arenaSessionBattlesCompleted;
    private int arenaSessionWins;
    private int arenaSessionLosses;
    private int arenaSessionUnknownResults;
    private int arenaSessionRewardsClaimed;
    private int arenaSessionRefills;
    private int arenaSessionGemsSpent;
    private bool arenaSessionBattleCounted;
    private bool arenaSessionLimitReached;
    private bool arenaDashboardRunFinalized = true;
    private LiveArenaDashboardFile arenaDashboard = LiveArenaDashboardFile.Empty;
    private ArenaDashboardRange arenaDashboardRange = ArenaDashboardRange.Session;
    private BattleOpenerFile battleOpener = new(BattleOpenerFile.CurrentVersion, []);
    private readonly Dictionary<int, int> battleOpenerProgress = [];
    private BattleOpenerDecision? pendingBattleOpenerDecision;
    private BattleSnapshotMessage? pendingBattleOpenerSnapshot;
    private DateTime pendingBattleOpenerDecisionAt;
    private BattleSnapshotMessage? lastBattleSnapshot;
    private long nextBattleActionId;
    private long pendingBattleActionId;
    private bool battleAutoRecoveryRequired;
    private int battleAutoRetryCount;
    private int battleOpenerRecoveryTurn = -1;
    private int battleOpenerRecoveryHeroId = -1;
    private int battleModeTransitionTurn = -1;
    private int battleModeTransitionHeroId = -1;
    private int battleModeTransitionCount;
    private bool battleOpenerInitialized;
    private bool battleInitialAutoVerified;
    private bool battleSkillStabilizationPending;
    private bool diagnosticClickPathArmed;
    private ArenaStrategyFile? undoArenaStrategy;
    private bool arenaPrioritiesDrawerOpen;
    private bool leaderPriorityReviewed;
    private ArenaBoardStep arenaBoardStep = ArenaBoardStep.Lineup;
    private ChampionPickerIntent? championPickerIntent;
    private PresetLineupSlotRow? championPickerSlot;
    private PresetLineupCandidateRow? championPickerCandidate;
    private FrameworkElement? championPickerReturnFocus;
    private bool pickRuleEditorReparented;
    private ArenaDraftSimulator? draftSimulator;
    private bool draftSimulationResolved;
    private string? hudDecisionKey;
    private LiveArenaDecision? hudDecision;
    private Point dragStart;
    private ArenaPoolRow? draggedArenaPool;
    private ArenaPoolRow? draggedArenaLeader;
    private ArenaBanPriorityRow? draggedArenaBan;
    private PresetLineupCandidateRow? draggedPresetCandidate;
    private ArenaPickRuleRow? draggedPickRule;
    private Guid? editingPickRuleId;
    private DragPreviewAdorner? dragPreview;
    private AdornerLayer? dragLayer;
    private UIElement? dragSurface;
    private FrameworkElement? draggedContainer;
    private bool dragInProgress;
    private readonly Queue<DraftReveal> draftReveals = [];
    private bool draftRevealPlaying;
    private int? activeDraftRevealBaseId;

    private enum ChampionPickerIntent { Primary, ReplacePrimary, Substitute, ReplaceSubstitute }
    private enum ArenaBoardStep { Lineup, BanPlan, PickRules, Leader, Review }
    private enum ArenaDashboardRange { LastRun, Session, AllTime }

    public MainWindow()
    {
        Log.Info("Application started.");
        arenaRoleCatalog = HellHadesArenaCatalog.LoadEmbedded();
        InitializeComponent();
        ((TextBlock)((StackPanel)ArenaBoardBanTab.Header).Children[0]).Text = "Ban";
        ((TextBlock)((StackPanel)ArenaBoardPickRulesTab.Header).Children[0]).Text = "Rules";
        ((TextBlock)((StackPanel)ArenaBoardLeaderTab.Header).Children[0]).Text = "Leader";
        RarityFilter.ItemsSource = new[] { "Rarity: All", "Rarity: Common", "Rarity: Uncommon", "Rarity: Rare", "Rarity: Epic", "Rarity: Legendary", "Rarity: Mythical" };
        AffinityFilter.ItemsSource = new[] { "Affinity: All", "Affinity: Magic", "Affinity: Force", "Affinity: Spirit", "Affinity: Void" };
        LocationFilter.ItemsSource = new[] { "Location: All", "Location: Inventory", "Location: Storage", "Location: Reserve" };
        RarityFilter.SelectedIndex = AffinityFilter.SelectedIndex = LocationFilter.SelectedIndex = 0;
        ChampionGrid.ItemsSource = champions;
        BattleGrid.ItemsSource = battleHeroes;
        BattleEventList.ItemsSource = battleEvents;
        ArenaPoolGrid.ItemsSource = arenaPool;
        ArenaLeaderPriorityList.ItemsSource = arenaLeaderPriority;
        ArenaBanPriorityList.ItemsSource = arenaBanPriority;
        for (var index = 0; index < 5; index++) presetLineupSlots.Add(new(index));
        PresetLineupList.ItemsSource = presetLineupSlots;
        PresetSlotBox.ItemsSource = presetLineupSlots;
        PresetSlotBox.SelectedIndex = 0;
        PickRulesList.ItemsSource = arenaPickRules;
        PickRuleEnemyList.ItemsSource = pickRuleEnemyChampions;
        PickRulePlayerList.ItemsSource = pickRulePlayerChampions;
        PickRuleEnemyMatchBox.ItemsSource = Enum.GetValues<ArenaChampionMatch>();
        PickRulePlayerMatchBox.ItemsSource = Enum.GetValues<ArenaChampionMatch>();
        PickRuleDraftBox.ItemsSource = Enum.GetValues<ArenaPickRuleDraft>();
        PickRuleFirstTurnBox.ItemsSource = Enum.GetValues<ArenaPickRuleFirstTurn>();
        PickRuleRoleCountBox.ItemsSource = Enumerable.Range(1, 5);
        PickRuleMinimumEnemyBox.ItemsSource = Enumerable.Range(0, 6);
        PickRuleTargetSlotBox.ItemsSource = presetLineupSlots;
        ArenaRolePresetBox.ItemsSource = ArenaRolePresets.Names;
        ArenaRolePresetBox.SelectedIndex = 0;
        ArenaBoardPresetList.ItemsSource = presetLineupSlots;
ArenaDraftRosterGrid.ItemsSource = arenaPool;
        ArenaDraftRosterRolePresetBox.ItemsSource = ArenaRolePresets.Names;
        ArenaDraftRosterRolePresetBox.SelectedIndex = 0;
        ArenaBoardBanList.ItemsSource = arenaBanPriority;
        ArenaBoardPickRulesList.ItemsSource = arenaPickRules;
        ArenaBoardLeaderList.ItemsSource = arenaLeaderPriority;
        ArenaBoardLivePlayerList.ItemsSource = liveArenaPlayerRows;
        ArenaBoardLiveEnemyList.ItemsSource = liveArenaEnemyRows;
        SimulatorFirstBox.ItemsSource = new[] { "Player first", "Opponent first" };
        SimulatorFirstBox.SelectedIndex = 0;
        SimulatorRuleBox.ItemsSource = new[] { "Shared picks (Bronze / Silver)", "Exclusive picks (Gold+)" };
        SimulatorRuleBox.SelectedIndex = 0;
        SimulatorPlayerList.ItemsSource = simulatorPlayerRows;
        SimulatorOpponentList.ItemsSource = simulatorOpponentRows;
        SimulatorEventList.ItemsSource = simulatorEvents;
        SimulatorPlayerBanBox.ItemsSource = simulatorPlayerRows;
        ArenaOpenerSequenceList.ItemsSource = battleOpenerSteps;
        view = CollectionViewSource.GetDefaultView(champions);
        view.Filter = MatchesFilter;
        ChampionSorting.Apply(view);
        arenaCandidateView = new ListCollectionView(teamCandidates);
        arenaCandidateView.Filter = item => item is ChampionRow row && (string.IsNullOrWhiteSpace(ArenaChampionSearchBox.Text)
            || row.Name.Contains(ArenaChampionSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        ChampionSorting.Apply(arenaCandidateView);
        ArenaCandidateBox.ItemsSource = arenaCandidateView;
        ArenaDraftRosterCandidateBox.ItemsSource = arenaCandidateView;
        presetCandidateView = new ListCollectionView(teamCandidates);
        presetCandidateView.Filter = item => item is ChampionRow row && (string.IsNullOrWhiteSpace(PresetChampionSearchBox.Text)
            || row.Name.Contains(PresetChampionSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        ChampionSorting.Apply(presetCandidateView);
        PresetCandidateBox.ItemsSource = presetCandidateView;
        ArenaBoardChampionPickerList.ItemsSource = presetCandidateView;
        pickRuleEnemyView = new ListCollectionView(arenaCatalog);
        pickRuleEnemyView.Filter = item => item is ArenaCatalogRow row && (string.IsNullOrWhiteSpace(PickRuleEnemySearchBox.Text)
            || row.Name.Contains(PickRuleEnemySearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        pickRuleEnemyView.SortDescriptions.Add(new(nameof(ArenaCatalogRow.Name), ListSortDirection.Ascending));
        PickRuleEnemyCandidateBox.ItemsSource = pickRuleEnemyView;
        pickRulePlayerView = new ListCollectionView(arenaCatalog);
        pickRulePlayerView.Filter = item => item is ArenaCatalogRow row && (string.IsNullOrWhiteSpace(PickRulePlayerSearchBox.Text)
            || row.Name.Contains(PickRulePlayerSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        pickRulePlayerView.SortDescriptions.Add(new(nameof(ArenaCatalogRow.Name), ListSortDirection.Ascending));
        PickRulePlayerCandidateBox.ItemsSource = pickRulePlayerView;
        pickRuleReplacementView = new ListCollectionView(teamCandidates);
        pickRuleReplacementView.Filter = item => item is ChampionRow row && (string.IsNullOrWhiteSpace(PickRuleReplacementSearchBox.Text)
            || row.Name.Contains(PickRuleReplacementSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        ChampionSorting.Apply(pickRuleReplacementView);
        PickRuleReplacementBox.ItemsSource = pickRuleReplacementView;
        arenaBanView = new ListCollectionView(arenaCatalog);
        arenaBanView.Filter = item => item is ArenaCatalogRow row && (string.IsNullOrWhiteSpace(ArenaBanSearchBox.Text)
            || row.Name.Contains(ArenaBanSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase)
            || row.BaseId.ToString().Contains(ArenaBanSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        arenaBanView.SortDescriptions.Add(new(nameof(ArenaCatalogRow.Name), ListSortDirection.Ascending));
        ArenaBanCandidateBox.ItemsSource = arenaBanView;
        ArenaBoardBanCandidateBox.ItemsSource = arenaBanView;
        simulatorOpponentView = new ListCollectionView(arenaCatalog);
        simulatorOpponentView.Filter = item => item is ArenaCatalogRow row && (string.IsNullOrWhiteSpace(SimulatorOpponentSearchBox.Text)
            || row.Name.Contains(SimulatorOpponentSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        simulatorOpponentView.SortDescriptions.Add(new(nameof(ArenaCatalogRow.Name), ListSortDirection.Ascending));
        SimulatorOpponentCandidateBox.ItemsSource = simulatorOpponentView;
        battleOpenerChampionView = new ListCollectionView(arenaCatalog);
        battleOpenerChampionView.Filter = item => item is ArenaCatalogRow row && (row.Rarity is 5 or 6 || arenaPool.Any(champion => champion.BaseId == row.BaseId)
                || presetLineupSlots.SelectMany(slot => slot.Candidates).Any(champion => champion.BaseId == row.BaseId)
                || arenaPickRules.Any(rule => rule.ToRule().Replacement.BaseId == row.BaseId))
            && (string.IsNullOrWhiteSpace(ArenaOpenerSearchBox.Text) || row.Name.Contains(ArenaOpenerSearchBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
        battleOpenerChampionView.SortDescriptions.Add(new(nameof(ArenaCatalogRow.Name), ListSortDirection.Ascending));
        ArenaOpenerChampionBox.ItemsSource = battleOpenerChampionView;
        try
        {
            arenaStrategy = ArenaStrategyFile.Load();
            arenaDraftMode = arenaStrategy.DraftMode;
            foreach (var baseId in arenaStrategy.BanPriority)
                arenaBanPriority.Add(new(baseId, "Unavailable champion"));
            RebuildPickRules();
        }
        catch (Exception exception)
        {
            Log.Error("Live Arena strategy could not be loaded.", exception);
            ArenaStrategyStatusText.Text = $"Strategy not loaded: {exception.Message}";
        }
        try { battleOpener = BattleOpenerFile.Load(); }
        catch (Exception exception)
        {
            Log.Error("Live Arena opener could not be loaded.", exception);
            ArenaOpenerStatusText.Text = $"Opener not loaded: {exception.Message}";
        }
        try { arenaDashboard = LiveArenaDashboardFile.Load(); }
        catch (Exception exception)
        {
            Log.Error("Live Arena dashboard could not be loaded.", exception);
            arenaDashboard = LiveArenaDashboardFile.Empty;
        }
        StatusText.Text = GameLauncher.FindRaid() is not null ? "Waiting for connection" : GameLauncher.IsPlariumPlayRunning() ? "Waiting for RAID" : "Waiting for Plarium Play";
        DeveloperCatalogStatusText.Text = "Waiting for RAID catalog";
        sessionDashboardTimer.Tick += (_, _) =>
        {
            UpdateArenaSessionDashboard();
        };
        sessionDashboardTimer.Start();
        UpdateDraftModeUi();
        UpdateArenaModeUi();
        UpdateDiagnosticIndicator();
    }

    private void NavigateTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string value } &&
            int.TryParse(value, out var index) && index >= 0 && index < ArenaTabs.Items.Count)
            ArenaTabs.SelectedIndex = index;
    }

    private void LaunchRaid_Click(object sender, RoutedEventArgs e)
    {
        try { GameLauncher.Launch(); StatusText.Text = "Waiting for RAID"; }
        catch (Exception exception) { ShowError(exception.Message); }
    }

    private async void Connect_Click(object sender, RoutedEventArgs e)
    {
        ConnectButton.IsEnabled = false;
        try
        {
            await ResetProbeAsync();
            var process = GameLauncher.FindRaid() ?? throw new InvalidOperationException("RAID is not running from the official installation path.");
            Log.Info($"Connect requested for RAID PID {process.Id}.");
            StatusText.Text = "Connecting";
            await Task.Run(() => BuildValidator.Validate(process), lifetime.Token);
            var client = new ProbeClient(process.Id);
            probe = client;
            client.SnapshotReceived += snapshot => Dispatcher.Invoke(() => OnSnapshot(snapshot));
            client.CatalogReceived += catalog => Dispatcher.Invoke(() => OnCatalog(catalog));
            client.BattleReceived += snapshot => Dispatcher.Invoke(() => OnBattle(snapshot));
            client.LiveArenaReceived += snapshot => Dispatcher.Invoke(() => OnLiveArena(snapshot));
            client.AutomationReceived += message => Dispatcher.Invoke(() => OnAutomation(message));
            client.RewardTraceReceived += payload => Dispatcher.Invoke(() => OnRewardTrace(payload));
            client.MythicalClickTraceReceived += payload => Dispatcher.Invoke(() => OnMythicalClickTrace(payload));
            client.ErrorReceived += message => Dispatcher.Invoke(() => OnProbeError(message));
            var connecting = client.ConnectAsync(lifetime.Token);
            await Task.Run(() => NativeInjector.Inject(process, AppPaths.ProbeDll), lifetime.Token);
            await connecting.WaitAsync(TimeSpan.FromSeconds(30), lifetime.Token);
            if (!ReferenceEquals(probe, client)) throw new InvalidOperationException("The native probe stopped during connection.");
            await client.WatchAsync();
            connectedRaidProcessId = process.Id;
            var diagnosticPath = battleDiagnostics.Start(process.Id);
            BattleDiagnosticStatusText.Text = $"Session trace: {System.IO.Path.GetFileName(diagnosticPath)}";
            RefreshButton.IsEnabled = true;
            ConnectButton.Visibility = Visibility.Collapsed;
            StatusText.Text = "Connected";
            UpdateArenaModeUi();
            Log.Info("Host connection completed.");
        }
        catch (InvalidDataException exception) { Log.Error("Unsupported RAID build.", exception); await ResetProbeAsync(); ShowError(exception.Message, "Unsupported Build"); ConnectButton.IsEnabled = true; }
        catch (Exception exception) { Log.Error("Connection failed.", exception); await ResetProbeAsync(); ShowError(exception.Message); ConnectButton.IsEnabled = true; }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (probe is not null) await probe.WatchAsync();
    }

    private void OnSnapshot(SnapshotMessage snapshot)
    {
        Log.Info($"Applying snapshot revision {snapshot.Revision} with {snapshot.Champions.Length} champions.");
        champions.Clear();
        teamCandidates.Clear();
        foreach (var item in snapshot.Champions)
        {
            var row = new ChampionRow
            {
                Instance = new ChampionInstance(item.Id, item.TypeId, item.BaseId, item.Grade, item.Ascension, item.Level, item.Empowerment, item.Marker, item.Locked, item.InStorage, item.InBathhouse, item.Awakening),
                Definition = new ChampionDefinition(item.Name, item.Rarity, item.Affinity, item.Faction, null)
            };
            champions.Add(row);
            if (row.Location == "Inventory") teamCandidates.Add(row);
            _ = LoadPortraitAsync(row);
        }
        HydrateArenaPool();
        ApplyFilter();
    }

    private void OnCatalog(CatalogMessage catalog)
    {
        var compatibility = arenaRoleCatalog.ValidateAgainstRaid(catalog.Champions);
        if (compatibility.RarityMismatchBaseIds.Length > 0)
            Log.Info($"HellHades rarity differs from RAID for Base IDs {string.Join(", ", compatibility.RarityMismatchBaseIds)}; RAID rarity remains authoritative.");
        arenaCatalog.Clear();
        foreach (var champion in catalog.Champions.OrderBy(champion => champion.Name, StringComparer.OrdinalIgnoreCase)) arenaCatalog.Add(new(champion));
        foreach (var priority in arenaBanPriority)
        {
            var champion = arenaCatalog.FirstOrDefault(item => item.BaseId == priority.BaseId);
            if (champion is null) continue;
            priority.Name = champion.Name;
            _ = LoadCatalogPortraitAsync(champion);
        }
        arenaBanView.Refresh();
        simulatorOpponentView.Refresh();
        battleOpenerChampionView.Refresh();
        pickRuleEnemyView.Refresh();
        pickRulePlayerView.Refresh();
        RebuildPickRules();
        if (ArenaOpenerChampionBox.SelectedItem is null && !battleOpenerChampionView.IsEmpty) ArenaOpenerChampionBox.SelectedIndex = 0;
        LoadVisibleBanPortraits();
        ArenaStrategyStatusText.Text = $"RAID catalog loaded: {arenaCatalog.Count} identities; {compatibility.Matched} have compiled Arena roles, {compatibility.Missing} await a data update, and {compatibility.RarityMismatchBaseIds.Length} use RAID's authoritative rarity.";
        DeveloperCatalogStatusText.Text = $"{arenaCatalog.Count} identities loaded; {compatibility.Matched} have compiled Arena roles.";
        UpdateArenaModeUi();
    }

    private async Task LoadPortraitAsync(ChampionRow row)
    {
        var image = await LoadPortraitImageAsync(row.Instance.BaseId);
        if (image is not null) await Dispatcher.InvokeAsync(() => ApplyPortrait(row.Instance.BaseId, image));
    }

    private async Task LoadCatalogPortraitAsync(ArenaCatalogRow row)
    {
        if (row.Portrait is not null)
        {
            await Dispatcher.InvokeAsync(() => ApplyPortrait(row.BaseId, row.Portrait));
            return;
        }
        var image = await LoadPortraitImageAsync(row.BaseId);
        if (image is not null) await Dispatcher.InvokeAsync(() => ApplyPortrait(row.BaseId, image));
    }

    private async Task<ImageSource?> LoadPortraitImageAsync(int baseId)
    {
        try
        {
            var path = await portraits.GetAsync(baseId, lifetime.Token);
            if (path is null) return null;
            return await Dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.UriSource = new Uri(path);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return (ImageSource)bitmap;
                }
                catch (Exception exception)
                {
                    Log.Error($"Official RAID portrait decode failed. ChampionBaseId={baseId}; requestedPath='{path}'.", exception);
                    return null;
                }
            });
        }
        catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { return null; }
        catch (Exception exception)
        {
            Log.Error($"Official RAID portrait request failed. ChampionBaseId={baseId}.", exception);
            return null;
        }
    }

    private void ApplyPortrait(int baseId, ImageSource image)
    {
        foreach (var champion in champions.Where(row => row.Instance.BaseId == baseId)) champion.Portrait = image;
        foreach (var catalog in arenaCatalog.Where(row => row.BaseId == baseId)) catalog.Portrait = image;
        foreach (var priority in arenaBanPriority.Where(row => row.BaseId == baseId)) priority.Portrait = image;
        foreach (var rule in arenaPickRules.Where(row => row.ToRule().Replacement.BaseId == baseId)) rule.UpdatePortrait(image);
        foreach (var liveRow in liveArenaPlayerRows.Concat(liveArenaEnemyRows).Where(row => row.BaseId == baseId)) liveRow.Portrait = image;
        if (activeDraftRevealBaseId == baseId) DraftRevealPortrait.Source = image;
    }

    private void RefreshLiveArenaRows(LiveArenaSnapshotMessage snapshot)
    {
        liveArenaPlayerRows.Clear();
        liveArenaEnemyRows.Clear();
        foreach (var hero in snapshot.Draft.PlayerHeroes.OrderBy(hero => hero.Slot))
        {
            var catalog = arenaCatalog.FirstOrDefault(row => row.BaseId == hero.BaseId);
            var portrait = catalog?.Portrait
                ?? arenaPool.FirstOrDefault(row => row.BaseId == hero.BaseId)?.Portrait
                ?? presetLineupSlots.SelectMany(slot => slot.Candidates).FirstOrDefault(row => row.BaseId == hero.BaseId)?.Portrait;
            liveArenaPlayerRows.Add(new(hero.Slot, hero.BaseId, hero.TypeId, hero.Name, portrait));
            if (catalog is not null && catalog.Portrait is null) _ = LoadCatalogPortraitAsync(catalog);
        }
        foreach (var hero in snapshot.Draft.EnemyHeroes.OrderBy(hero => hero.Slot))
        {
            var catalog = arenaCatalog.FirstOrDefault(row => row.BaseId == hero.BaseId);
            liveArenaEnemyRows.Add(new(hero.Slot, hero.BaseId, hero.TypeId, hero.Name, catalog?.Portrait));
            if (catalog is not null && catalog.Portrait is null) _ = LoadCatalogPortraitAsync(catalog);
        }
    }

    private void OnLiveArena(LiveArenaSnapshotMessage snapshot)
    {
        var previous = lastLiveArena;
        RefreshLiveArenaRows(snapshot);
        var draftActive = snapshot.Draft.Phase is not null;
        if (draftActive != liveDraftActive)
        {
            lastArenaDecisionKey = null;
            hudDecisionKey = null;
            hudDecision = null;
            liveDraftActive = draftActive;
            if (draftActive) AddBattleEvent("Live Arena automation remains armed for the new draft.");
        }
        UpdateArenaHud(snapshot);
        ArenaRulesText.Text = snapshot.Draft.AllowDuplicatePicks switch
        {
            true => $"{LiveArenaLeagueName(snapshot.Draft.LeagueId)} â€¢ SHARED PICKS ALLOWED",
            false => $"{LiveArenaLeagueName(snapshot.Draft.LeagueId)} â€¢ PICKS ARE EXCLUSIVE",
            _ => $"{LiveArenaLeagueName(snapshot.Draft.LeagueId)} â€¢ RULE NOT VISIBLE"
        };
        if (snapshot.Draft.AllowDuplicatePicks is bool duplicateRule && duplicateRule != previous?.Draft.AllowDuplicatePicks)
            AddBattleEvent($"Live Arena rule: {(duplicateRule ? "shared champion picks are allowed" : "each champion can be picked only once")} ({LiveArenaLeagueName(snapshot.Draft.LeagueId)}).");
        if (snapshot.Matchmaking && previous?.Matchmaking != true) AddBattleEvent("Live Arena matchmaking started.");
        if (!snapshot.Matchmaking && previous?.Matchmaking == true)
            AddBattleEvent(snapshot.Draft.Phase is null ? "Live Arena matchmaking stopped." : "Live Arena opponent found.");

        if (snapshot.Draft.Phase != previous?.Draft.Phase && snapshot.Draft.Phase is not null)
            AddBattleEvent($"Live Arena phase: {LiveArenaPhase(snapshot.Draft.Phase)} â€¢ {snapshot.Draft.Turn ?? "waiting"} turn.");
        else if (snapshot.Draft.Phase is not null && snapshot.Draft.Turn != previous?.Draft.Turn)
            AddBattleEvent($"Live Arena draft turn: {snapshot.Draft.Turn ?? "waiting"}.");

        var previousPlayerHeroes = previous?.Draft.PlayerHeroes ?? [];
        var previousEnemyHeroes = previous?.Draft.EnemyHeroes ?? [];
        foreach (var hero in snapshot.Draft.PlayerHeroes.Where(hero => !previousPlayerHeroes.Any(old => old.Slot == hero.Slot && old.TypeId == hero.TypeId)))
        {
            AddBattleEvent($"Player picked slot {hero.Slot + 1}: {hero.Name} ({hero.TypeId}).");
            EnqueueDraftReveal(hero, DraftRevealKind.Pick, true);
        }
        foreach (var hero in snapshot.Draft.EnemyHeroes.Where(hero => !previousEnemyHeroes.Any(old => old.Slot == hero.Slot && old.TypeId == hero.TypeId)))
        {
            AddBattleEvent($"Opponent picked slot {hero.Slot + 1}: {hero.Name} ({hero.TypeId}).");
            EnqueueDraftReveal(hero, DraftRevealKind.Pick, false);
        }

        if (snapshot.Draft.BestEnemyBlockedSlot is int suggested && suggested != previous?.Draft.BestEnemyBlockedSlot)
            AddBattleEvent($"RAID suggested opponent ban: {HeroName(snapshot.Draft.EnemyHeroes, suggested)}.");
        if (snapshot.Draft.EnemyBlockedSlot is int enemyBlocked && enemyBlocked != previous?.Draft.EnemyBlockedSlot)
        {
            AddBattleEvent($"Player banned opponent slot {enemyBlocked + 1}: {HeroName(snapshot.Draft.EnemyHeroes, enemyBlocked)}.");
            EnqueueDraftReveal(snapshot.Draft.EnemyHeroes.FirstOrDefault(hero => hero.Slot == enemyBlocked), DraftRevealKind.Ban, false);
        }
        if (snapshot.Draft.PlayerBlockedSlot is int playerBlocked && playerBlocked != previous?.Draft.PlayerBlockedSlot)
        {
            AddBattleEvent($"Opponent banned player slot {playerBlocked + 1}: {HeroName(snapshot.Draft.PlayerHeroes, playerBlocked)}.");
            EnqueueDraftReveal(snapshot.Draft.PlayerHeroes.FirstOrDefault(hero => hero.Slot == playerBlocked), DraftRevealKind.Ban, true);
        }
        if (snapshot.Draft.PlayerLeaderSlot is int playerLeader && playerLeader != previous?.Draft.PlayerLeaderSlot)
        {
            AddBattleEvent($"Player leader: slot {playerLeader + 1}, {HeroName(snapshot.Draft.PlayerHeroes, playerLeader)}.");
            EnqueueDraftReveal(snapshot.Draft.PlayerHeroes.FirstOrDefault(hero => hero.Slot == playerLeader), DraftRevealKind.Leader, true);
        }
        if (snapshot.Draft.EnemyLeaderSlot is int enemyLeader && enemyLeader != previous?.Draft.EnemyLeaderSlot)
        {
            AddBattleEvent($"Opponent leader: slot {enemyLeader + 1}, {HeroName(snapshot.Draft.EnemyHeroes, enemyLeader)}.");
            EnqueueDraftReveal(snapshot.Draft.EnemyHeroes.FirstOrDefault(hero => hero.Slot == enemyLeader), DraftRevealKind.Leader, false);
        }
        if (snapshot.Draft.BattleSetupReady && previous?.Draft.BattleSetupReady != true) AddBattleEvent("Live Arena battle setup is ready.");

        if (snapshot.Transport.Active && previous?.Transport.Active != true) AddBattleEvent("Live Arena battle transport started.");
        if (snapshot.Transport.Phase != previous?.Transport.Phase && snapshot.Transport.Phase is not null)
            AddBattleEvent($"Live Arena transport phase: {snapshot.Transport.Phase}.");
        if (!snapshot.Transport.Active && previous?.Transport.Active == true) AddBattleEvent("Live Arena battle transport ended.");

        if (snapshot.Draft.Phase is not null)
            BattleSummaryText.Text = $"Live Arena â€¢ {LiveArenaPhase(snapshot.Draft.Phase)} â€¢ {snapshot.Draft.Turn ?? "waiting"} turn â€¢ Picks {snapshot.Draft.PlayerHeroes.Length}â€“{snapshot.Draft.EnemyHeroes.Length}";
        else if (snapshot.Matchmaking) BattleSummaryText.Text = "Live Arena â€¢ Matchmaking";
        else if (snapshot.Transport.Active) BattleSummaryText.Text = $"Live Arena â€¢ {snapshot.Transport.Phase ?? "battle"} â€¢ {snapshot.Transport.Turn ?? "waiting"} turn";
        else if (!battleActive) BattleSummaryText.Text = "No active battle";
        UpdateRewardBatchState(snapshot);
        lastLiveArena = snapshot;
        VerifyPendingArenaAction(snapshot);
        VerifyPendingArenaSessionAction(snapshot);
        _ = HandleLiveArenaDecisionAsync(snapshot);
        _ = HandleContinuousArenaSessionAsync(snapshot);
    }

    private void UpdateRewardBatchState(LiveArenaSnapshotMessage snapshot)
    {
        if (snapshot.Ui.RewardBatchReady && !rewardBatchInProgress)
        {
            rewardBatchInProgress = true;
            AddBattleEvent("Complete Live Arena daily reward batch detected.");
        }
        if (rewardClaimBaseline > 0 && snapshot.Ui.RewardClaimableCount < rewardClaimBaseline)
        {
            if (arenaSessionStartedAt is not null)
                arenaSessionRewardsClaimed += rewardClaimBaseline - snapshot.Ui.RewardClaimableCount;
            rewardClaimBaseline = 0;
            AddBattleEvent("Verified Live Arena reward collection state.");
            UpdateArenaSessionDashboard();
        }
        if (rewardBatchInProgress && rewardClaimBaseline == 0 && snapshot.Ui.RewardClaimableCount == 0 && !snapshot.Ui.RewardOverlayVisible)
        {
            rewardBatchInProgress = false;
            AddBattleEvent("Complete Live Arena daily reward batch collected.");
        }
    }

    private void RecordArenaSessionBattle(BattleSnapshotMessage snapshot)
    {
        if (!continuousArenaSession || snapshot.Kind != 6 || !snapshot.Finished || arenaSessionBattleCounted) return;
        arenaSessionBattleCounted = true;
        arenaSessionBattlesCompleted++;
        var outcome = LiveArenaSessionPlanner.Outcome(snapshot);
        if (outcome == LiveArenaBattleOutcome.Win) arenaSessionWins++;
        else if (outcome == LiveArenaBattleOutcome.Loss) arenaSessionLosses++;
        else arenaSessionUnknownResults++;
        arenaDashboard = arenaDashboard.RecordBattle(outcome);
        SaveArenaDashboard();
        arenaSessionLimitReached = LiveArenaSessionPlanner.LimitReached(arenaSessionBattlesCompleted, arenaSessionBattleLimit);
        var result = outcome == LiveArenaBattleOutcome.Unknown ? "unknown result" : outcome.ToString().ToLowerInvariant();
        AddBattleEvent($"Session battle {arenaSessionBattlesCompleted}/{arenaSessionBattleLimit} completed: {result}.");
        Log.Info($"Live Arena session battle {arenaSessionBattlesCompleted}/{arenaSessionBattleLimit} completed with {result}.");
        if (arenaSessionLimitReached)
            AddBattleEvent("Battle limit reached. The session will finish result and available reward cleanup without starting another match.");
        UpdateArenaSessionDashboard();
    }

    private void UpdateArenaSessionDashboard()
    {
        if (!IsInitialized) return;
        ArenaSessionStateText.Text = continuousArenaSession ? "RUNNING" : arenaSessionLimitReached ? "COMPLETE" : arenaMode == LiveArenaAutomationMode.DryRun ? "DRY RUN" : arenaMode == LiveArenaAutomationMode.Armed ? "ARMED" : arenaSessionStartedAt is null ? "IDLE" : "STOPPED";
        ArenaSessionBattlesText.Text = $"{arenaSessionBattlesCompleted} / {arenaSessionBattleLimit}";
        ArenaSessionWinsText.Text = arenaSessionWins.ToString(CultureInfo.InvariantCulture);
        ArenaSessionLossesText.Text = arenaSessionLosses.ToString(CultureInfo.InvariantCulture);
        ArenaSessionUnknownText.Text = arenaSessionUnknownResults.ToString(CultureInfo.InvariantCulture);
        ArenaSessionRewardsText.Text = arenaSessionRewardsClaimed.ToString(CultureInfo.InvariantCulture);
        var duration = arenaSessionStartedAt is DateTime started ? (arenaSessionEndedAt ?? DateTime.UtcNow) - started : TimeSpan.Zero;
        ArenaSessionDurationText.Text = duration.TotalHours >= 1 ? duration.ToString(@"hh\:mm\:ss") : duration.ToString(@"mm\:ss");

        var selected = arenaDashboardRange switch
        {
            ArenaDashboardRange.LastRun => arenaDashboard.LastRun,
            ArenaDashboardRange.AllTime => arenaDashboard.AllTime,
            _ => CurrentArenaDashboardStats()
        };
        ArenaDashboardLastRunButton.IsChecked = arenaDashboardRange == ArenaDashboardRange.LastRun;
        ArenaDashboardSessionButton.IsChecked = arenaDashboardRange == ArenaDashboardRange.Session;
        ArenaDashboardAllTimeButton.IsChecked = arenaDashboardRange == ArenaDashboardRange.AllTime;
        ArenaDashboardRangeText.Text = arenaDashboardRange switch
        {
            ArenaDashboardRange.LastRun => "LAST RUN",
            ArenaDashboardRange.AllTime => "ALL TIME",
            _ => continuousArenaSession ? "CURRENT SESSION" : "SESSION"
        };
        ArenaDashboardBattlesText.Text = selected.Battles.ToString(CultureInfo.InvariantCulture);
        ArenaDashboardWinsText.Text = selected.Wins.ToString(CultureInfo.InvariantCulture);
        ArenaDashboardLossesText.Text = selected.Losses.ToString(CultureInfo.InvariantCulture);
        ArenaDashboardUnknownText.Text = selected.Unknown.ToString(CultureInfo.InvariantCulture);
        ArenaDashboardRefillsText.Text = selected.Refills.ToString(CultureInfo.InvariantCulture);
        ArenaDashboardGemsText.Text = selected.GemsSpent.ToString(CultureInfo.InvariantCulture);
    }

    private LiveArenaDashboardStats CurrentArenaDashboardStats() => new(
        arenaSessionBattlesCompleted, arenaSessionWins, arenaSessionLosses, arenaSessionUnknownResults,
        arenaSessionRefills, arenaSessionGemsSpent);

    private void SaveArenaDashboard()
    {
        try { arenaDashboard.Save(); }
        catch (Exception exception) { Log.Error("Live Arena dashboard could not be saved.", exception); }
    }

    private void FinalizeArenaDashboardRun()
    {
        if (arenaDashboardRunFinalized || arenaSessionStartedAt is null) return;
        arenaDashboardRunFinalized = true;
        arenaDashboard = arenaDashboard.FinishRun(CurrentArenaDashboardStats());
        SaveArenaDashboard();
    }

    private void ArenaDashboardRange_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }
            || !Enum.TryParse(tag, out ArenaDashboardRange range)) return;
        arenaDashboardRange = range;
        UpdateArenaSessionDashboard();
    }

    private void CompleteArenaSessionAtLimit()
    {
        continuousArenaSession = false;
        arenaMode = LiveArenaAutomationMode.Off;
        arenaSessionEndedAt = DateTime.UtcNow;
        pendingArenaDecision = null;
        pendingArenaSessionDecision = null;
        ArenaStrategyStatusText.Text = $"Session complete: {arenaSessionBattlesCompleted} of {arenaSessionBattleLimit} requested battles finished.";
        ArenaHudActionText.Text = "Battle limit reached â€” session complete";
        AddBattleEvent(ArenaStrategyStatusText.Text);
        Log.Info($"Live Arena session completed at its battle limit: {arenaSessionBattlesCompleted}/{arenaSessionBattleLimit}, {arenaSessionWins} wins, {arenaSessionLosses} losses, {arenaSessionUnknownResults} unknown, {arenaSessionRewardsClaimed} rewards claimed.");
        UpdateArenaModeUi();
    }

    private void ToggleArenaInspector_Click(object sender, RoutedEventArgs e)
    {
        var opening = ArenaInspectorPanel.Visibility != Visibility.Visible;
        ArenaInspectorPanel.Visibility = opening ? Visibility.Visible : Visibility.Collapsed;
        ArenaInspectorButton.Content = opening ? "Close inspector" : "Inspector";
        if (opening)
        {
            DeveloperToolsLayer.Visibility = Visibility.Visible;
            DeveloperToolsLayer.IsHitTestVisible = true;
        }
    }

    private void OpenDeveloperTools_Click(object sender, RoutedEventArgs e)
    {
        DeveloperToolsLayer.Visibility = Visibility.Visible;
        DeveloperToolsLayer.IsHitTestVisible = true;
        UpdateDiagnosticIndicator();
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            var darkMode = 1;
            DwmSetWindowAttribute(new WindowInteropHelper(this).Handle, DwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        }
        catch (Exception exception)
        {
            Log.Info($"System dark title bar is unavailable: {exception.Message}");
        }
        ApplyLiveArenaLayout();
    }

    private void CloseDeveloperTools_Click(object sender, RoutedEventArgs e)
    {
        DeveloperToolsLayer.Visibility = Visibility.Collapsed;
        DeveloperToolsLayer.IsHitTestVisible = false;
    }

    private void EnqueueDraftReveal(LiveArenaHeroWire? hero, DraftRevealKind kind, bool playerTeam)
    {
        if (hero is null) return;
        draftReveals.Enqueue(new(hero, kind, playerTeam));
        if (!draftRevealPlaying) _ = PlayDraftRevealsAsync();
    }

    private async Task PlayDraftRevealsAsync()
    {
        draftRevealPlaying = true;
        try
        {
            while (draftReveals.TryDequeue(out var reveal))
            {
                var catalogChampion = arenaCatalog.FirstOrDefault(champion => champion.BaseId == reveal.Hero.BaseId);
                if (catalogChampion is not null)
                {
                    try { await LoadCatalogPortraitAsync(catalogChampion); }
                    catch (OperationCanceledException) { return; }
                    catch (Exception exception) { Log.Error($"Could not load the draft reveal portrait for {reveal.Hero.Name}.", exception); }
                }

                ConfigureDraftReveal(reveal, catalogChampion?.Portrait
                    ?? arenaPool.FirstOrDefault(champion => champion.BaseId == reveal.Hero.BaseId)?.Portrait);
                DraftRevealLayer.Visibility = Visibility.Visible;

                if (SystemParameters.ClientAreaAnimation)
                {
                    var entryDuration = TimeSpan.FromMilliseconds(430);
                    var entryEase = new CubicEase { EasingMode = EasingMode.EaseOut };
                    DraftRevealCard.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, entryDuration) { EasingFunction = entryEase });
                    DraftRevealScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.08, 1, entryDuration) { EasingFunction = entryEase });
                    DraftRevealScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, entryDuration) { EasingFunction = entryEase });
                    DraftRevealRotation.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(reveal.PlayerTeam ? -16 : 16, 0, entryDuration) { EasingFunction = entryEase });
                    DraftRevealTranslation.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(34, 0, entryDuration) { EasingFunction = entryEase });
                    await Task.Delay(TimeSpan.FromMilliseconds(1180), lifetime.Token);
                    DraftRevealCard.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(230))
                    {
                        EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn }
                    });
                    await Task.Delay(TimeSpan.FromMilliseconds(250), lifetime.Token);
                }
                else
                {
                    DraftRevealCard.Opacity = 1;
                    await Task.Delay(TimeSpan.FromMilliseconds(900), lifetime.Token);
                }

                DraftRevealLayer.Visibility = Visibility.Collapsed;
                activeDraftRevealBaseId = null;
                DraftRevealCard.BeginAnimation(OpacityProperty, null);
                DraftRevealScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                DraftRevealScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                DraftRevealRotation.BeginAnimation(RotateTransform.AngleProperty, null);
                DraftRevealTranslation.BeginAnimation(TranslateTransform.YProperty, null);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            DraftRevealLayer.Visibility = Visibility.Collapsed;
            activeDraftRevealBaseId = null;
            draftRevealPlaying = false;
        }
    }

    private void ConfigureDraftReveal(DraftReveal reveal, ImageSource? portrait)
    {
        var accent = reveal.Kind switch
        {
            DraftRevealKind.Ban => Color.FromRgb(181, 107, 112),
            DraftRevealKind.Leader => Color.FromRgb(200, 155, 74),
            _ when reveal.PlayerTeam => Color.FromRgb(200, 155, 74),
            _ => Color.FromRgb(79, 179, 168)
        };
        var accentBrush = new SolidColorBrush(accent);
        accentBrush.Freeze();

        DraftRevealCard.BorderBrush = accentBrush;
        DraftRevealPhaseText.Foreground = accentBrush;
        DraftRevealPhaseText.Text = reveal.Kind switch
        {
            DraftRevealKind.Pick when reveal.PlayerTeam => "YOUR PICK",
            DraftRevealKind.Pick => "OPPONENT PICK",
            DraftRevealKind.Ban when reveal.PlayerTeam => "YOUR CHAMPION BANNED",
            DraftRevealKind.Ban => "OPPONENT BANNED",
            DraftRevealKind.Leader when reveal.PlayerTeam => "YOUR LEADER",
            _ => "OPPONENT LEADER"
        };
        DraftRevealNameText.Text = reveal.Hero.Name;
        DraftRevealPortrait.PlaceholderText = reveal.Hero.Name;
        activeDraftRevealBaseId = reveal.Hero.BaseId;
        DraftRevealTeamText.Text = $"{(reveal.PlayerTeam ? "PLAYER" : "OPPONENT")} TEAM  â€¢  SLOT {reveal.Hero.Slot + 1}";
        DraftRevealPortrait.Source = portrait;
        DraftRevealPortrait.Opacity = reveal.Kind == DraftRevealKind.Ban ? 0.42 : 1;
        DraftRevealBanVeil.Visibility = reveal.Kind == DraftRevealKind.Ban ? Visibility.Visible : Visibility.Collapsed;
        DraftRevealBanSlash.Visibility = reveal.Kind == DraftRevealKind.Ban ? Visibility.Visible : Visibility.Collapsed;
        DraftRevealCrown.Visibility = reveal.Kind == DraftRevealKind.Leader ? Visibility.Visible : Visibility.Collapsed;
        DraftRevealCard.Opacity = 1;
        DraftRevealScale.ScaleX = 1;
        DraftRevealScale.ScaleY = 1;
        DraftRevealRotation.Angle = 0;
        DraftRevealTranslation.Y = 0;
    }

    private void UpdateArenaHud(LiveArenaSnapshotMessage snapshot)
    {
        ArenaHudPhaseText.Text = snapshot.Ui.RewardOverlayVisible ? "Reward"
            : snapshot.Ui.RefillVisible ? "Token refill"
            : snapshot.Ui.FinishVisible ? "Results"
            : snapshot.Draft.Phase is not null ? $"{LiveArenaPhase(snapshot.Draft.Phase)} â€¢ {snapshot.Draft.Turn ?? "waiting"}"
            : snapshot.Matchmaking ? "Matchmaking"
            : snapshot.Ui.MenuVisible ? "Live Arena menu"
            : "Waiting";
        ArenaHudTimerText.Text = snapshot.Draft.SecondsRemaining is int seconds ? $"{seconds:00}s" : "No timer";
        UpdateArenaStrip(snapshot);
        if (snapshot.Draft.Phase is null)
        {
            if (pendingArenaSessionDecision is not null) ArenaHudActionText.Text = pendingArenaSessionDecision.Explanation;
            else if (snapshot.Ui.RefillVisible && ArenaAutoRefillCheckBox.IsChecked != true)
                ArenaHudActionText.Text = "Token refill waiting â€” auto-refill is off";
            else if (continuousArenaSession)
                ArenaHudActionText.Text = LiveArenaSessionPlanner.Decide(snapshot, ArenaAutoRefillCheckBox.IsChecked == true, rewardBatchInProgress, rewardClaimBaseline > 0)?.Explanation
                    ?? (snapshot.Transport.Active ? "Battle in progress" : "Waiting for the next verified screen");
            else ArenaHudActionText.Text = "Continuous session is off";
            return;
        }
        if (!snapshot.Ui.DraftVisible)
        {
            ArenaHudActionText.Text = "Waiting for RAID's draft screen";
            return;
        }
        if (snapshot.Draft.Turn != "player")
        {
            ArenaHudActionText.Text = snapshot.Draft.Phase is null ? "No active draft" : "Waiting for opponent";
            return;
        }
        try
        {
            var decision = PreviewArenaDecision(snapshot);
            ArenaHudActionText.Text = decision?.Explanation ?? "No action for this phase";
        }
        catch (Exception exception) { ArenaHudActionText.Text = $"Strategy not ready: {exception.Message}"; }
    }

    private void UpdateArenaStrip(LiveArenaSnapshotMessage snapshot)
    {
        var phase = snapshot.Draft.Phase;
        var queueState = snapshot.Matchmaking ? "ACTIVE" : phase is not null || snapshot.Transport.Active ? "DONE" : "WAITING";
        var draftState = phase switch
        {
            "initialize" or "heroPick" => "ACTIVE",
            "heroBan" or "leaderSelection" or "startBattle" => "DONE",
            _ => "WAITING"
        };
        var banState = phase switch
        {
            "heroBan" => "ACTIVE",
            "leaderSelection" or "startBattle" => "DONE",
            _ => "WAITING"
        };
        var leaderState = phase switch
        {
            "leaderSelection" => "ACTIVE",
            "startBattle" when snapshot.Transport.Active => "DONE",
            _ => "WAITING"
        };
        var battleState = snapshot.Transport.Active ? "ACTIVE" : phase == "startBattle" ? "READY" : "WAITING";

        SetArenaStripState(ArenaStripQueueState, queueState);
        SetArenaStripState(ArenaStripDraftState, draftState);
        SetArenaStripState(ArenaStripBanState, banState);
        SetArenaStripState(ArenaStripLeaderState, leaderState);
        SetArenaStripState(ArenaStripBattleState, battleState);
    }

    private void SetArenaStripState(TextBlock stateText, string state)
    {
        stateText.Text = state == "WAITING" ? "â€”" : state;
        stateText.Foreground = state switch
        {
            "ACTIVE" => (Brush)FindResource("CyanBrush"),
            "DONE" or "READY" => (Brush)FindResource("GoldBrush"),
            _ => (Brush)FindResource("MutedBrush")
        };
    }

    private LiveArenaDecision? PreviewArenaDecision(LiveArenaSnapshotMessage snapshot)
    {
        var key = $"{snapshot.Draft.Revision}:{snapshot.Draft.Phase}:{snapshot.Draft.Turn}:{snapshot.Draft.PlayerHeroes.Length}:{snapshot.Draft.EnemyHeroes.Length}";
        if (key == hudDecisionKey) return hudDecision;
        var decision = LiveArenaDecisionEngine.Decide(snapshot, CaptureArenaStrategy(true), arenaRoleCatalog.RolesByBaseId);
        hudDecisionKey = key;
        hudDecision = decision;
        return hudDecision;
    }

    private async Task HandleLiveArenaDecisionAsync(LiveArenaSnapshotMessage snapshot)
    {
        if (arenaMode == LiveArenaAutomationMode.Off || !snapshot.Ui.DraftVisible || snapshot.Draft.Turn != "player" || pendingArenaDecision is not null) return;
        try
        {
            var decision = PreviewArenaDecision(snapshot);
            if (decision is null || decision.Key == lastArenaDecisionKey) return;
            lastArenaDecisionKey = decision.Key;
            ArenaStrategyStatusText.Text = decision.Explanation;
            ArenaHudActionText.Text = decision.Explanation;
            AddBattleEvent($"Strategy: {decision.Explanation}");
            if (decision.RuleEvaluations is { Length: > 0 })
                foreach (var evaluation in decision.RuleEvaluations) AddBattleEvent($"Pick rule: {evaluation.Explanation}");
            Log.Info($"Live Arena {arenaMode} decision: {decision.Action} [{string.Join(',', decision.Values)}]. {decision.Explanation}");
            if (arenaMode == LiveArenaAutomationMode.DryRun) return;
            if (probe is null) throw new InvalidOperationException("The RAID probe is not connected.");
            pendingArenaDecision = decision;
            pendingArenaDecisionAt = DateTime.UtcNow;
            switch (decision.Action)
            {
                case "pick": await probe.PickLiveArenaAsync(decision.Values); break;
                case "ban": await probe.BanLiveArenaAsync(decision.Values.Single()); break;
                case "leader": await probe.SelectLiveArenaLeaderAsync(decision.Values.Single()); break;
                default: throw new InvalidDataException("The strategy produced an unsupported Live Arena action.");
            }
            _ = EnforceArenaVerificationDeadlineAsync(decision);
        }
        catch (Exception exception)
        {
            Log.Error("Live Arena strategy stopped.", exception);
            arenaMode = LiveArenaAutomationMode.Off;
            continuousArenaSession = false;
            pendingArenaDecision = null;
            pendingArenaSessionDecision = null;
            ArenaStrategyStatusText.Text = $"Stopped fail-closed: {exception.Message}";
            AddBattleEvent($"Live Arena strategy stopped: {exception.Message}");
            UpdateArenaModeUi();
        }
    }

    private async Task EnforceArenaVerificationDeadlineAsync(LiveArenaDecision decision)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(8), lifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (!ReferenceEquals(pendingArenaDecision, decision)) return;
        pendingArenaDecision = null;
        arenaMode = LiveArenaAutomationMode.Off;
        continuousArenaSession = false;
        ArenaStrategyStatusText.Text = $"Stopped fail-closed: RAID did not expose the submitted {decision.Action} within 8 seconds.";
        AddBattleEvent(ArenaStrategyStatusText.Text);
        UpdateArenaModeUi();
    }

    private void VerifyPendingArenaAction(LiveArenaSnapshotMessage snapshot)
    {
        if (pendingArenaDecision is null) return;
        var applied = pendingArenaDecision.Action switch
        {
            "pick" => pendingArenaDecision.Values.All(id => snapshot.Draft.PlayerHeroes.Any(hero => hero.Id == id)),
            "ban" => snapshot.Draft.EnemyBlockedSlot == pendingArenaDecision.Values.Single(),
            "leader" => snapshot.Draft.PlayerLeaderSlot == pendingArenaDecision.Values.Single(),
            _ => false
        };
        if (applied)
        {
            AddBattleEvent($"Verified Live Arena {pendingArenaDecision.Action} application.");
            pendingArenaDecision = null;
            return;
        }
        if (DateTime.UtcNow - pendingArenaDecisionAt <= TimeSpan.FromSeconds(8)) return;
        var action = pendingArenaDecision.Action;
        pendingArenaDecision = null;
        arenaMode = LiveArenaAutomationMode.Off;
        continuousArenaSession = false;
        ArenaStrategyStatusText.Text = $"Stopped fail-closed: RAID did not expose the submitted {action} within 8 seconds.";
        AddBattleEvent(ArenaStrategyStatusText.Text);
        UpdateArenaModeUi();
    }

    private async Task HandleContinuousArenaSessionAsync(LiveArenaSnapshotMessage snapshot)
    {
        if (!continuousArenaSession || arenaMode != LiveArenaAutomationMode.Armed
            || pendingArenaSessionDecision is not null || pendingArenaDecision is not null
            || deferredLiveArenaReturnRetryPending) return;
        try
        {
            var decision = LiveArenaSessionPlanner.Decide(snapshot, !arenaSessionLimitReached && ArenaAutoRefillCheckBox.IsChecked == true,
                rewardBatchInProgress, rewardClaimBaseline > 0);
            if (arenaSessionLimitReached && decision?.Action == "queue") decision = null;
            if (decision is null)
            {
                if (arenaSessionLimitReached && snapshot.Ui.MenuVisible && !snapshot.Ui.FinishVisible && !snapshot.Ui.RewardOverlayVisible
                    && !rewardBatchInProgress && rewardClaimBaseline == 0)
                {
                    CompleteArenaSessionAtLimit();
                    return;
                }
                if (snapshot.Ui.RefillVisible && ArenaAutoRefillCheckBox.IsChecked != true)
                    ArenaStrategyStatusText.Text = "Live Arena tokens are empty. Enable auto-refill to confirm this refill, or stop the session.";
                return;
            }
            if (probe is null) throw new InvalidOperationException("The RAID probe is not connected.");
            pendingArenaSessionDecision = decision;
            pendingArenaSessionDecisionAt = DateTime.UtcNow;
            if (decision.Action is "reward-claim" or "reward-refill")
            {
                if (decision.Action == "reward-claim") rewardBatchInProgress = true;
                rewardClaimBaseline = decision.BeforeValue;
                rewardClaimAt = DateTime.UtcNow;
            }
            ArenaStrategyStatusText.Text = decision.Explanation;
            ArenaHudActionText.Text = decision.Explanation;
            AddBattleEvent($"Session: {decision.Explanation}");
            Log.Info($"Live Arena continuous-session action: {decision.Action}. {decision.Explanation}");
            switch (decision.Action)
            {
                case "queue": await probe.QueueLiveArenaAsync(); break;
                case "refill": await probe.RefillLiveArenaAsync(decision.BeforeValue); break;
                case "return": await probe.ReturnToLiveArenaAsync(); break;
                case "reward-claim": await probe.ClaimLiveArenaRewardAsync(decision.BeforeValue); break;
                case "reward-refill": await probe.ClaimLiveArenaRewardAsync(decision.BeforeValue); break;
                case "reward-close": await probe.CloseLiveArenaRewardOverlayAsync(); break;
                default: throw new InvalidDataException("The session planner produced an unsupported Live Arena action.");
            }
            _ = EnforceArenaSessionVerificationDeadlineAsync(decision);
            if (decision.Action is "reward-claim" or "reward-refill") _ = EnforceRewardClaimStateDeadlineAsync(decision.BeforeValue, rewardClaimAt);
        }
        catch (Exception exception)
        {
            Log.Error("Live Arena continuous session stopped.", exception);
            arenaMode = LiveArenaAutomationMode.Off;
            continuousArenaSession = false;
            pendingArenaDecision = null;
            pendingArenaSessionDecision = null;
            rewardBatchInProgress = false;
            rewardClaimBaseline = 0;
            ArenaStrategyStatusText.Text = $"Stopped fail-closed: {exception.Message}";
            AddBattleEvent(ArenaStrategyStatusText.Text);
            UpdateArenaModeUi();
        }
    }

    private async Task EnforceArenaSessionVerificationDeadlineAsync(LiveArenaSessionDecision decision)
    {
        var timeout = decision.Action.StartsWith("reward-", StringComparison.Ordinal) ? 15 : 8;
        try { await Task.Delay(TimeSpan.FromSeconds(timeout), lifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (!ReferenceEquals(pendingArenaSessionDecision, decision)) return;
        pendingArenaSessionDecision = null;
        arenaMode = LiveArenaAutomationMode.Off;
        continuousArenaSession = false;
        rewardBatchInProgress = false;
        rewardClaimBaseline = 0;
        ArenaStrategyStatusText.Text = $"Stopped fail-closed: RAID did not expose the submitted {decision.Action} within {timeout} seconds.";
        AddBattleEvent(ArenaStrategyStatusText.Text);
        UpdateArenaModeUi();
    }

    private async Task RetryDeferredLiveArenaReturnAsync()
    {
        try { await Task.Delay(LiveArenaSessionPlanner.DeferredReturnRetryDelay, lifetime.Token); }
        catch (OperationCanceledException)
        {
            deferredLiveArenaReturnRetryPending = false;
            return;
        }
        deferredLiveArenaReturnRetryPending = false;
        if (!continuousArenaSession || arenaMode != LiveArenaAutomationMode.Armed
            || pendingArenaSessionDecision is not null || lastLiveArena is null) return;
        await HandleContinuousArenaSessionAsync(lastLiveArena);
    }

    private void VerifyPendingArenaSessionAction(LiveArenaSnapshotMessage snapshot)
    {
        if (pendingArenaSessionDecision is null) return;
        var applied = pendingArenaSessionDecision.Action switch
        {
            "queue" => snapshot.Matchmaking || snapshot.Draft.Phase is not null || snapshot.Ui.RefillVisible,
            "refill" => !snapshot.Ui.RefillVisible,
            "return" => !snapshot.Ui.FinishVisible,
            "reward-claim" => snapshot.Ui.RewardClaimableCount < pendingArenaSessionDecision.BeforeValue || snapshot.Ui.RewardOverlayVisible,
            "reward-refill" => snapshot.Ui.RewardClaimableCount < pendingArenaSessionDecision.BeforeValue || snapshot.Ui.RewardOverlayVisible,
            "reward-close" => !snapshot.Ui.RewardOverlayVisible,
            _ => false
        };
        if (applied)
        {
            if (pendingArenaSessionDecision.Action == "refill")
            {
                arenaSessionRefills++;
                arenaSessionGemsSpent = checked(arenaSessionGemsSpent + pendingArenaSessionDecision.BeforeValue);
                arenaDashboard = arenaDashboard.RecordRefill(pendingArenaSessionDecision.BeforeValue);
                SaveArenaDashboard();
                AddBattleEvent(pendingArenaSessionDecision.BeforeValue == 0
                    ? "Verified free Live Arena refill consumption."
                    : $"Verified Live Arena refill purchase for {pendingArenaSessionDecision.BeforeValue} Gems.");
            }
            AddBattleEvent($"Verified Live Arena session {pendingArenaSessionDecision.Action} action.");
            pendingArenaSessionDecision = null;
            UpdateArenaSessionDashboard();
            return;
        }
        var timeout = pendingArenaSessionDecision.Action.StartsWith("reward-", StringComparison.Ordinal) ? 15 : 8;
        if (DateTime.UtcNow - pendingArenaSessionDecisionAt <= TimeSpan.FromSeconds(timeout)) return;
        var action = pendingArenaSessionDecision.Action;
        pendingArenaSessionDecision = null;
        arenaMode = LiveArenaAutomationMode.Off;
        continuousArenaSession = false;
        rewardBatchInProgress = false;
        rewardClaimBaseline = 0;
        ArenaStrategyStatusText.Text = $"Stopped fail-closed: RAID did not expose the submitted {action} within {timeout} seconds.";
        AddBattleEvent(ArenaStrategyStatusText.Text);
        UpdateArenaModeUi();
    }

    private static string LiveArenaPhase(string phase) => phase switch
    {
        "initialize" => "Initialize",
        "heroPick" => "Champion pick",
        "heroBan" => "Champion ban",
        "leaderSelection" => "Leader selection",
        "startBattle" => "Start battle",
        "opponentCanceled" => "Opponent canceled",
        _ => phase
    };

    private static string HeroName(IEnumerable<LiveArenaHeroWire> heroes, int slot) =>
        heroes.FirstOrDefault(hero => hero.Slot == slot)?.Name ?? $"slot {slot + 1}";

    private static string LiveArenaLeagueName(int? leagueId) => leagueId switch
    {
        1 => "BRONZE I",
        2 => "BRONZE II",
        3 => "BRONZE III",
        4 => "BRONZE IV",
        11 => "SILVER I",
        12 => "SILVER II",
        13 => "SILVER III",
        14 => "SILVER IV",
        21 => "GOLD I",
        22 => "GOLD II",
        23 => "GOLD III",
        24 => "GOLD IV",
        30 => "PLATINUM",
        _ => "LIVE ARENA"
    };

    private void OnBattle(BattleSnapshotMessage snapshot)
    {
        lastBattleSnapshot = snapshot;
        battleDiagnostics.RecordSnapshot(snapshot, pendingBattleActionId == 0 ? null : pendingBattleActionId);
        if (!snapshot.Active)
        {
            if (battleDiagnostics.ManualReferenceActive && battleDiagnostics.ManualReferenceSawBattle)
                _ = FinishManualBattleDiagnosticsAsync("battle-ended");
            if (battleActive) AddBattleEvent("Battle ended.");
            battleActive = false;
            lastBattleTurn = -1;
            lastActiveHeroId = 0;
            battleOpenerInitialized = false;
            battleInitialAutoVerified = false;
            battleSkillStabilizationPending = false;
            battleOpenerProgress.Clear();
            pendingBattleOpenerDecision = null;
            pendingBattleOpenerSnapshot = null;
            pendingBattleActionId = 0;
            battleAutoRecoveryRequired = false;
            battleAutoRetryCount = 0;
            ResetBattleOpenerGuards();
            arenaSessionBattleCounted = false;
            battleHeroes.Clear();
            BattleSummaryText.Text = "No active battle";
            return;
        }

        if (!battleActive && !snapshot.Finished)
        {
            arenaSessionBattleCounted = false;
            ResetBattleOpenerGuards();
        }
        RecordArenaSessionBattle(snapshot);
        if (snapshot.Finished)
        {
            if (pendingBattleOpenerDecision is not null)
                battleDiagnostics.RecordMarker("battle-finished", "The battle finished before the pending action could be verified.", pendingBattleActionId);
            ClearPendingBattleAction();
            battleAutoRecoveryRequired = false;
            battleAutoRetryCount = 0;
            ResetBattleOpenerGuards();
        }
        else VerifyPendingBattleOpenerAction(snapshot);

        var activeName = snapshot.Heroes.FirstOrDefault(hero => hero.Id == snapshot.ActiveHeroId)?.Name ?? "None";
        BattleSummaryText.Text = $"{Names.BattleKind(snapshot.Kind)} â€¢ Stage {snapshot.StageId} â€¢ Round {snapshot.Round} â€¢ Turn {snapshot.Turn} â€¢ {(snapshot.AutoMode ? "Auto" : "Manual")} â€¢ Active: {activeName}";
        battleHeroes.Clear();
        foreach (var hero in snapshot.Heroes.OrderBy(hero => hero.Team).ThenBy(hero => hero.Slot)) battleHeroes.Add(new BattleHeroRow(hero, snapshot.ActiveHeroId));

        if (!battleActive)
        {
            AddBattleEvent($"{Names.BattleKind(snapshot.Kind)} battle started at stage {snapshot.StageId}.");
            Log.Info($"Battle started: kind {snapshot.Kind}, stage {snapshot.StageId}.");
        }
        if (snapshot.Turn != lastBattleTurn || snapshot.ActiveHeroId != lastActiveHeroId)
        {
            AddBattleEvent($"Round {snapshot.Round}, turn {snapshot.Turn}: {activeName} is active.");
            Log.Info($"Battle round {snapshot.Round}, turn {snapshot.Turn}, active hero {snapshot.ActiveHeroId}.");
        }
        battleActive = true;
        lastBattleTurn = snapshot.Turn;
        lastActiveHeroId = snapshot.ActiveHeroId;
        _ = HandleBattleOpenerAsync(snapshot);
    }

    private async Task HandleBattleOpenerAsync(BattleSnapshotMessage snapshot)
    {
        if (!continuousArenaSession || arenaMode != LiveArenaAutomationMode.Armed || snapshot.Kind != 6 || snapshot.Finished
            || !snapshot.HudVisible || pendingBattleOpenerDecision is not null || battleSkillStabilizationPending) return;
        try
        {
            if (battleAutoRecoveryRequired)
            {
                await ContinueBattleAutoRecoveryAsync(snapshot);
                return;
            }
            if (IsBattleOpenerRecoveryBlocked(snapshot)) return;
            if (!battleOpenerInitialized)
            {
                battleOpenerProgress.Clear();
                foreach (var champion in battleOpener.Champions) battleOpenerProgress[champion.BaseId] = 0;
                battleOpenerInitialized = true;
                battleInitialAutoVerified = snapshot.AutoMode;
                AddBattleEvent(battleOpener.Champions.Count == 0
                    ? "No opening sequence is configured; Auto mode will start immediately."
                    : "Configured Live Arena opening sequence started.");
            }
            var decision = BattleOpenerPlanner.Decide(
                snapshot,
                battleOpener,
                battleOpenerProgress,
                lastLiveArena?.Draft.PlayerLeaderSlot,
                battleInitialAutoVerified,
                lastLiveArena?.Draft.EnemyLeaderSlot,
                arenaBanPriority.Select(row => row.BaseId).ToArray());
            if (decision is null) return;
            if (probe is null) throw new InvalidOperationException("The RAID probe is not connected.");
            if (decision.Action == "skill")
            {
                battleSkillStabilizationPending = true;
                try { await Task.Delay(BattleOpenerPlanner.HudStabilizationDelay, lifetime.Token); }
                catch (OperationCanceledException) { return; }
                finally { battleSkillStabilizationPending = false; }
                var current = lastBattleSnapshot;
                if (current is null || !continuousArenaSession || arenaMode != LiveArenaAutomationMode.Armed) return;
                if (current.Turn != snapshot.Turn || current.ActiveHeroId != snapshot.ActiveHeroId)
                {
                    _ = HandleBattleOpenerAsync(current);
                    return;
                }
                decision = BattleOpenerPlanner.Decide(
                    current,
                    battleOpener,
                    battleOpenerProgress,
                    lastLiveArena?.Draft.PlayerLeaderSlot,
                    battleInitialAutoVerified,
                    lastLiveArena?.Draft.EnemyLeaderSlot,
                    arenaBanPriority.Select(row => row.BaseId).ToArray());
                if (decision is null || decision.Action != "skill") return;
                snapshot = current;
            }
            await SubmitBattleOpenerActionAsync(decision, snapshot);
        }
        catch (Exception exception)
        {
            if (pendingBattleOpenerDecision is not null)
                await RecoverBattleOpenerAutoAsync(pendingBattleOpenerDecision, $"RAID rejected the opener action: {exception.Message}", exception);
            else
                StopArenaFailClosed($"Live Arena opener stopped: {exception.Message}", exception);
        }
    }

    private async Task SubmitBattleOpenerActionAsync(BattleOpenerDecision decision, BattleSnapshotMessage snapshot)
    {
        if (probe is null) throw new InvalidOperationException("The RAID probe is not connected.");
        if (pendingBattleOpenerDecision is not null) throw new InvalidOperationException("A battle opener action is already pending.");
        if (!TryRegisterBattleModeTransition(decision, snapshot)) return;
        pendingBattleOpenerDecision = decision;
        pendingBattleOpenerSnapshot = snapshot;
        pendingBattleOpenerDecisionAt = DateTime.UtcNow;
        pendingBattleActionId = ++nextBattleActionId;
        var diagnosticClickPath = decision.Action == "skill" && diagnosticClickPathArmed;
        if (diagnosticClickPath)
        {
            diagnosticClickPathArmed = false;
            BattleDiagnosticClickButton.IsEnabled = true;
            BattleDiagnosticStatusText.Text = "Diagnostic click path submitted. Compare the HUD transition in the native probe log.";
        }
        ArenaStrategyStatusText.Text = decision.Explanation;
        ArenaHudActionText.Text = decision.Explanation;
        AddBattleEvent($"Opener #{pendingBattleActionId}: {decision.Explanation}");
        var method = decision.Action switch
        {
            "auto" or "manual" => "BattleHUDContext.OnChangeModeHit",
            "skill" when diagnosticClickPath && decision.RequiresExplicitTarget => "SkillContext.SetSelectedAndActivate + SkillContext.OnClick + BattleViewMode.TrySelectTarget (diagnostic)",
            "skill" when diagnosticClickPath => "SkillContext.SetSelectedAndActivate + SkillContext.OnClick + ClientCommandGenerator.SelectTargetManually (diagnostic)",
            "skill" when decision.RequiresExplicitTarget => "BattleHUDContext.TrySelectSkill + BattleViewMode.TrySelectTarget",
            "skill" => "SkillContext visible click lifecycle + ClientCommandGenerator.SelectTargetManually",
            _ => throw new InvalidDataException("The battle opener produced an unsupported action.")
        };
        var expected = decision.Action switch
        {
            "auto" => "autoMode=true",
            "manual" => "autoMode=false",
            "skill" => "cooldown increase, form skill replacement, or zero-cooldown turn completion",
            _ => "unsupported"
        };
        Log.Info($"Battle action #{pendingBattleActionId} requested: {decision.Action}, skill {decision.SkillTypeId}, slot {decision.SkillSlot}, target {decision.TargetId}, revision {snapshot.Revision}, turn {snapshot.Turn}, active hero {snapshot.ActiveHeroId}. {decision.Explanation}");
        battleDiagnostics.RecordAction(pendingBattleActionId, "requested", decision, snapshot, method, expected);
        if (decision.Action == "auto") await probe.EnableBattleAutoAsync();
        else if (decision.Action == "manual") await probe.EnableBattleManualAsync();
        else if (diagnosticClickPath) await probe.DiagnosticClickBattleSkillAsync(decision.SkillTypeId, decision.SkillSlot, decision.TargetId);
        else await probe.UseBattleSkillAsync(decision.SkillTypeId, decision.SkillSlot, decision.TargetId);
        battleDiagnostics.RecordAction(pendingBattleActionId, "pipe-submitted", decision, snapshot, method, expected);
        _ = EnforceBattleOpenerVerificationDeadlineAsync(decision);
    }

    private void VerifyPendingBattleOpenerAction(BattleSnapshotMessage snapshot)
    {
        if (pendingBattleOpenerDecision is null || pendingBattleOpenerSnapshot is null) return;
        var decision = pendingBattleOpenerDecision;
        var before = pendingBattleOpenerSnapshot;
        var verificationTimeout = BattleOpenerPlanner.VerificationTimeout(decision.Action);
        var turnChanged = snapshot.Turn != before.Turn || snapshot.ActiveHeroId != before.ActiveHeroId;
        if (decision.Action == "manual" && turnChanged && snapshot.AutoMode)
        {
            AddBattleEvent("The configured turn passed before Manual mode could be applied; the opener will retry on the champion's next turn.");
            battleDiagnostics.RecordMarker("action-not-applied", "The configured turn passed before Manual mode was applied.", pendingBattleActionId);
            ClearPendingBattleAction();
            return;
        }
        var applied = BattleOpenerPlanner.IsActionApplied(decision, before, snapshot);
        if (!applied)
        {
            var terminalFailure = BattleOpenerPlanner.TerminalFailureReason(decision, before, snapshot);
            if (terminalFailure is not null)
            {
                _ = RecoverBattleOpenerAutoAsync(decision, terminalFailure, snapshot: snapshot);
                return;
            }
            if (DateTime.UtcNow - pendingBattleOpenerDecisionAt <= verificationTimeout) return;
            _ = RecoverBattleOpenerAutoAsync(decision,
                $"RAID did not apply the submitted {decision.Action} within {verificationTimeout.TotalSeconds:0} seconds.", snapshot: snapshot);
            return;
        }
        if (decision.ConsumesConfiguredStep) battleOpenerProgress[decision.BaseId] = battleOpenerProgress.GetValueOrDefault(decision.BaseId) + 1;
        if (decision.Action == "auto")
        {
            battleInitialAutoVerified = true;
            battleAutoRecoveryRequired = false;
            battleAutoRetryCount = 0;
        }
        AddBattleEvent($"Verified battle action #{pendingBattleActionId}: {decision.Action}.");
        battleDiagnostics.RecordAction(pendingBattleActionId, "verified", decision, snapshot, "state predicate", "applied");
        ClearPendingBattleAction();
    }

    private async Task EnforceBattleOpenerVerificationDeadlineAsync(BattleOpenerDecision decision)
    {
        var verificationTimeout = BattleOpenerPlanner.VerificationTimeout(decision.Action);
        try { await Task.Delay(verificationTimeout, lifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (!ReferenceEquals(pendingBattleOpenerDecision, decision)) return;
        await RecoverBattleOpenerAutoAsync(decision,
            $"RAID did not apply the submitted {decision.Action} within {verificationTimeout.TotalSeconds:0} seconds.", snapshot: lastBattleSnapshot);
    }

    private async Task RecoverBattleOpenerAutoAsync(BattleOpenerDecision decision, string reason, Exception? exception = null, BattleSnapshotMessage? snapshot = null)
    {
        if (!ReferenceEquals(pendingBattleOpenerDecision, decision)) return;
        if (exception is not null) Log.Error("Live Arena opener action failed; Auto recovery requested.", exception);
        battleDiagnostics.RecordMarker("action-failed", reason, pendingBattleActionId);
        var recoverySnapshot = snapshot ?? lastBattleSnapshot ?? pendingBattleOpenerSnapshot;
        if (decision.Action == "auto")
        {
            ClearPendingBattleAction();
            if (battleAutoRetryCount >= 1)
            {
                StopArenaFailClosed($"Auto mode could not be verified after one retry: {reason}", exception);
                return;
            }
            battleAutoRetryCount++;
            battleAutoRecoveryRequired = true;
            AddBattleEvent($"Auto verification failed: {reason} Retrying once.");
        }
        else
        {
            var failedSnapshot = pendingBattleOpenerSnapshot ?? recoverySnapshot;
            if (failedSnapshot is not null)
            {
                battleOpenerRecoveryTurn = failedSnapshot.Turn;
                battleOpenerRecoveryHeroId = failedSnapshot.ActiveHeroId;
            }
            ClearPendingBattleAction();
            battleAutoRetryCount = 0;
            battleAutoRecoveryRequired = true;
            AddBattleEvent($"Opener step kept pending after failure: {reason} Resuming Auto mode once; retry waits for a new turn or active hero.");
        }
        if (recoverySnapshot is not null) await ContinueBattleAutoRecoveryAsync(recoverySnapshot);
    }

    private async Task ContinueBattleAutoRecoveryAsync(BattleSnapshotMessage snapshot)
    {
        if (!battleAutoRecoveryRequired || pendingBattleOpenerDecision is not null || !snapshot.Active || snapshot.Finished) return;
        if (snapshot.AutoMode)
        {
            battleAutoRecoveryRequired = false;
            battleAutoRetryCount = 0;
            battleInitialAutoVerified = true;
            battleDiagnostics.RecordMarker("auto-recovered", "RAID already exposes Auto mode.");
            return;
        }
        if (!snapshot.HudVisible || !snapshot.ModeChangeAvailable) return;
        var decision = new BattleOpenerDecision("auto", 0, -1, 0, 0, false,
            battleAutoRetryCount > 0 ? "Retrying Auto mode after a failed verification." : "Restoring Auto mode after a failed opening skill.");
        try { await SubmitBattleOpenerActionAsync(decision, snapshot); }
        catch (Exception exception)
        {
            if (pendingBattleOpenerDecision is not null)
                await RecoverBattleOpenerAutoAsync(pendingBattleOpenerDecision, $"RAID rejected Auto recovery: {exception.Message}", exception, snapshot);
            else StopArenaFailClosed($"Live Arena opener recovery failed: {exception.Message}", exception);
        }
    }

    private void ClearPendingBattleAction()
    {
        pendingBattleOpenerDecision = null;
        pendingBattleOpenerSnapshot = null;
        pendingBattleActionId = 0;
    }

    private bool IsBattleOpenerRecoveryBlocked(BattleSnapshotMessage snapshot)
    {
        if (battleOpenerRecoveryTurn < 0) return false;
        if (snapshot.Turn != battleOpenerRecoveryTurn || snapshot.ActiveHeroId != battleOpenerRecoveryHeroId)
        {
            battleOpenerRecoveryTurn = -1;
            battleOpenerRecoveryHeroId = -1;
            return false;
        }
        return true;
    }

    private bool TryRegisterBattleModeTransition(BattleOpenerDecision decision, BattleSnapshotMessage snapshot)
    {
        if (decision.Action is not ("auto" or "manual")) return true;
        if (snapshot.Turn != battleModeTransitionTurn || snapshot.ActiveHeroId != battleModeTransitionHeroId)
        {
            battleModeTransitionTurn = snapshot.Turn;
            battleModeTransitionHeroId = snapshot.ActiveHeroId;
            battleModeTransitionCount = 0;
        }
        if (battleModeTransitionCount >= 3)
        {
            StopArenaFailClosed($"Live Arena opener exceeded the mode-transition limit for turn {snapshot.Turn}.");
            return false;
        }
        battleModeTransitionCount++;
        return true;
    }

    private void ResetBattleOpenerGuards()
    {
        battleOpenerRecoveryTurn = -1;
        battleOpenerRecoveryHeroId = -1;
        battleModeTransitionTurn = -1;
        battleModeTransitionHeroId = -1;
        battleModeTransitionCount = 0;
    }

    private void StopArenaFailClosed(string message, Exception? exception = null)
    {
        if (exception is not null) Log.Error("Live Arena automation stopped.", exception);
        arenaMode = LiveArenaAutomationMode.Off;
        continuousArenaSession = false;
        pendingArenaDecision = null;
        pendingArenaSessionDecision = null;
        rewardBatchInProgress = false;
        rewardClaimBaseline = 0;
        pendingBattleOpenerDecision = null;
        pendingBattleOpenerSnapshot = null;
        pendingBattleActionId = 0;
        battleAutoRecoveryRequired = false;
        battleAutoRetryCount = 0;
        ResetBattleOpenerGuards();
        battleSkillStabilizationPending = false;
        ArenaStrategyStatusText.Text = $"Stopped fail-closed: {message}";
        AddBattleEvent(ArenaStrategyStatusText.Text);
        UpdateArenaModeUi();
    }

    private void AddBattleEvent(string message)
    {
        battleEvents.Add($"{DateTime.Now:HH:mm:ss}  {message}");
        while (battleEvents.Count > 100) battleEvents.RemoveAt(0);
        BattleEventList.ScrollIntoView(battleEvents[^1]);
    }

    private bool MatchesFilter(object item)
    {
        var row = (ChampionRow)item;
        return ChampionFilter.Apply([row], SearchBox.Text, FilterValue(RarityFilter), FilterValue(AffinityFilter), FilterValue(LocationFilter)).Any();
    }

    private static string? FilterValue(ComboBox filter) =>
        (filter.SelectedItem as string)?.Split(':', 2).Last().Trim();

    private void Filter_Changed(object sender, EventArgs e) => ApplyFilter();

    private void ClearChampionFilters_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Clear();
        RarityFilter.SelectedIndex = AffinityFilter.SelectedIndex = LocationFilter.SelectedIndex = 0;
        SearchBox.Focus();
    }

    private async void OnAutomation(AutomationMessage message)
    {
        Log.Info($"Automation state {message.State}: {message.Message}");
        battleDiagnostics.RecordAutomation(message, pendingBattleActionId == 0 ? null : pendingBattleActionId);
        ArenaStrategyStatusText.Text = message.Message;
        if (message.State == "battle-error")
        {
            if (pendingBattleOpenerDecision is not null)
                await RecoverBattleOpenerAutoAsync(pendingBattleOpenerDecision, message.Message, snapshot: lastBattleSnapshot);
            else BattleDiagnosticStatusText.Text = $"Battle diagnostic error: {message.Message}";
            UpdateArenaModeUi();
            return;
        }
        if (message.State == "battle-diagnostics")
        {
            BattleDiagnosticStatusText.Text = message.Message;
            return;
        }
        if (message.State == "mythical-click-trace")
        {
            BattleDiagnosticStatusText.Text = message.Message;
            if (message.Message.Contains("stopped", StringComparison.OrdinalIgnoreCase))
            {
                mythicalClickTrace.Stop("probe-stopped");
                MythicalClickTraceButton.Content = "Record Mythical click path";
                UpdateDiagnosticIndicator();
            }
            return;
        }
        if (message.State == "reward-diagnostics-error")
        {
            rewardDiagnostics.Stop("probe-error");
            RewardDiagnosticButton.Content = "Record reward trace";
            ArenaStrategyStatusText.Text = $"Reward trace stopped: {message.Message}";
            UpdateDiagnosticIndicator();
            return;
        }
        if (pendingArenaSessionDecision is { Action: "return" } returnDecision
            && LiveArenaSessionPlanner.IsDeferredReturn(returnDecision.Action, message.State, message.Message))
        {
            pendingArenaSessionDecision = null;
            deferredLiveArenaReturnAttempts++;
            if (deferredLiveArenaReturnAttempts > LiveArenaSessionPlanner.DeferredReturnMaxAttempts)
            {
                deferredLiveArenaReturnRetryPending = false;
                arenaMode = LiveArenaAutomationMode.Off;
                continuousArenaSession = false;
                ArenaStrategyStatusText.Text = $"Stopped fail-closed: the Live Arena result screen did not become actionable after {LiveArenaSessionPlanner.DeferredReturnMaxAttempts} retries.";
                AddBattleEvent(ArenaStrategyStatusText.Text);
                UpdateArenaModeUi();
                return;
            }
            deferredLiveArenaReturnRetryPending = true;
            ArenaStrategyStatusText.Text = $"Waiting for the Live Arena result screen to become actionable (retry {deferredLiveArenaReturnAttempts}/{LiveArenaSessionPlanner.DeferredReturnMaxAttempts}).";
            AddBattleEvent(ArenaStrategyStatusText.Text);
            _ = RetryDeferredLiveArenaReturnAsync();
            UpdateArenaModeUi();
            return;
        }
        if (message.State == "live-session-submitted" && pendingArenaSessionDecision?.Action == "return")
        {
            deferredLiveArenaReturnAttempts = 0;
            deferredLiveArenaReturnRetryPending = false;
        }
        if (message.State == "live-error") arenaMode = LiveArenaAutomationMode.Off;
        if (message.State == "live-error")
        {
            continuousArenaSession = false;
            pendingArenaDecision = null;
            pendingArenaSessionDecision = null;
            deferredLiveArenaReturnRetryPending = false;
            rewardBatchInProgress = false;
            rewardClaimBaseline = 0;
            pendingBattleOpenerDecision = null;
            pendingBattleOpenerSnapshot = null;
            battleSkillStabilizationPending = false;
        }
        UpdateArenaModeUi();
    }

    private void HydrateArenaPool()
    {
        if (arenaPool.Count > 0 || presetLineupSlots.Any(slot => slot.Candidates.Count > 0))
            arenaStrategy = CaptureArenaStrategy(false);
        RebuildArenaPool();
        RebuildPresetLineup();
        RebuildPickRules();
        leaderPriorityReviewed = arenaStrategy.LeaderPriorityReviewed;
        RebuildLeaderPriorities(true);
        pickRuleReplacementView.Refresh();
        UpdateArenaModeUi();
    }

    private async Task EnforceRewardClaimStateDeadlineAsync(int baseline, DateTime requestedAt)
    {
        try { await Task.Delay(TimeSpan.FromSeconds(15), lifetime.Token); }
        catch (OperationCanceledException) { return; }
        if (rewardClaimBaseline != baseline || rewardClaimAt != requestedAt) return;
        rewardClaimBaseline = 0;
        rewardBatchInProgress = false;
        pendingArenaSessionDecision = null;
        arenaMode = LiveArenaAutomationMode.Off;
        continuousArenaSession = false;
        ArenaStrategyStatusText.Text = "Stopped fail-closed: RAID did not confirm the Live Arena reward collection state within 15 seconds.";
        AddBattleEvent(ArenaStrategyStatusText.Text);
        UpdateArenaModeUi();
    }

    private void RebuildArenaPool()
    {
        foreach (var row in arenaPool) row.PropertyChanged -= ArenaPoolRow_PropertyChanged;
        arenaPool.Clear();
        arenaLeaderPriority.Clear();
        foreach (var candidate in arenaStrategy.Pool.OrderBy(candidate => candidate.Priority))
        {
            var champion = teamCandidates.FirstOrDefault(row => row.Instance.Id == candidate.InstanceId && row.Instance.BaseId == candidate.BaseId);
            if (champion is null) continue;
            var row = new ArenaPoolRow { Champion = champion, Roles = candidate.Roles, Priority = candidate.Priority, LeaderPriority = candidate.LeaderPriority };
            row.PropertyChanged += ArenaPoolRow_PropertyChanged;
            arenaPool.Add(row);
        }
        NormalizeArenaPriorities();
    }

    private void RebuildPresetLineup()
    {
        foreach (var slot in presetLineupSlots) slot.Candidates.Clear();
        var savedSlots = arenaStrategy.PresetLineup ?? ArenaStrategyFile.EmptyPresetLineup();
        for (var slotIndex = 0; slotIndex < Math.Min(5, savedSlots.Count); slotIndex++)
            foreach (var candidate in savedSlots[slotIndex].Candidates)
            {
                var champion = teamCandidates.FirstOrDefault(row => row.Instance.Id == candidate.InstanceId && row.Instance.BaseId == candidate.BaseId);
                if (champion is not null) presetLineupSlots[slotIndex].Candidates.Add(new(champion));
            }
        NormalizePresetLineup();
    }

    private void RebuildPickRules()
    {
        arenaPickRules.Clear();
        foreach (var rule in arenaStrategy.PickRules ?? [])
        {
            var portrait = teamCandidates.FirstOrDefault(champion => champion.Instance.BaseId == rule.Replacement.BaseId)?.Portrait
                ?? arenaCatalog.FirstOrDefault(champion => champion.BaseId == rule.Replacement.BaseId)?.Portrait;
            arenaPickRules.Add(new(rule, PickRuleSummary(rule), portrait));
            var catalog = arenaCatalog.FirstOrDefault(champion => champion.BaseId == rule.Replacement.BaseId);
            if (catalog is not null) _ = LoadCatalogPortraitAsync(catalog);
        }
    }

    private string PickRuleSummary(ArenaPickRule rule)
    {
        var names = rule.EnemyBaseIds.Select(baseId => arenaCatalog.FirstOrDefault(champion => champion.BaseId == baseId)?.Name ?? $"#{baseId}");
        var match = rule.EnemyMatch switch { ArenaChampionMatch.Any => "any of", ArenaChampionMatch.All => "all of", _ => "none of" };
        var filters = new List<string>();
        if (rule.MinimumEnemyRoleCount > 0) filters.Add($"{rule.MinimumEnemyRoleCount}+ {rule.EnemyRoles}");
        if (rule.PlayerBaseIds is { Count: > 0 }) filters.Add($"our picks {rule.PlayerMatch.ToString().ToLowerInvariant()} of {rule.PlayerBaseIds.Count}");
        if (rule.DraftRule != ArenaPickRuleDraft.Any) filters.Add($"{rule.DraftRule.ToString().ToLowerInvariant()} draft");
        if (rule.FirstTurn != ArenaPickRuleFirstTurn.Any) filters.Add($"{rule.FirstTurn.ToString().ToLowerInvariant()} first");
        if (rule.MinimumVisibleEnemyPicks > 0) filters.Add($"{rule.MinimumVisibleEnemyPicks}+ enemy picks");
        var suffix = filters.Count == 0 ? string.Empty : $" AND {string.Join(", ", filters)}";
        return $"WHEN {match} {string.Join(", ", names)}{suffix} â†’ {rule.Replacement.Name}";
    }

    private void RebuildLeaderPriorities(bool restoreReview = false)
    {
        arenaLeaderPriority.Clear();
        var available = arenaDraftMode == ArenaDraftMode.AdaptiveDraft
            ? arenaPool.AsEnumerable()
            : presetLineupSlots.SelectMany(slot => slot.Candidates).Select(candidate => new ArenaPoolRow
            {
                Champion = candidate.Champion,
                Roles = arenaRoleCatalog.RolesFor(candidate.BaseId) is var roles && roles != ArenaRole.None ? roles : ArenaRole.Utility
            }).Concat(arenaPickRules.Select(rule => rule.ToRule().Replacement)
                .Select(candidate => teamCandidates.FirstOrDefault(champion => champion.Instance.Id == candidate.InstanceId && champion.Instance.BaseId == candidate.BaseId))
                .Where(champion => champion is not null)
                .Select(champion => new ArenaPoolRow
                {
                    Champion = champion!,
                    Roles = arenaRoleCatalog.RolesFor(champion!.Instance.BaseId) is var roles && roles != ArenaRole.None ? roles : ArenaRole.Utility
                }));
        var order = (arenaStrategy.LeaderPriority ?? []).Select((baseId, priority) => (baseId, priority)).ToDictionary(item => item.baseId, item => item.priority);
        foreach (var row in available.DistinctBy(row => row.BaseId).OrderBy(row => order.TryGetValue(row.BaseId, out var priority) ? priority : int.MaxValue).ThenBy(row => row.Priority))
            arenaLeaderPriority.Add(row);
        NormalizeArenaPriorities();
        if (!restoreReview) leaderPriorityReviewed = false;
    }

    private void NormalizePresetLineup()
    {
        foreach (var slot in presetLineupSlots)
            for (var index = 0; index < slot.Candidates.Count; index++) slot.Candidates[index].Order = index;
    }

    private void ArenaChampionSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (arenaCandidateView is null) return;
        arenaCandidateView.Refresh();
        ArenaCandidateBox.SelectedIndex = arenaCandidateView.IsEmpty ? -1 : 0;
    }

    private void ArenaDraftRosterSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (arenaCandidateView is null) return;
        if (ArenaChampionSearchBox.Text != ArenaDraftRosterSearchBox.Text) ArenaChampionSearchBox.Text = ArenaDraftRosterSearchBox.Text;
        arenaCandidateView.Refresh();
        ArenaDraftRosterCandidateBox.SelectedIndex = arenaCandidateView.IsEmpty ? -1 : 0;
        foreach (var row in arenaCandidateView.Cast<ChampionRow>().Take(24)) _ = LoadPortraitAsync(row);
    }

    private void AddDraftRosterChampion_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaDraftRosterCandidateBox.SelectedItem is not ChampionRow champion) return;
        ArenaCandidateBox.SelectedItem = champion;
        AddArenaChampion_Click(sender, e);
    }

    private void RemoveDraftRosterChampion_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaDraftRosterGrid.SelectedItem is not ArenaPoolRow row) return;
        ArenaPoolGrid.SelectedItem = row;
        RemoveArenaChampion_Click(sender, e);
    }

    private void ApplyDraftRosterRolePreset_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaDraftRosterGrid.SelectedItem is not ArenaPoolRow row || ArenaDraftRosterRolePresetBox.SelectedItem is not string preset) return;
        ArenaPoolGrid.SelectedItem = row;
        ArenaRolePresetBox.SelectedItem = preset;
        ApplyArenaRolePreset_Click(sender, e);
        ArenaDraftRosterGrid.Items.Refresh();
    }

    private void PresetChampionSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (presetCandidateView is null) return;
        presetCandidateView.Refresh();
        PresetCandidateBox.SelectedIndex = presetCandidateView.IsEmpty ? -1 : 0;
    }

    private void ChoosePresetChampion_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PresetLineupSlotRow slot }) return;
        PresetSlotBox.SelectedItem = slot;
        PresetLineupList.SelectedItem = slot;
        OpenChampionPicker(slot, slot.HasPrimary ? ChampionPickerIntent.Substitute : ChampionPickerIntent.Primary, sender as FrameworkElement);
    }

    private void SelectBoardCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PresetLineupCandidateRow candidate }) return;
        var slot = presetLineupSlots.FirstOrDefault(item => item.Candidates.Contains(candidate));
        if (slot is null) return;
        ArenaBoardPresetList.SelectedItem = slot;
        OpenChampionPicker(slot, candidate.Order == 0 ? ChampionPickerIntent.ReplacePrimary : ChampionPickerIntent.ReplaceSubstitute, sender as FrameworkElement, candidate);
    }

    private void ReplacePrimary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PresetLineupSlotRow slot } || slot.Primary is null) return;
        ArenaBoardPresetList.SelectedItem = slot;
        OpenChampionPicker(slot, ChampionPickerIntent.ReplacePrimary, sender as FrameworkElement, slot.Primary);
    }

    private void ReplacePresetCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PresetLineupCandidateRow candidate }) return;
        var slot = presetLineupSlots.FirstOrDefault(item => item.Candidates.Contains(candidate));
        if (slot is null) return;
        ArenaBoardPresetList.SelectedItem = slot;
        OpenChampionPicker(slot, ChampionPickerIntent.ReplaceSubstitute, sender as FrameworkElement, candidate);
    }

    private void RemovePrimary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PresetLineupSlotRow slot } || slot.Primary is null) return;
        RemovePrimary(slot);
    }

    private void ClearPresetSlot_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: PresetLineupSlotRow slot } || !slot.HasPrimary) return;
        ClearPresetSlot(slot);
    }

    private void ArenaBoardPresetList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is not (Key.Delete or Key.Back) || ArenaBoardPresetList.SelectedItem is not PresetLineupSlotRow slot) return;
        ClearPresetSlot(slot);
        e.Handled = true;
    }

    private void PresetCandidateList_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not ListBox list || list.SelectedItem is not PresetLineupCandidateRow candidate) return;
        if (e.Key == Key.P)
        {
            PromotePresetCandidate(candidate);
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Delete or Key.Back)) return;
        RemovePresetCandidate(candidate);
        e.Handled = true;
    }

    private void OpenChampionPicker(PresetLineupSlotRow slot, ChampionPickerIntent intent, FrameworkElement? returnFocus, PresetLineupCandidateRow? candidate = null)
    {
        if (arenaMode != LiveArenaAutomationMode.Off) return;
        championPickerSlot = slot;
        championPickerIntent = intent;
        championPickerCandidate = candidate;
        championPickerReturnFocus = returnFocus;
        ArenaBoardChampionPickerTitle.Text = intent switch
        {
            ChampionPickerIntent.Primary => $"CHOOSE PRIMARY FOR {slot.Label}",
            ChampionPickerIntent.ReplacePrimary => $"REPLACE PRIMARY IN {slot.Label}",
            ChampionPickerIntent.ReplaceSubstitute => $"REPLACE SUBSTITUTE IN {slot.Label}",
            _ => $"ADD SUBSTITUTE TO {slot.Label}"
        };
        ArenaBoardChampionPickerContext.Text = intent switch
        {
            ChampionPickerIntent.Primary => "The primary champion anchors this tactical lane.",
            ChampionPickerIntent.ReplacePrimary => "Choose a new primary; ordered substitutes stay in place.",
            ChampionPickerIntent.ReplaceSubstitute => "Replace this fallback without changing the remaining order.",
            _ => "Choose the next legal fallback for this lane."
        };
        ArenaBoardChampionPickerCurrent.Text = slot.Candidates.Count == 0
            ? "Current lane: empty"
            : $"Current lane: {string.Join("  â†’  ", slot.Candidates.Select(candidate => candidate.Name))}";
        ArenaBoardChampionPickerSearchBox.Clear();
        ArenaBoardChampionPickerList.SelectedIndex = -1;
        ArenaBoardChampionPickerOverlay.Visibility = Visibility.Visible;
        ArenaBoardChampionPickerOverlay.IsHitTestVisible = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            ArenaBoardChampionPickerSearchBox.Focus();
            ArenaBoardChampionPickerSearchBox.SelectAll();
        }));
    }

    private void ChampionPickerSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (presetCandidateView is null) return;
        if (PresetChampionSearchBox.Text != ArenaBoardChampionPickerSearchBox.Text) PresetChampionSearchBox.Text = ArenaBoardChampionPickerSearchBox.Text;
        presetCandidateView.Refresh();
        ArenaBoardChampionPickerList.SelectedIndex = -1;
        foreach (var row in presetCandidateView.Cast<ChampionRow>().Take(24)) _ = LoadPortraitAsync(row);
    }

    private void ChampionPickerCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ChampionRow champion }) ApplyChampionPickerSelection(champion);
    }

    private void ChampionPickerList_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && ArenaBoardChampionPickerList.SelectedItem is ChampionRow champion)
        {
            ApplyChampionPickerSelection(champion);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            CloseChampionPicker(sender, e);
        }
    }

    private void ChampionPickerOverlay_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) CloseChampionPicker(sender, e);
    }

    private void CloseChampionPicker_Click(object sender, RoutedEventArgs e) => CloseChampionPicker(sender, e);

    private void CloseChampionPicker(object? sender, RoutedEventArgs e)
    {
        ArenaBoardChampionPickerOverlay.Visibility = Visibility.Collapsed;
        ArenaBoardChampionPickerOverlay.IsHitTestVisible = false;
        championPickerSlot = null;
        championPickerIntent = null;
        championPickerCandidate = null;
        var focus = championPickerReturnFocus;
        championPickerReturnFocus = null;
        if (focus is not null) Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() => focus.Focus()));
    }

    private void ApplyChampionPickerSelection(ChampionRow champion)
    {
        if (championPickerSlot is not PresetLineupSlotRow slot || championPickerIntent is null) return;
        var replacing = championPickerIntent is ChampionPickerIntent.ReplacePrimary or ChampionPickerIntent.ReplaceSubstitute;
        var all = presetLineupSlots.SelectMany(item => item.Candidates).Where(candidate => !ReferenceEquals(candidate, championPickerCandidate)).ToArray();
        if (!replacing && all.Length >= 20)
        {
            ArenaBoardChampionPickerCurrent.Text = "Preset Lineup is limited to 20 unique champions.";
            return;
        }
        if (all.Any(candidate => candidate.InstanceId == champion.Instance.Id || candidate.BaseId == champion.Instance.BaseId))
        {
            ArenaBoardChampionPickerCurrent.Text = $"{champion.Name} is already used in this lineup.";
            return;
        }
        PushArenaUndo();
        if (replacing && championPickerCandidate is not null)
        {
            var index = slot.Candidates.IndexOf(championPickerCandidate);
            if (index < 0)
            {
                ArenaBoardChampionPickerCurrent.Text = "That lane changed before the replacement was applied.";
                return;
            }
            slot.Candidates[index] = new(champion);
        }
        else
        {
            slot.Candidates.Add(new(champion));
        }
        NormalizePresetLineup();
        RebuildLeaderPriorities();
        PresetSlotBox.SelectedItem = slot;
        PresetLineupList.SelectedItem = slot;
        var message = championPickerIntent switch
        {
            ChampionPickerIntent.Primary => $"{slot.Label}: {champion.Name} is now the primary.",
            ChampionPickerIntent.ReplacePrimary => $"{slot.Label}: primary replaced with {champion.Name}.",
            ChampionPickerIntent.ReplaceSubstitute => $"{slot.Label}: substitute replaced with {champion.Name}.",
            _ => $"{slot.Label}: {champion.Name} added as the next fallback."
        };
        AutoSaveArenaStrategy(message);
        ArenaBoardPresetHint.Text = message;
        UpdateArenaModeUi();
        CloseChampionPicker(this, new RoutedEventArgs());
    }

    private void ArenaBoardBanSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (arenaBanView is null) return;
        if (ArenaBanSearchBox.Text != ArenaBoardBanSearchBox.Text) ArenaBanSearchBox.Text = ArenaBoardBanSearchBox.Text;
        arenaBanView.Refresh();
        ArenaBoardBanCandidateBox.SelectedIndex = arenaBanView.IsEmpty ? -1 : 0;
        foreach (var row in arenaBanView.Cast<ArenaCatalogRow>().Take(24)) _ = LoadCatalogPortraitAsync(row);
    }

    private void AddBoardBanPriority_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaBoardBanCandidateBox.SelectedItem is not ArenaCatalogRow champion) return;
        ArenaBanCandidateBox.SelectedItem = champion;
        AddBanPriority_Click(sender, e);
    }

    private void ArenaBoardStep_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string tag }) return;
        if (tag == "PickRules" && arenaDraftMode == ArenaDraftMode.AdaptiveDraft)
        {
            ArenaBoardStatusText.Text = "Pick Rules are available in Preset Lineup mode and remain optional.";
            return;
        }

        arenaBoardStep = tag switch
        {
            "BanPlan" => ArenaBoardStep.BanPlan,
            "PickRules" => ArenaBoardStep.PickRules,
            "Leader" => ArenaBoardStep.Leader,
            "Review" => ArenaBoardStep.Review,
            _ => ArenaBoardStep.Lineup
        };
        UpdateArenaStepUi();
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            switch (arenaBoardStep)
            {
                case ArenaBoardStep.Lineup:
                    if (arenaDraftMode == ArenaDraftMode.AdaptiveDraft) ArenaDraftRosterGrid.Focus();
                    else ArenaBoardPresetList.Focus();
                    break;
                case ArenaBoardStep.BanPlan: ArenaBoardBanSearchBox.Focus(); break;
                case ArenaBoardStep.PickRules: ArenaBoardPickRulesList.Focus(); break;
                case ArenaBoardStep.Leader: ArenaBoardLeaderList.Focus(); break;
                case ArenaBoardStep.Review: ArenaBoardReviewPanel.Focus(); break;
            }
        }));
    }

    private void UpdateArenaStepUi()
    {
        if (!IsInitialized) return;
        var automationActive = arenaMode != LiveArenaAutomationMode.Off;
        var lineup = arenaBoardStep == ArenaBoardStep.Lineup;
        var priorityEditor = arenaBoardStep is ArenaBoardStep.BanPlan or ArenaBoardStep.PickRules or ArenaBoardStep.Leader;
        ArenaBoardEditorPanel.Visibility = !automationActive && lineup ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardStrategyRail.Visibility = !automationActive && priorityEditor ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardReviewPanel.Visibility = !automationActive && arenaBoardStep == ArenaBoardStep.Review ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardLiveSessionPanel.Visibility = automationActive ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardRailToggle.Visibility = Visibility.Collapsed;
        ArenaBoardStepLineupButton.IsEnabled = !automationActive;
        ArenaBoardStepBanButton.IsEnabled = !automationActive;
        ArenaBoardStepRulesButton.IsEnabled = !automationActive && arenaDraftMode == ArenaDraftMode.PresetLineup;
        ArenaBoardStepLeaderButton.IsEnabled = !automationActive;
        ArenaBoardStepReviewButton.IsEnabled = !automationActive;
        ArenaBoardConfirmLeaderButton.Visibility = !automationActive && arenaBoardStep == ArenaBoardStep.Leader ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardConfirmLeaderButton.IsEnabled = !automationActive && arenaLeaderPriority.Count > 0 && !leaderPriorityReviewed;
        ArenaBoardConfirmLeaderButton.Content = leaderPriorityReviewed ? "Leader order reviewed" : "Confirm leader order";

        if (arenaBoardStep == ArenaBoardStep.BanPlan) ArenaBoardStrategyTabs.SelectedItem = ArenaBoardBanTab;
        if (arenaBoardStep == ArenaBoardStep.PickRules) ArenaBoardStrategyTabs.SelectedItem = ArenaBoardPickRulesTab;
        if (arenaBoardStep == ArenaBoardStep.Leader) ArenaBoardStrategyTabs.SelectedItem = ArenaBoardLeaderTab;

        ArenaBoardCentralTitle.Text = arenaBoardStep switch
        {
            ArenaBoardStep.BanPlan => "BAN PLAN",
            ArenaBoardStep.PickRules => "PICK RULES",
            ArenaBoardStep.Leader => "LEADER ORDER",
            _ => "STRATEGY EDITOR"
        };
        ArenaBoardCentralSubtitle.Text = arenaBoardStep switch
        {
            ArenaBoardStep.BanPlan => "Recommended: define the order the bot should use when an explicit ban target is configured.",
            ArenaBoardStep.PickRules => "Optional overrides. The first matching rule wins; the default lineup order remains safe when no rule exists.",
            ArenaBoardStep.Leader => "Review the generated order, then confirm the preferred leader and fallback sequence before running.",
            _ => "Configure the decisions that Review & Run will summarize."
        };

        SetStepButtonState(ArenaBoardStepLineupButton, lineup);
        SetStepButtonState(ArenaBoardStepBanButton, arenaBoardStep == ArenaBoardStep.BanPlan);
        SetStepButtonState(ArenaBoardStepRulesButton, arenaBoardStep == ArenaBoardStep.PickRules);
        SetStepButtonState(ArenaBoardStepLeaderButton, arenaBoardStep == ArenaBoardStep.Leader);
        SetStepButtonState(ArenaBoardStepReviewButton, arenaBoardStep == ArenaBoardStep.Review);
        UpdateArenaStepStates();
        UpdateArenaReview();
    }

    private void SetStepButtonState(Button button, bool active)
    {
        button.Background = (Brush)FindResource(active ? "RaisedHoverBrush" : "SurfaceBrush");
        button.BorderBrush = (Brush)FindResource(active ? "GoldBrush" : "LineBrush");
    }

    private void UpdateArenaStepStates()
    {
        var lineupReady = arenaDraftMode == ArenaDraftMode.AdaptiveDraft
            ? arenaPool.Count >= 5
            : presetLineupSlots.Count == 5 && presetLineupSlots.All(slot => slot.HasPrimary);
        var banReady = arenaBanPriority.Count > 0;
        var rulesReady = arenaPickRules.Count == 0 || arenaPickRules.Any(rule => rule.Enabled);
        var leaderReady = arenaLeaderPriority.Count > 0;
        var reviewReady = probe is not null && lineupReady && leaderPriorityReviewed;

        ArenaBoardStepLineupState.Text = $"Required Â· {(lineupReady ? "Ready" : "Incomplete")}";
        ArenaBoardStepLineupProgress.Text = lineupReady ? "â—" : "â—‹";
        ArenaBoardStepBanState.Text = banReady
            ? $"Recommended Â· {arenaBanPriority.Count} configured Â· Ready"
            : "Recommended Â· No explicit bans Â· Needs review";
        ArenaBoardStepBanProgress.Text = banReady ? "â—" : "â—";
        ArenaBoardStepRulesState.Text = arenaPickRules.Count == 0
            ? "Optional Â· None Â· Ready"
            : $"Optional Â· {arenaPickRules.Count(rule => rule.Enabled)} active Â· Ready";
        ArenaBoardStepRulesProgress.Text = rulesReady ? "â—" : "â—";
        ArenaBoardStepLeaderState.Text = !leaderReady
            ? "Review required Â· Incomplete"
            : leaderPriorityReviewed ? "Reviewed" : "Needs review";
        ArenaBoardStepLeaderProgress.Text = !leaderReady ? "â—‹" : leaderPriorityReviewed ? "â—" : "â—";
        ArenaBoardStepReviewState.Text = probe is null
            ? "Blocked Â· Connect RAID"
            : !lineupReady ? "Blocked Â· Lineup incomplete"
            : !leaderReady || !leaderPriorityReviewed ? "Needs review Â· Confirm leader order"
            : "Ready Â· Reviewed strategy";
        ArenaBoardStepReviewProgress.Text = reviewReady ? "â—" : "â—";
    }

    private void UpdateArenaReview()
    {
        if (!IsInitialized) return;
        var lineup = arenaDraftMode == ArenaDraftMode.AdaptiveDraft
            ? $"Adaptive pool: {arenaPool.Count} champion{(arenaPool.Count == 1 ? string.Empty : "s")}\n{string.Join(" Â· ", arenaPool.Select(row => row.Name))}"
            : string.Join("\n", presetLineupSlots.Select(slot => $"{slot.Label}: {(slot.HasPrimary ? string.Join(" â†’ ", slot.Candidates.Select(candidate => candidate.Name)) : "Empty")}"));
        ArenaBoardReviewLineupText.Text = lineup;

        ArenaBoardReviewBanText.Text = arenaBanPriority.Count == 0
            ? "Recommended Â· No explicit ban order. The engine fallback remains available."
            : string.Join("\n", arenaBanPriority.Select(row => $"{row.OrderLabel}: {row.Name}"));
        ArenaBoardReviewRulesText.Text = arenaPickRules.Count == 0
            ? "Optional Â· No pick overrides. The default lineup order will be used unless a rule is added."
            : $"Optional Â· {arenaPickRules.Count(rule => rule.Enabled)} active of {arenaPickRules.Count} configured. The first matching rule wins.";
        ArenaBoardReviewLeaderText.Text = arenaLeaderPriority.Count == 0
            ? "Review required Â· No leader order available."
            : $"{(leaderPriorityReviewed ? "Reviewed" : "Needs review")}\nPreferred leader: {arenaLeaderPriority[0].Name}\n{string.Join("\n", arenaLeaderPriority.Skip(1).Select((row, index) => $"Fallback {index + 1}: {row.Name}"))}";
        ArenaBoardReviewSettingsText.Text = $"Battle limit: {ArenaBattleLimitBox.Text.Trim()}\nAuto-refill: {(ArenaAutoRefillCheckBox.IsChecked == true ? "ON â€” may spend account resources" : "OFF")}";
        ArenaBoardReviewExecutionText.Text = probe is null
            ? "Not executable yet Â· Connect RAID to verify the session."
            : ArenaBoardStepReviewState.Text.StartsWith("Ready", StringComparison.Ordinal)
                ? "Executable Â· Ready to run Â· Strategy reviewed"
                : "Executable with engine fallbacks Â· Strategy review remains required";
        var warnings = new List<string>();
        if (arenaBanPriority.Count == 0) warnings.Add("Recommended: add an explicit ban order to control the bot's ban choice.");
        if (arenaLeaderPriority.Count == 0 || !leaderPriorityReviewed) warnings.Add("Confirm the generated leader order before treating the strategy as reviewed.");
        if (ArenaAutoRefillCheckBox.IsChecked == true) warnings.Add("Auto-refill can spend Live Arena tokens.");
        ArenaBoardReviewWarningsText.Text = string.Join("\n", warnings);
    }

    private void RemoveBoardBanPriority_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ArenaBanPriorityRow row } || arenaMode != LiveArenaAutomationMode.Off) return;
        PushArenaUndo();
        arenaBanPriority.Remove(row);
        AutoSaveArenaStrategy($"Removed {row.Name} from ban priorities; strategy saved automatically.");
        UpdateArenaModeUi();
    }

    private void ToggleArenaPriorities_Click(object sender, RoutedEventArgs e)
    {
        arenaPrioritiesDrawerOpen = !arenaPrioritiesDrawerOpen;
        ApplyLiveArenaLayout();
    }

    private void ArenaBoardSummary_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: string target }) return;
        if (target == "Lineup")
        {
            ArenaExperienceScroll.ScrollToTop();
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
            {
                if (arenaDraftMode == ArenaDraftMode.AdaptiveDraft) ArenaDraftRosterGrid.Focus();
                else ArenaBoardPresetList.Focus();
            }));
            return;
        }

        if (target == "Rules" && arenaDraftMode == ArenaDraftMode.AdaptiveDraft)
        {
            ArenaBoardStatusText.Text = "Pick Rules are optional and available in Preset Lineup mode.";
            ArenaBoardPresetButton.Focus();
            return;
        }

        arenaPrioritiesDrawerOpen = true;
        ApplyLiveArenaLayout();
        ArenaBoardStrategyTabs.SelectedItem = target switch
        {
            "Ban" => ArenaBoardBanTab,
            "Rules" => ArenaBoardPickRulesTab,
            "Leader" => ArenaBoardLeaderTab,
            _ => ArenaBoardBanTab
        };
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            switch (target)
            {
                case "Ban": ArenaBoardBanSearchBox.Focus(); break;
                case "Rules": ArenaBoardPickRulesList.Focus(); break;
                case "Leader": ArenaBoardLeaderList.Focus(); break;
            }
        }));
    }

    private void ConfirmLeaderOrder_Click(object sender, RoutedEventArgs e)
    {
        if (arenaMode != LiveArenaAutomationMode.Off || arenaLeaderPriority.Count == 0) return;
        PushArenaUndo();
        leaderPriorityReviewed = true;
        AutoSaveArenaStrategy("Leader order confirmed and saved automatically.");
        UpdateArenaModeUi();
    }

    private void LiveArenaRoot_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyLiveArenaLayout();

    private void ApplyLiveArenaLayout(double? width = null)
    {
        if (!IsInitialized) return;
        var availableWidth = width ?? ArenaExperienceScroll.ViewportWidth;
        if (availableWidth <= 0) availableWidth = ArenaExperienceScroll.ActualWidth;
        if (availableWidth <= 0) availableWidth = LiveArenaRoot.ActualWidth;
        if (availableWidth <= 0) return;
        var wide = availableWidth >= 1350;
        var compact = availableWidth < 950;
        if (wide) arenaPrioritiesDrawerOpen = true;
        ArenaHeaderTools.MaxWidth = compact ? Math.Max(240, availableWidth - 8) : Math.Max(500, availableWidth * 0.68);
        ArenaBoardHeader.RowDefinitions.Clear();
        ArenaBoardHeader.ColumnDefinitions.Clear();
        if (wide)
        {
            ArenaBoardHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ArenaBoardHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            ArenaBoardHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ArenaBoardHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(ArenaBoardHeaderTools, 1);
            Grid.SetRow(ArenaBoardHeaderTools, 0);
            Grid.SetRow(ArenaDashboardPanel, 1);
            ArenaBoardHeaderTools.Margin = new Thickness(0);
            ArenaBoardHeaderTools.HorizontalAlignment = HorizontalAlignment.Right;
        }
        else
        {
            ArenaBoardHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            ArenaBoardHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ArenaBoardHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            ArenaBoardHeader.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(ArenaBoardHeaderTools, 0);
            Grid.SetRow(ArenaBoardHeaderTools, 1);
            Grid.SetRow(ArenaDashboardPanel, 2);
            ArenaBoardHeaderTools.Margin = new Thickness(0, 8, 0, 0);
            ArenaBoardHeaderTools.HorizontalAlignment = HorizontalAlignment.Left;
        }
        ArenaBoardHeaderTools.MaxWidth = wide ? Math.Max(500, availableWidth * 0.68) : Math.Max(240, availableWidth - 8);
        ArenaPrioritiesColumn.Width = new GridLength(0);
        ArenaPrioritiesPanel.Visibility = Visibility.Collapsed;
        ArenaPrioritiesToggleButton.Visibility = wide ? Visibility.Collapsed : Visibility.Visible;
        ArenaPrioritiesToggleButton.Content = "Priorities";
        ArenaBoardRailColumn.Width = new GridLength(0);
        ArenaBoardRailToggle.Visibility = Visibility.Collapsed;
        ArenaBoardStrategyRail.Width = double.NaN;
        ArenaBoardStrategyRail.HorizontalAlignment = HorizontalAlignment.Stretch;
        ArenaBoardStrategyRail.Margin = new Thickness(0);
        var laneColumns = compact ? 3 : 5;
        ArenaBoardOrderHeader.Visibility = laneColumns == 5 ? Visibility.Visible : Visibility.Collapsed;
        if (FindVisualChild<UniformGrid>(ArenaBoardPresetList) is UniformGrid lanes) lanes.Columns = laneColumns;
        LiveArenaRoot.Margin = compact ? new Thickness(10, 10, 10, 0) : new Thickness(18, 12, 18, 0);
        ArenaExperienceSurface.MinHeight = 0;
        ScrollViewer.SetVerticalScrollBarVisibility(ArenaBoardPresetList, ScrollBarVisibility.Disabled);
        ArenaBoardSessionChip.Visibility = probe is null ? Visibility.Collapsed : Visibility.Visible;
    }

    private void DraftMode_Click(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized || sender is not FrameworkElement { Tag: string value } || !Enum.TryParse<ArenaDraftMode>(value, out var mode)) return;
        if (arenaMode != LiveArenaAutomationMode.Off)
        {
            ArenaStrategyStatusText.Text = "Stop automation before changing the draft mode.";
            UpdateDraftModeUi();
            return;
        }
        if (arenaDraftMode == mode) { UpdateDraftModeUi(); return; }
        PushArenaUndo();
        arenaDraftMode = mode;
        RebuildLeaderPriorities();
        AutoSaveArenaStrategy($"Draft mode changed to {(mode == ArenaDraftMode.AdaptiveDraft ? "Adaptive Draft" : "Preset Lineup")}.");
        UpdateDraftModeUi();
        UpdateArenaModeUi();
    }

    private void AddPresetCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (PresetSlotBox.SelectedItem is not PresetLineupSlotRow slot || PresetCandidateBox.SelectedItem is not ChampionRow champion) return;
        var all = presetLineupSlots.SelectMany(item => item.Candidates).ToArray();
        if (all.Length >= 20) { ArenaStrategyStatusText.Text = "Preset Lineup is limited to 20 unique champions."; return; }
        if (all.Any(candidate => candidate.InstanceId == champion.Instance.Id || candidate.BaseId == champion.Instance.BaseId))
        {
            ArenaStrategyStatusText.Text = "That champion is already used in Preset Lineup.";
            return;
        }
        PushArenaUndo();
        slot.Candidates.Add(new(champion));
        NormalizePresetLineup();
        RebuildLeaderPriorities();
        PresetCandidateBox.IsDropDownOpen = false;
        PresetSelectorHint.Text = $"{slot.Label}: {champion.Name} added. Add another champion to place it after the primary.";
        ArenaBoardPresetHint.Text = $"{slot.Label}: {champion.Name} added. Select the lane again to add another substitute.";
        AutoSaveArenaStrategy($"Added {champion.Name} to {slot.Label.ToLowerInvariant()}.");
        UpdateArenaModeUi();
    }

    private void RemovePresetCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PresetLineupCandidateRow candidate }) return;
        RemovePresetCandidate(candidate);
    }

    private void RemovePresetCandidate(PresetLineupCandidateRow candidate)
    {
        var slot = presetLineupSlots.FirstOrDefault(item => item.Candidates.Contains(candidate));
        if (slot is null) return;
        if (candidate.Order == 0)
        {
            RemovePrimary(slot);
            return;
        }
        PushArenaUndo();
        slot.Candidates.Remove(candidate);
        NormalizePresetLineup();
        RebuildLeaderPriorities();
        var message = $"{slot.Label}: {candidate.Name} removed from the fallback order.";
        AutoSaveArenaStrategy(message);
        PresetSelectorHint.Text = message;
        ArenaBoardPresetHint.Text = message;
        UpdateArenaModeUi();
    }

    private void RemovePrimary(PresetLineupSlotRow slot)
    {
        if (slot.Primary is null) return;
        if (slot.Candidates.Count > 1)
        {
            var blockedMessage = $"{slot.Label}: promote a substitute explicitly before removing the primary, or use Clear lane to remove the whole lane.";
            PresetSelectorHint.Text = blockedMessage;
            ArenaBoardPresetHint.Text = blockedMessage;
            ArenaStrategyStatusText.Text = blockedMessage;
            return;
        }
        var removed = slot.Primary;
        PushArenaUndo();
        slot.Candidates.RemoveAt(0);
        NormalizePresetLineup();
        RebuildLeaderPriorities();
        var message = $"{slot.Label}: {removed.Name} removed; choose a primary to complete this lane.";
        AutoSaveArenaStrategy(message);
        PresetSelectorHint.Text = message;
        ArenaBoardPresetHint.Text = message;
        UpdateArenaModeUi();
    }

    private void PromotePresetCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PresetLineupCandidateRow candidate }) return;
        PromotePresetCandidate(candidate);
    }

    private void PromotePresetCandidate(PresetLineupCandidateRow candidate)
    {
        var slot = presetLineupSlots.FirstOrDefault(item => item.Candidates.Contains(candidate));
        if (slot is null || candidate.Order == 0) return;
        PushArenaUndo();
        slot.Candidates.Move(slot.Candidates.IndexOf(candidate), 0);
        NormalizePresetLineup();
        RebuildLeaderPriorities();
        var message = $"{slot.Label}: {candidate.Name} promoted to primary.";
        AutoSaveArenaStrategy(message);
        PresetSelectorHint.Text = message;
        ArenaBoardPresetHint.Text = message;
        UpdateArenaModeUi();
    }

    private void ClearPresetSlot(PresetLineupSlotRow slot)
    {
        if (!slot.HasPrimary) return;
        PushArenaUndo();
        var removedCount = slot.Candidates.Count;
        slot.Candidates.Clear();
        RebuildLeaderPriorities();
        var message = $"{slot.Label} cleared; {removedCount} champion{(removedCount == 1 ? string.Empty : "s")} removed.";
        AutoSaveArenaStrategy(message);
        PresetSelectorHint.Text = message;
        ArenaBoardPresetHint.Text = message;
        UpdateArenaModeUi();
    }

    private void MovePresetCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: PresetLineupCandidateRow candidate, Tag: string direction }
            || !int.TryParse(direction, out var offset)) return;
        var slot = presetLineupSlots.FirstOrDefault(item => item.Candidates.Contains(candidate));
        if (slot is null) return;
        var from = slot.Candidates.IndexOf(candidate);
        var to = from + offset;
        if (to < 0 || to >= slot.Candidates.Count) return;
        PushArenaUndo();
        slot.Candidates.Move(from, to);
        NormalizePresetLineup();
        AutoSaveArenaStrategy($"Reordered substitutes in {slot.Label.ToLowerInvariant()}.");
    }

    private void AddPickRule_Click(object sender, RoutedEventArgs e) => OpenPickRuleEditor(null);

    private void EditPickRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ArenaPickRuleRow row }) OpenPickRuleEditor(row.ToRule());
    }

    private void OpenPickRuleEditor(ArenaPickRule? rule)
    {
        if (arenaMode != LiveArenaAutomationMode.Off || arenaDraftMode != ArenaDraftMode.PresetLineup) return;
        AttachPickRuleEditorToBoard();
        editingPickRuleId = rule?.Id;
        PickRuleEditorTitle.Text = rule is null ? "NEW PICK RULE" : "EDIT PICK RULE";
        PickRuleNameBox.Text = rule?.Name ?? $"Pick rule {arenaPickRules.Count + 1}";
        PickRuleEnemyMatchBox.SelectedItem = rule?.EnemyMatch ?? ArenaChampionMatch.Any;
        PickRulePlayerMatchBox.SelectedItem = rule?.PlayerMatch ?? ArenaChampionMatch.Any;
        PickRuleDraftBox.SelectedItem = rule?.DraftRule ?? ArenaPickRuleDraft.Any;
        PickRuleFirstTurnBox.SelectedItem = rule?.FirstTurn ?? ArenaPickRuleFirstTurn.Any;
        PickRuleRoleCountBox.SelectedItem = rule?.MinimumEnemyRoleCount is > 0 ? rule.MinimumEnemyRoleCount : 1;
        PickRuleMinimumEnemyBox.SelectedItem = rule?.MinimumVisibleEnemyPicks ?? 0;
        PickRuleTargetSlotBox.SelectedIndex = rule?.TargetSlot ?? 0;
        PickRuleReplacementSearchBox.Text = string.Empty;
        PickRuleEnemySearchBox.Text = string.Empty;
        PickRulePlayerSearchBox.Text = string.Empty;
        PickRuleReplacementBox.SelectedItem = rule is null ? null
            : teamCandidates.FirstOrDefault(champion => champion.Instance.Id == rule.Replacement.InstanceId
                && champion.Instance.BaseId == rule.Replacement.BaseId);
        pickRuleEnemyChampions.Clear();
        foreach (var baseId in rule?.EnemyBaseIds ?? [])
        {
            var champion = arenaCatalog.FirstOrDefault(item => item.BaseId == baseId);
            if (champion is not null) { pickRuleEnemyChampions.Add(champion); _ = LoadCatalogPortraitAsync(champion); }
        }
        pickRulePlayerChampions.Clear();
        foreach (var baseId in rule?.PlayerBaseIds ?? [])
        {
            var champion = arenaCatalog.FirstOrDefault(item => item.BaseId == baseId);
            if (champion is not null) { pickRulePlayerChampions.Add(champion); _ = LoadCatalogPortraitAsync(champion); }
        }
        SetPickRuleRoles(rule?.EnemyRoles ?? ArenaRole.None);
        var hasConditions = rule is not null && (rule.EnemyRoles != ArenaRole.None || rule.PlayerBaseIds is { Count: > 0 }
            || rule.DraftRule != ArenaPickRuleDraft.Any || rule.FirstTurn != ArenaPickRuleFirstTurn.Any || rule.MinimumVisibleEnemyPicks > 0);
        PickRuleConditionsPanel.Visibility = hasConditions ? Visibility.Visible : Visibility.Collapsed;
        PickRuleAddConditionsButton.Content = hasConditions ? "Hide optional conditions" : "Add optional conditions";
        PickRuleEditorPanel.Visibility = Visibility.Visible;
        ArenaBoardPickRuleEditorOverlay.Visibility = Visibility.Visible;
    }

    private void AttachPickRuleEditorToBoard()
    {
        if (pickRuleEditorReparented) return;
        if (PickRuleEditorPanel.Parent is Panel parent) parent.Children.Remove(PickRuleEditorPanel);
        ArenaBoardPickRuleEditorHost.Content = PickRuleEditorPanel;
        pickRuleEditorReparented = true;
    }

    private void CancelPickRuleEdit_Click(object sender, RoutedEventArgs e)
    {
        editingPickRuleId = null;
        PickRuleEditorPanel.Visibility = Visibility.Collapsed;
        ArenaBoardPickRuleEditorOverlay.Visibility = Visibility.Collapsed;
        pickRuleEnemyChampions.Clear();
        pickRulePlayerChampions.Clear();
    }

    private void TogglePickRuleConditions_Click(object sender, RoutedEventArgs e)
    {
        var show = PickRuleConditionsPanel.Visibility != Visibility.Visible;
        PickRuleConditionsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        PickRuleAddConditionsButton.Content = show ? "Hide optional conditions" : "Add optional conditions";
    }

    private void SavePickRule_Click(object sender, RoutedEventArgs e)
    {
        if (arenaMode != LiveArenaAutomationMode.Off) return;
        if (arenaPickRules.Count >= 50 && editingPickRuleId is null) { ArenaStrategyStatusText.Text = "Pick Rules is limited to 50 rules."; return; }
        if (string.IsNullOrWhiteSpace(PickRuleNameBox.Text)) { ArenaStrategyStatusText.Text = "Enter a rule name."; return; }
        if (pickRuleEnemyChampions.Count == 0) { ArenaStrategyStatusText.Text = "Add at least one champion to WHEN ENEMY PICKED."; return; }
        if (PickRuleReplacementBox.SelectedItem is not ChampionRow replacement || PickRuleTargetSlotBox.SelectedItem is not PresetLineupSlotRow target)
        { ArenaStrategyStatusText.Text = "Choose an owned replacement champion and target slot."; return; }
        var roles = CurrentPickRuleRoles();
        var rule = new ArenaPickRule(
            editingPickRuleId ?? Guid.NewGuid(), PickRuleNameBox.Text.Trim(), true,
            PickRuleEnemyMatchBox.SelectedItem is ArenaChampionMatch enemyMatch ? enemyMatch : ArenaChampionMatch.Any,
            pickRuleEnemyChampions.Select(champion => champion.BaseId).ToList(), roles,
            roles == ArenaRole.None ? 0 : PickRuleRoleCountBox.SelectedItem is int roleCount ? roleCount : 1,
            PickRulePlayerMatchBox.SelectedItem is ArenaChampionMatch playerMatch ? playerMatch : ArenaChampionMatch.Any,
            pickRulePlayerChampions.Select(champion => champion.BaseId).ToList(),
            PickRuleDraftBox.SelectedItem is ArenaPickRuleDraft draftRule ? draftRule : ArenaPickRuleDraft.Any,
            PickRuleFirstTurnBox.SelectedItem is ArenaPickRuleFirstTurn firstTurn ? firstTurn : ArenaPickRuleFirstTurn.Any,
            PickRuleMinimumEnemyBox.SelectedItem is int minimumEnemy ? minimumEnemy : 0,
            target.Slot, new(replacement.Instance.Id, replacement.Instance.TypeId, replacement.Instance.BaseId, replacement.Name));
        PushArenaUndo();
        var existing = arenaPickRules.FirstOrDefault(item => item.Id == rule.Id);
        if (existing is null) arenaPickRules.Add(new(rule, PickRuleSummary(rule), replacement.Portrait));
        else existing.Update(rule with { Enabled = existing.Enabled }, PickRuleSummary(rule), replacement.Portrait);
        editingPickRuleId = null;
        PickRuleEditorPanel.Visibility = Visibility.Collapsed;
        ArenaBoardPickRuleEditorOverlay.Visibility = Visibility.Collapsed;
        RebuildLeaderPriorities();
        battleOpenerChampionView.Refresh();
        AutoSaveArenaStrategy($"Pick rule {rule.Name} saved automatically.");
    }

    private void DuplicatePickRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ArenaPickRuleRow source } || arenaPickRules.Count >= 50 || arenaMode != LiveArenaAutomationMode.Off) return;
        PushArenaUndo();
        var index = arenaPickRules.IndexOf(source);
        var copyName = source.Name.Length <= 75 ? $"{source.Name} copy" : $"{source.Name[..75]} copy";
        var copy = source.ToRule() with { Id = Guid.NewGuid(), Name = copyName, Enabled = false };
        arenaPickRules.Insert(index + 1, new(copy, PickRuleSummary(copy), source.Portrait));
        AutoSaveArenaStrategy($"Duplicated pick rule {source.Name}; the copy is disabled.");
    }

    private void RemovePickRule_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: ArenaPickRuleRow row } || arenaMode != LiveArenaAutomationMode.Off) return;
        PushArenaUndo();
        arenaPickRules.Remove(row);
        if (editingPickRuleId == row.Id) CancelPickRuleEdit_Click(sender, e);
        RebuildLeaderPriorities();
        battleOpenerChampionView.Refresh();
        AutoSaveArenaStrategy($"Removed pick rule {row.Name}.");
    }

    private void PickRuleEnabled_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not CheckBox { DataContext: ArenaPickRuleRow row } || arenaMode != LiveArenaAutomationMode.Off) return;
        try
        {
            var current = CaptureArenaStrategy(false);
            undoArenaStrategy = current with { PickRules = current.PickRules!.Select(rule => rule.Id == row.Id ? rule with { Enabled = !row.Enabled } : rule).ToList() };
            ArenaUndoButton.IsEnabled = true;
            ArenaUndoButton.Visibility = Visibility.Visible;
        }
        catch { }
        AutoSaveArenaStrategy($"Pick rule {row.Name} {(row.Enabled ? "enabled" : "disabled")}.");
    }

    private void PickRuleEnemySearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshPickRuleCatalogView(pickRuleEnemyView, PickRuleEnemyCandidateBox);
    private void PickRulePlayerSearch_TextChanged(object sender, TextChangedEventArgs e) => RefreshPickRuleCatalogView(pickRulePlayerView, PickRulePlayerCandidateBox);

    private void RefreshPickRuleCatalogView(ICollectionView candidateView, ComboBox box)
    {
        if (candidateView is null) return;
        candidateView.Refresh();
        box.SelectedIndex = candidateView.IsEmpty ? -1 : 0;
        foreach (var champion in candidateView.Cast<ArenaCatalogRow>().Take(20)) _ = LoadCatalogPortraitAsync(champion);
    }

    private void PickRuleReplacementSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (pickRuleReplacementView is null) return;
        pickRuleReplacementView.Refresh();
        PickRuleReplacementBox.SelectedIndex = pickRuleReplacementView.IsEmpty ? -1 : 0;
    }

    private void AddPickRuleEnemy_Click(object sender, RoutedEventArgs e)
    {
        if (PickRuleEnemyCandidateBox.SelectedItem is ArenaCatalogRow champion && pickRuleEnemyChampions.All(item => item.BaseId != champion.BaseId)
            && pickRuleEnemyChampions.Count < 20) { pickRuleEnemyChampions.Add(champion); _ = LoadCatalogPortraitAsync(champion); }
    }

    private void RemovePickRuleEnemy_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ArenaCatalogRow champion }) pickRuleEnemyChampions.Remove(champion);
    }

    private void AddPickRulePlayer_Click(object sender, RoutedEventArgs e)
    {
        if (PickRulePlayerCandidateBox.SelectedItem is ArenaCatalogRow champion && pickRulePlayerChampions.All(item => item.BaseId != champion.BaseId)
            && pickRulePlayerChampions.Count < 20) { pickRulePlayerChampions.Add(champion); _ = LoadCatalogPortraitAsync(champion); }
    }

    private void RemovePickRulePlayer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: ArenaCatalogRow champion }) pickRulePlayerChampions.Remove(champion);
    }

    private ArenaRole CurrentPickRuleRoles()
    {
        var roles = ArenaRole.None;
        if (PickRuleRoleSpeed.IsChecked == true) roles |= ArenaRole.Initiative;
        if (PickRuleRoleOpen.IsChecked == true) roles |= ArenaRole.Opener;
        if (PickRuleRoleDps.IsChecked == true) roles |= ArenaRole.Damage;
        if (PickRuleRoleControl.IsChecked == true) roles |= ArenaRole.Control;
        if (PickRuleRoleProtect.IsChecked == true) roles |= ArenaRole.Protection;
        if (PickRuleRoleHeal.IsChecked == true) roles |= ArenaRole.Sustain;
        if (PickRuleRoleCleanse.IsChecked == true) roles |= ArenaRole.Cleanse;
        if (PickRuleRoleUtility.IsChecked == true) roles |= ArenaRole.Utility;
        return roles;
    }

    private void SetPickRuleRoles(ArenaRole roles)
    {
        PickRuleRoleSpeed.IsChecked = roles.HasFlag(ArenaRole.Initiative);
        PickRuleRoleOpen.IsChecked = roles.HasFlag(ArenaRole.Opener);
        PickRuleRoleDps.IsChecked = roles.HasFlag(ArenaRole.Damage);
        PickRuleRoleControl.IsChecked = roles.HasFlag(ArenaRole.Control);
        PickRuleRoleProtect.IsChecked = roles.HasFlag(ArenaRole.Protection);
        PickRuleRoleHeal.IsChecked = roles.HasFlag(ArenaRole.Sustain);
        PickRuleRoleCleanse.IsChecked = roles.HasFlag(ArenaRole.Cleanse);
        PickRuleRoleUtility.IsChecked = roles.HasFlag(ArenaRole.Utility);
    }

    private void ArenaBanSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (arenaBanView is null) return;
        arenaBanView.Refresh();
        ArenaBanCandidateBox.SelectedIndex = arenaBanView.IsEmpty ? -1 : 0;
        LoadVisibleBanPortraits();
    }

    private void ArenaOpenerSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (battleOpenerChampionView is null) return;
        battleOpenerChampionView.Refresh();
        ArenaOpenerChampionBox.SelectedIndex = battleOpenerChampionView.IsEmpty ? -1 : 0;
    }

    private void ArenaOpenerChampion_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ArenaOpenerChampionBox.SelectedItem is not ArenaCatalogRow champion)
        {
            ArenaOpenerAvailableList.ItemsSource = null;
            battleOpenerSteps.Clear();
            return;
        }
        ArenaOpenerAvailableList.ItemsSource = champion.Skills;
        _ = LoadCatalogPortraitAsync(champion);
        RebuildBattleOpenerSteps(champion);
        _ = LoadBattleOpenerSkillIconsAsync(champion);
    }

    private void RebuildBattleOpenerSteps(ArenaCatalogRow champion)
    {
        battleOpenerSteps.Clear();
        var configured = battleOpener.Champions.FirstOrDefault(item => item.BaseId == champion.BaseId);
        if (configured is not null)
            for (var index = 0; index < configured.SkillSlots.Count; index++)
            {
                var slot = configured.SkillSlots[index];
                var configuredTypeId = configured.SkillTypeIds?.ElementAtOrDefault(index) ?? 0;
                var skill = configuredTypeId > 0
                    ? champion.Skills.FirstOrDefault(item => item.TypeId == configuredTypeId)
                    : champion.Skills.FirstOrDefault(item => item.Slot == slot);
                var targetType = skill?.Target ?? 0;
                var requiresTarget = skill?.RequiresTarget ?? false;
                var savedPolicy = configured.TargetPolicies?.ElementAtOrDefault(index);
                var policy = requiresTarget && savedPolicy is not null
                    ? savedPolicy
                    : BattleTargetPolicies.Default(targetType, requiresTarget);
                var targetBaseId = configured.TargetBaseIds?.ElementAtOrDefault(index);
                battleOpenerSteps.Add(new(slot, skill?.TypeId ?? configuredTypeId, skill?.Name ?? $"Unavailable skill",
                    targetType, policy, targetBaseId, BattleOpenerAllyTargets(targetBaseId), skill?.Icon,
                    skill?.FormLabel ?? "Saved skill", skill?.CooldownLabel ?? "Unavailable in the current catalog",
                    skill?.TargetLabel ?? "Target unavailable", requiresTarget));
            }
        ArenaOpenerStatusText.Text = configured is null || configured.SkillSlots.Count == 0
            ? $"{champion.Name}: no sequence. Auto mode starts immediately unless another allied champion still has opening steps."
            : $"{champion.Name}: {configured.SkillSlots.Count} opening step(s), then Auto mode.";
    }

    private void AddArenaOpenerSkill_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaOpenerChampionBox.SelectedItem is not ArenaCatalogRow champion
            || ArenaOpenerAvailableList.SelectedItem is not ChampionSkillCatalogWire skill) return;
        battleOpenerSteps.Add(new(skill.Slot, skill.TypeId, skill.Name, skill.Target, BattleTargetPolicies.Default(skill.Target, skill.RequiresTarget),
            null, BattleOpenerAllyTargets(null), skill.Icon, skill.FormLabel, skill.CooldownLabel, skill.TargetLabel, skill.RequiresTarget));
        SaveBattleOpenerChampion(champion, $"Added {skill.Name} to {champion.Name}'s opening sequence.");
    }

    private async Task LoadBattleOpenerSkillIconsAsync(ArenaCatalogRow champion)
    {
        foreach (var skill in champion.Skills)
        {
            if (skill.Icon is not null) continue;
            var path = await skillIcons.GetAsync(champion.BaseId, skill.Slot, skill.Variant, lifetime.Token);
            if (path is null) continue;
            await Dispatcher.InvokeAsync(() =>
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.UriSource = new Uri(path);
                bitmap.EndInit();
                bitmap.Freeze();
                skill.Icon = bitmap;
                foreach (var step in battleOpenerSteps.Where(step => step.TypeId == skill.TypeId)) step.Icon = bitmap;
            });
        }
    }

    private void ArenaOpenerTarget_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count == 0 || e.AddedItems.Count == 0
            || ArenaOpenerChampionBox.SelectedItem is not ArenaCatalogRow champion
            || sender is not ComboBox { DataContext: BattleOpenerStepRow step, SelectedItem: string policy }) return;
        step.TargetPolicy = policy;
        SaveBattleOpenerChampion(champion, $"Updated the target for {step.Name}.");
    }

    private void ArenaOpenerSpecificAlly_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.RemovedItems.Count == 0 || e.AddedItems.Count == 0
            || ArenaOpenerChampionBox.SelectedItem is not ArenaCatalogRow champion
            || sender is not ComboBox { DataContext: BattleOpenerStepRow step, SelectedValue: int baseId }) return;
        step.TargetBaseId = baseId;
        SaveBattleOpenerChampion(champion, $"Updated the preferred ally for {step.Name}.");
    }

    private IReadOnlyList<BattleTargetChampionOption> BattleOpenerAllyTargets(int? selectedBaseId)
    {
        var options = arenaPool.Select(row => new BattleTargetChampionOption(row.BaseId, row.Name))
            .Concat(presetLineupSlots.SelectMany(slot => slot.Candidates).Select(row => new BattleTargetChampionOption(row.BaseId, row.Name)))
            .DistinctBy(row => row.BaseId)
            .ToList();
        if (selectedBaseId is > 0 && options.All(row => row.BaseId != selectedBaseId))
        {
            var selected = arenaCatalog.FirstOrDefault(row => row.BaseId == selectedBaseId);
            options.Add(new(selectedBaseId.Value, selected?.Name ?? "Unavailable champion"));
        }
        return options.OrderBy(row => row.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    private void RemoveArenaOpenerSkill_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaOpenerChampionBox.SelectedItem is not ArenaCatalogRow champion || ArenaOpenerSequenceList.SelectedIndex < 0) return;
        battleOpenerSteps.RemoveAt(ArenaOpenerSequenceList.SelectedIndex);
        SaveBattleOpenerChampion(champion, $"Updated {champion.Name}'s opening sequence.");
    }

    private void MoveArenaOpenerSkill_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaOpenerChampionBox.SelectedItem is not ArenaCatalogRow champion || ArenaOpenerSequenceList.SelectedIndex < 0
            || sender is not Button button || button.Tag is not string direction) return;
        var from = ArenaOpenerSequenceList.SelectedIndex;
        var to = direction == "up" ? from - 1 : from + 1;
        if (to < 0 || to >= battleOpenerSteps.Count) return;
        battleOpenerSteps.Move(from, to);
        ArenaOpenerSequenceList.SelectedIndex = to;
        SaveBattleOpenerChampion(champion, $"Reordered {champion.Name}'s opening sequence.");
    }

    private void ClearArenaOpener_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaOpenerChampionBox.SelectedItem is not ArenaCatalogRow champion) return;
        battleOpenerSteps.Clear();
        SaveBattleOpenerChampion(champion, $"Cleared {champion.Name}'s opening sequence. Auto mode will start immediately when no other sequence is pending.");
    }

    private void SaveBattleOpenerChampion(ArenaCatalogRow champion, string status)
    {
        var configurations = battleOpener.Champions.Where(item => item.BaseId != champion.BaseId).ToList();
        if (battleOpenerSteps.Count > 0) configurations.Add(new(
            champion.BaseId,
            battleOpenerSteps.Select(step => step.Slot).ToList(),
            battleOpenerSteps.Select(step => step.TargetPolicy).ToList(),
            battleOpenerSteps.Select(step => step.TypeId).ToList(),
            battleOpenerSteps.Select(step => step.TargetBaseId).ToList()));
        battleOpener = new(BattleOpenerFile.CurrentVersion, configurations.OrderBy(item => item.BaseId).ToList());
        battleOpener.Save();
        ArenaOpenerStatusText.Text = status + " Saved automatically.";
    }

    private void LoadVisibleBanPortraits()
    {
        if (arenaBanView is null) return;
        foreach (var row in arenaBanView.Cast<ArenaCatalogRow>().Take(24)) _ = LoadCatalogPortraitAsync(row);
    }

    private void AddArenaChampion_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaCandidateBox.SelectedItem is not ChampionRow champion) return;
        if (arenaPool.Count >= 20) { ArenaStrategyStatusText.Text = "The pool is limited to 20 champions for bounded exhaustive planning."; return; }
        if (arenaPool.Any(row => row.InstanceId == champion.Instance.Id || row.BaseId == champion.Instance.BaseId))
        {
            ArenaStrategyStatusText.Text = "That champion instance or base identity is already in the pool.";
            return;
        }
        var roles = arenaRoleCatalog.RolesFor(champion.Instance.BaseId);
        if (roles == ArenaRole.None) roles = ArenaRoleDefaults.FromMarker(champion.Instance.Marker);
        if (roles == ArenaRole.None) roles = ArenaRole.Utility;
        var leaderPriority = champion.Instance.Marker switch { 300 => 0, 301 => 1, _ => arenaPool.Count + 2 };
        var row = new ArenaPoolRow { Champion = champion, Roles = roles, Priority = arenaPool.Count, LeaderPriority = leaderPriority };
        PushArenaUndo();
        row.PropertyChanged += ArenaPoolRow_PropertyChanged;
        arenaPool.Add(row);
        var leaderIndex = Math.Min(leaderPriority, arenaLeaderPriority.Count);
        arenaLeaderPriority.Insert(leaderIndex, row);
        leaderPriorityReviewed = false;
        NormalizeArenaPriorities();
        ArenaCandidateBox.IsDropDownOpen = false;
        AutoSaveArenaStrategy($"Added {champion.Name}; strategy saved automatically.");
        UpdateArenaModeUi();
    }

    private void RemoveArenaChampion_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaPoolGrid.SelectedItem is ArenaPoolRow row)
        {
            PushArenaUndo();
            row.PropertyChanged -= ArenaPoolRow_PropertyChanged;
            arenaPool.Remove(row);
            arenaLeaderPriority.Remove(row);
            leaderPriorityReviewed = false;
            AutoSaveArenaStrategy($"Removed {row.Name}; strategy saved automatically.");
        }
        NormalizeArenaPriorities();
        UpdateArenaModeUi();
    }

    private void NormalizeArenaPriorities()
    {
        for (var index = 0; index < arenaPool.Count; index++) arenaPool[index].Priority = index;
        for (var index = 0; index < arenaLeaderPriority.Count; index++) arenaLeaderPriority[index].LeaderPriority = index;
        ArenaPoolGrid.Items.Refresh();
        ArenaDraftRosterGrid.Items.Refresh();
    }

    private void AddBanPriority_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaBanCandidateBox.SelectedItem is not ArenaCatalogRow champion) return;
        if (arenaBanPriority.All(row => row.BaseId != champion.BaseId))
        {
            PushArenaUndo();
            arenaBanPriority.Add(new(champion.BaseId, champion.Name, champion.Portrait));
            AutoSaveArenaStrategy($"Added {champion.Name} to ban priorities; strategy saved automatically.");
        }
        ArenaBanCandidateBox.IsDropDownOpen = false;
    }

    private void RemoveBanPriority_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaBanPriorityList.SelectedItem is not ArenaBanPriorityRow row) return;
        PushArenaUndo();
        arenaBanPriority.Remove(row);
        AutoSaveArenaStrategy($"Removed {row.Name} from ban priorities; strategy saved automatically.");
    }

    private void MoveArenaPriority_Click(object sender, RoutedEventArgs e)
    {
        if (arenaMode != LiveArenaAutomationMode.Off
            || sender is not Button { DataContext: var row, Tag: string direction }
            || !int.TryParse(direction, NumberStyles.Integer, CultureInfo.InvariantCulture, out var offset)) return;
        switch (row)
        {
            case ArenaBanPriorityRow ban:
                MovePriority(arenaBanPriority, ban, offset, "Reordered ban priorities; strategy saved automatically.");
                break;
            case ArenaPoolRow leader:
                MovePriority(arenaLeaderPriority, leader, offset, "Reordered leader priorities; strategy saved automatically.", true);
                break;
            case ArenaPickRuleRow rule:
                MovePriority(arenaPickRules, rule, offset, "Reordered pick rules; strategy saved automatically.");
                break;
        }
    }

    private void MovePriority<T>(ObservableCollection<T> rows, T row, int offset, string message, bool marksLeaderReviewed = false)
    {
        var from = rows.IndexOf(row);
        var to = from + offset;
        if (from < 0 || to < 0 || to >= rows.Count) return;
        PushArenaUndo();
        rows.Move(from, to);
        if (marksLeaderReviewed) leaderPriorityReviewed = true;
        AutoSaveArenaStrategy(message);
        UpdateArenaModeUi();
    }

    private void RoleBadge_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        undoArenaStrategy = CaptureArenaStrategy(false);
        ArenaUndoButton.IsEnabled = true;
        ArenaUndoButton.Visibility = Visibility.Visible;
        ArenaBoardUndoButton.IsEnabled = true;
        ArenaBoardUndoButton.Visibility = Visibility.Visible;
    }

    private void OpenArenaOpener_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        var baseId = button.DataContext switch
        {
            ArenaPoolRow row => row.BaseId,
            PresetLineupCandidateRow row => row.BaseId,
            _ => 0
        };
        if (baseId <= 0) return;
        var champion = arenaCatalog.FirstOrDefault(candidate => candidate.BaseId == baseId);
        if (champion is null)
        {
            ArenaStrategyStatusText.Text = $"RAID's skill catalog does not contain champion {baseId}.";
            return;
        }
        ArenaOpenerSearchBox.Clear();
        battleOpenerChampionView.Refresh();
        ArenaOpenerChampionBox.SelectedItem = champion;
        ArenaTabs.SelectedItem = BattleOpenerTab;
    }

    private void ArenaPoolRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ArenaPoolRow.Roles)) AutoSaveArenaStrategy("Champion roles saved automatically.");
    }

    private void ApplyArenaRolePreset_Click(object sender, RoutedEventArgs e)
    {
        if (ArenaPoolGrid.SelectedItem is not ArenaPoolRow row || ArenaRolePresetBox.SelectedItem is not string preset) return;
        PushArenaUndo();
        row.Roles = ArenaRolePresets.FromName(preset);
    }

    private void StartArenaDryRun_Click(object sender, RoutedEventArgs e) => SetArenaMode(LiveArenaAutomationMode.DryRun);
    private void StartArenaSession_Click(object sender, RoutedEventArgs e)
    {
        ArenaBattleLimitBox.Text = ArenaBoardBattleLimitBox.Text;
        SetArenaMode(LiveArenaAutomationMode.Armed, true);
    }
    private void DisarmArena_Click(object sender, RoutedEventArgs e) => SetArenaMode(LiveArenaAutomationMode.Off);

    private async void ToggleBattleDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (probe is null)
        {
            ShowError("Connect RAID before recording a manual battle reference.");
            return;
        }
        BattleDiagnosticButton.IsEnabled = false;
        try
        {
            if (battleDiagnostics.ManualReferenceActive)
            {
                await FinishManualBattleDiagnosticsAsync("stopped-by-user");
                return;
            }
            if (mythicalClickTrace.IsRecording)
                throw new InvalidOperationException("Stop the Mythical click-path trace before recording a manual battle reference.");
            SetArenaMode(LiveArenaAutomationMode.Off);
            battleDiagnostics.BeginManualReference();
            await probe.StartBattleDiagnosticsAsync();
            BattleDiagnosticButton.Content = "Stop manual trace";
            BattleDiagnosticStatusText.Text = "Manual reference armed. Play one Live Arena battle yourself and click the desired skill normally; mythical form and HUD transaction changes are being recorded.";
        }
        catch (Exception exception)
        {
            battleDiagnostics.EndManualReference("start-failed");
            BattleDiagnosticStatusText.Text = exception.Message;
        }
        finally
        {
            BattleDiagnosticButton.IsEnabled = true;
            UpdateDiagnosticIndicator();
        }
    }

    private async void ToggleRewardDiagnostics_Click(object sender, RoutedEventArgs e)
    {
        if (probe is null || connectedRaidProcessId <= 0)
        {
            ShowError("Connect RAID before recording a reward trace.");
            return;
        }
        RewardDiagnosticButton.IsEnabled = false;
        try
        {
            if (rewardDiagnostics.IsRecording)
            {
                await probe.StopRewardDiagnosticsAsync();
                rewardDiagnostics.Stop("stopped-by-user");
                RewardDiagnosticButton.Content = "Record reward trace";
                ArenaStrategyStatusText.Text = $"Reward trace saved: {System.IO.Path.GetFileName(rewardDiagnostics.FilePath)}";
                return;
            }
            var path = rewardDiagnostics.Start(connectedRaidProcessId);
            await probe.StartRewardDiagnosticsAsync();
            RewardDiagnosticButton.Content = "Stop reward trace";
            ArenaStrategyStatusText.Text = $"Reward trace active: {System.IO.Path.GetFileName(path)}. Claim rewards manually, then stop the trace.";
        }
        catch (Exception exception)
        {
            rewardDiagnostics.Stop("start-or-stop-failed");
            RewardDiagnosticButton.Content = "Record reward trace";
            ShowError(exception.Message);
        }
        finally
        {
            RewardDiagnosticButton.IsEnabled = true;
            UpdateDiagnosticIndicator();
        }
    }

    private async void ToggleMythicalClickTrace_Click(object sender, RoutedEventArgs e)
    {
        if (probe is null || connectedRaidProcessId <= 0)
        {
            ShowError("Connect RAID before recording a Mythical click path.");
            return;
        }
        if (arenaMode == LiveArenaAutomationMode.Armed || continuousArenaSession)
        {
            ShowError("Stop Live Arena automation before recording a Mythical click path.");
            return;
        }
        MythicalClickTraceButton.IsEnabled = false;
        try
        {
            if (mythicalClickTrace.IsRecording)
            {
                await probe.StopMythicalClickTraceAsync();
                mythicalClickTrace.Stop("stopped-by-user");
                MythicalClickTraceButton.Content = "Record Mythical click path";
                BattleDiagnosticStatusText.Text = $"Mythical click path saved: {Path.GetFileName(mythicalClickTrace.FilePath)}";
                return;
            }
            if (battleDiagnostics.ManualReferenceActive)
                throw new InvalidOperationException("Stop the manual battle trace before recording a Mythical click path.");
            var path = mythicalClickTrace.Start(connectedRaidProcessId);
            await probe.StartMythicalClickTraceAsync();
            MythicalClickTraceButton.Content = "Stop Mythical click path";
            BattleDiagnosticStatusText.Text = $"Mythical click path active for 15 seconds: {Path.GetFileName(path)}. Manually click Metamorph now.";
        }
        catch (Exception exception)
        {
            mythicalClickTrace.Stop("start-or-stop-failed");
            MythicalClickTraceButton.Content = "Record Mythical click path";
            ShowError(exception.Message);
        }
        finally
        {
            MythicalClickTraceButton.IsEnabled = true;
            UpdateDiagnosticIndicator();
        }
    }

    private void ArmDiagnosticClickPath_Click(object sender, RoutedEventArgs e)
    {
        if (probe is null)
        {
            ShowError("Connect RAID before arming the diagnostic skill click path.");
            return;
        }
        if (arenaMode == LiveArenaAutomationMode.Armed)
        {
            ShowError("Disarm Live Arena before arming the diagnostic skill click path.");
            return;
        }
        if (mythicalClickTrace.IsRecording)
        {
            ShowError("Stop the Mythical click-path trace before arming the diagnostic skill click path.");
            return;
        }
        diagnosticClickPathArmed = true;
        BattleDiagnosticClickButton.IsEnabled = false;
        BattleDiagnosticStatusText.Text = "Diagnostic click path armed for the next configured skill. It will call the visible SkillContext click handler once and then disarm itself.";
    }

    private void OnRewardTrace(string payload)
    {
        try { rewardDiagnostics.Record(payload); }
        catch (Exception exception)
        {
            rewardDiagnostics.Stop("write-failed");
            RewardDiagnosticButton.Content = "Record reward trace";
            Log.Error("Reward diagnostic write failed.", exception);
            ArenaStrategyStatusText.Text = $"Reward trace stopped: {exception.Message}";
            UpdateDiagnosticIndicator();
        }
    }

    private void OnMythicalClickTrace(string payload)
    {
        try { mythicalClickTrace.Record(payload); }
        catch (Exception exception)
        {
            mythicalClickTrace.Stop("write-failed");
            MythicalClickTraceButton.Content = "Record Mythical click path";
            Log.Error("Mythical click-path diagnostic write failed.", exception);
            BattleDiagnosticStatusText.Text = $"Mythical click path stopped: {exception.Message}";
            UpdateDiagnosticIndicator();
        }
    }

    private async Task FinishManualBattleDiagnosticsAsync(string reason)
    {
        if (!battleDiagnostics.ManualReferenceActive) return;
        try
        {
            if (probe is not null) await probe.StopBattleDiagnosticsAsync();
        }
        catch (Exception exception) { Log.Error("Failed to stop high-frequency battle diagnostics.", exception); }
        battleDiagnostics.EndManualReference(reason);
        BattleDiagnosticButton.Content = "Record manual trace";
        BattleDiagnosticStatusText.Text = $"Manual reference saved: {System.IO.Path.GetFileName(battleDiagnostics.FilePath)}";
        UpdateDiagnosticIndicator();
    }

    private void ArenaAutoRefill_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsInitialized) return;
        if (ReferenceEquals(sender, ArenaBoardAutoRefillCheckBox))
            ArenaAutoRefillCheckBox.IsChecked = ArenaBoardAutoRefillCheckBox.IsChecked;
        else
            ArenaBoardAutoRefillCheckBox.IsChecked = ArenaAutoRefillCheckBox.IsChecked;
        ArenaBoardAutoRefillWarningText.Visibility = ArenaBoardAutoRefillCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        UpdateArenaBoardSummary(probe is not null, arenaMode != LiveArenaAutomationMode.Off, arenaDraftMode == ArenaDraftMode.AdaptiveDraft
            ? arenaPool.Count >= 5
            : presetLineupSlots.Count == 5 && presetLineupSlots.All(slot => slot.HasPrimary));
        if (!continuousArenaSession || lastLiveArena is null) return;
        ArenaStrategyStatusText.Text = ArenaAutoRefillCheckBox.IsChecked == true
            ? "Auto-refill enabled for visible Live Arena token prompts. Account resources may be spent."
            : "Auto-refill disabled. The session will wait if Live Arena tokens run out.";
        _ = HandleContinuousArenaSessionAsync(lastLiveArena);
    }

    private void SetArenaMode(LiveArenaAutomationMode mode, bool continuous = false)
    {
        var wasContinuous = continuousArenaSession;
        try
        {
            if (mode != LiveArenaAutomationMode.Off)
            {
                if (mythicalClickTrace.IsRecording)
                    throw new InvalidOperationException("Stop the Mythical click-path trace before starting Live Arena automation.");
                arenaStrategy = CaptureArenaStrategy(true);
                arenaStrategy.Save();
                battleOpener.Validate();
                battleOpener.Save();
                if (probe is null) throw new InvalidOperationException("Connect RAID before starting Live Arena strategy.");
            }
            if (mode == LiveArenaAutomationMode.Armed && continuous)
            {
                if (!int.TryParse(ArenaBattleLimitBox.Text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out arenaSessionBattleLimit)
                    || arenaSessionBattleLimit is < 1 or > 999)
                    throw new InvalidDataException("Battle limit must be a whole number between 1 and 999.");
                arenaSessionStartedAt = DateTime.UtcNow;
                arenaSessionEndedAt = null;
                arenaSessionBattlesCompleted = 0;
                arenaSessionWins = 0;
                arenaSessionLosses = 0;
                arenaSessionUnknownResults = 0;
                arenaSessionRewardsClaimed = 0;
                arenaSessionRefills = 0;
                arenaSessionGemsSpent = 0;
                arenaSessionBattleCounted = false;
                arenaSessionLimitReached = false;
                arenaDashboardRunFinalized = false;
                deferredLiveArenaReturnAttempts = 0;
                deferredLiveArenaReturnRetryPending = false;
            }
            else if (mode == LiveArenaAutomationMode.Off && wasContinuous)
                arenaSessionEndedAt = DateTime.UtcNow;
            arenaMode = mode;
            continuousArenaSession = mode == LiveArenaAutomationMode.Armed && continuous;
            lastArenaDecisionKey = null;
            pendingArenaDecision = null;
            pendingArenaSessionDecision = null;
            deferredLiveArenaReturnAttempts = 0;
            deferredLiveArenaReturnRetryPending = false;
            rewardBatchInProgress = false;
            rewardClaimBaseline = 0;
            pendingBattleOpenerDecision = null;
            pendingBattleOpenerSnapshot = null;
            pendingBattleActionId = 0;
            battleAutoRecoveryRequired = false;
            battleAutoRetryCount = 0;
            ResetBattleOpenerGuards();
            battleSkillStabilizationPending = false;
            battleOpenerInitialized = false;
            battleOpenerProgress.Clear();
            ArenaStrategyStatusText.Text = mode switch
            {
                LiveArenaAutomationMode.DryRun => "Dry Run active. Decisions will be explained and logged without clicking or confirming anything.",
                LiveArenaAutomationMode.Armed => "Continuous Live Arena session armed. Open the Battle tab in RAID; the next match starts only when that screen is verified.",
                _ => "Live Arena strategy is disarmed."
            };
        }
        catch (Exception exception)
        {
            arenaMode = LiveArenaAutomationMode.Off;
            continuousArenaSession = false;
            if (wasContinuous) arenaSessionEndedAt = DateTime.UtcNow;
            ArenaStrategyStatusText.Text = exception.Message;
        }
        UpdateArenaModeUi();
        if (continuousArenaSession && lastLiveArena is not null) _ = HandleContinuousArenaSessionAsync(lastLiveArena);
    }

    private ArenaStrategyFile CaptureArenaStrategy(bool requireReady)
    {
        ArenaPoolGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        ArenaPoolGrid.CommitEdit(DataGridEditingUnit.Row, true);
        NormalizeArenaPriorities();
        NormalizePresetLineup();
        if (requireReady)
        {
            var missing = arenaPickRules.Select(row => row.ToRule()).Where(rule => rule.Enabled)
                .FirstOrDefault(rule => teamCandidates.All(champion => champion.Instance.Id != rule.Replacement.InstanceId
                    || champion.Instance.BaseId != rule.Replacement.BaseId));
            if (missing is not null)
                throw new InvalidDataException($"Pick rule {missing.Name} uses an owned champion that is no longer available.");
        }
        var preset = presetLineupSlots.Select(slot => new ArenaPresetSlot(slot.Candidates.Select(row => row.ToCandidate()).ToList())).ToList();
        var result = new ArenaStrategyFile(ArenaStrategyFile.CurrentVersion, arenaPool.Select(row => row.ToCandidate()).ToList(), CurrentBanPriority(),
            arenaDraftMode, preset, CurrentLeaderPriority(), arenaPickRules.Select(row => row.ToRule()).ToList())
        {
            LeaderPriorityReviewed = leaderPriorityReviewed
        };
        result.Validate(requireReady);
        return result;
    }

    private List<int> CurrentBanPriority() => arenaBanPriority.Select(row => row.BaseId).ToList();
    private List<int> CurrentLeaderPriority() => arenaLeaderPriority.Select(row => row.BaseId).ToList();

    private void PushArenaUndo()
    {
        try
        {
            undoArenaStrategy = CaptureArenaStrategy(false);
            ArenaUndoButton.IsEnabled = true;
            ArenaUndoButton.Visibility = Visibility.Visible;
            ArenaBoardUndoButton.IsEnabled = true;
            ArenaBoardUndoButton.Visibility = Visibility.Visible;
        }
        catch { }
    }

    private void AutoSaveArenaStrategy(string message)
    {
        try
        {
            ArenaAutosaveText.Text = "Savingâ€¦";
            arenaStrategy = CaptureArenaStrategy(false);
            arenaStrategy.Save();
            hudDecisionKey = null;
            hudDecision = null;
            ArenaAutosaveText.Text = "Saved";
            ArenaStrategyStatusText.Text = message;
            ArenaBoardStatusText.Text = "Strategy saved Â· Automation off";
        }
        catch (Exception exception)
        {
            ArenaAutosaveText.Text = "Save failed";
            ArenaStrategyStatusText.Text = exception.Message;
            ArenaBoardStatusText.Text = $"Save failed Â· {exception.Message}";
        }
        UpdateArenaPriorityCounts();
    }

    private void UndoArenaStrategy_Click(object sender, RoutedEventArgs e)
    {
        if (undoArenaStrategy is null) return;
        arenaStrategy = undoArenaStrategy;
        undoArenaStrategy = null;
        arenaDraftMode = arenaStrategy.DraftMode;
        leaderPriorityReviewed = arenaStrategy.LeaderPriorityReviewed;
        RebuildArenaPool();
        RebuildPresetLineup();
        RebuildPickRules();
        RebuildBanPriorities();
        RebuildLeaderPriorities(true);
        arenaStrategy.Save();
        ArenaUndoButton.IsEnabled = false;
        ArenaUndoButton.Visibility = Visibility.Collapsed;
        ArenaBoardUndoButton.IsEnabled = false;
        ArenaBoardUndoButton.Visibility = Visibility.Collapsed;
        ArenaAutosaveText.Text = "Saved";
        ArenaStrategyStatusText.Text = "Last strategy change undone and saved automatically.";
        UpdateDraftModeUi();
        UpdateArenaModeUi();
    }

    private void RebuildBanPriorities()
    {
        arenaBanPriority.Clear();
        foreach (var baseId in arenaStrategy.BanPriority)
        {
            var champion = arenaCatalog.FirstOrDefault(row => row.BaseId == baseId);
            arenaBanPriority.Add(new(baseId, champion?.Name ?? "Unavailable champion", champion?.Portrait));
        }
    }

    private void ArenaPool_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsButtonSource(e.OriginalSource))
        {
            draggedArenaPool = null;
            return;
        }
        dragStart = e.GetPosition(this);
        draggedArenaPool = sender is ItemsControl source ? ItemAt<ArenaPoolRow>(source, e.OriginalSource) : null;
    }

    private void ArenaPool_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is ItemsControl source) BeginDrag(source, draggedArenaPool, e);
    }

    private void ArenaPool_Drop(object sender, DragEventArgs e)
    {
        NormalizeArenaPriorities();
    }

    private void ArenaLeader_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this);
        draggedArenaLeader = sender is ItemsControl source ? ItemAt<ArenaPoolRow>(source, e.OriginalSource) : null;
    }

    private void ArenaLeader_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is ItemsControl source) BeginDrag(source, draggedArenaLeader, e);
    }

    private void ArenaLeader_Drop(object sender, DragEventArgs e)
    {
        leaderPriorityReviewed = true;
        NormalizeArenaPriorities();
    }

    private void ArenaBan_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        dragStart = e.GetPosition(this);
        draggedArenaBan = sender is ItemsControl source ? ItemAt<ArenaBanPriorityRow>(source, e.OriginalSource) : null;
    }

    private void ArenaBan_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is ItemsControl source) BeginDrag(source, draggedArenaBan, e);
    }

    private void ArenaBan_Drop(object sender, DragEventArgs e) { }

    private void PresetCandidate_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox list || IsButtonSource(e.OriginalSource)) { draggedPresetCandidate = null; return; }
        dragStart = e.GetPosition(this);
        draggedPresetCandidate = ItemAt<PresetLineupCandidateRow>(list, e.OriginalSource);
    }

    private void PresetCandidate_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is ListBox list) BeginDrag(list, draggedPresetCandidate, e);
    }

    private void PresetCandidate_Drop(object sender, DragEventArgs e) => NormalizePresetLineup();

    private void PickRule_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (IsButtonSource(e.OriginalSource)) { draggedPickRule = null; return; }
        dragStart = e.GetPosition(this);
        draggedPickRule = sender is ItemsControl source ? ItemAt<ArenaPickRuleRow>(source, e.OriginalSource) : null;
    }

    private void PickRule_MouseMove(object sender, MouseEventArgs e)
    {
        if (sender is ItemsControl source) BeginDrag(source, draggedPickRule, e);
    }

    private void PickRule_Drop(object sender, DragEventArgs e) { }

    private void OrderedList_DragOver(object sender, DragEventArgs e)
    {
        var accepted = false;
        if (sender == ArenaPoolGrid && e.Data.GetData(typeof(ArenaPoolRow)) is ArenaPoolRow poolRow)
        {
            MoveAtPointer(ArenaPoolGrid, arenaPool, poolRow, e);
            accepted = true;
        }
        else if (sender == ArenaDraftRosterGrid && e.Data.GetData(typeof(ArenaPoolRow)) is ArenaPoolRow rosterRow)
        {
            MoveAtPointer(ArenaDraftRosterGrid, arenaPool, rosterRow, e);
            accepted = true;
        }
        else if (sender == ArenaLeaderPriorityList && e.Data.GetData(typeof(ArenaPoolRow)) is ArenaPoolRow leaderRow)
        {
            MoveAtPointer(ArenaLeaderPriorityList, arenaLeaderPriority, leaderRow, e);
            accepted = true;
        }
        else if (sender == ArenaBoardLeaderList && e.Data.GetData(typeof(ArenaPoolRow)) is ArenaPoolRow boardLeaderRow)
        {
            MoveAtPointer(ArenaBoardLeaderList, arenaLeaderPriority, boardLeaderRow, e);
            accepted = true;
        }
        else if (sender == ArenaBanPriorityList && e.Data.GetData(typeof(ArenaBanPriorityRow)) is ArenaBanPriorityRow banRow)
        {
            MoveAtPointer(ArenaBanPriorityList, arenaBanPriority, banRow, e);
            accepted = true;
        }
        else if (sender == ArenaBoardBanList && e.Data.GetData(typeof(ArenaBanPriorityRow)) is ArenaBanPriorityRow boardBanRow)
        {
            MoveAtPointer(ArenaBoardBanList, arenaBanPriority, boardBanRow, e);
            accepted = true;
        }
        else if (sender == PickRulesList && e.Data.GetData(typeof(ArenaPickRuleRow)) is ArenaPickRuleRow ruleRow)
        {
            MoveAtPointer(PickRulesList, arenaPickRules, ruleRow, e);
            accepted = true;
        }
        else if (sender == ArenaBoardPickRulesList && e.Data.GetData(typeof(ArenaPickRuleRow)) is ArenaPickRuleRow boardRuleRow)
        {
            MoveAtPointer(ArenaBoardPickRulesList, arenaPickRules, boardRuleRow, e);
            accepted = true;
        }
        else if (sender is ListBox { DataContext: PresetLineupSlotRow slot } presetList
            && e.Data.GetData(typeof(PresetLineupCandidateRow)) is PresetLineupCandidateRow presetRow
            && slot.Candidates.Contains(presetRow))
        {
            MoveAtPointer(presetList, slot.Candidates, presetRow, e, true);
            NormalizePresetLineup();
            accepted = true;
        }
        e.Effects = accepted ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void BeginDrag(ItemsControl source, object? item, MouseEventArgs e)
    {
        if (dragInProgress || item is null || e.LeftButton != MouseButtonState.Pressed) return;
        var position = e.GetPosition(this);
        if (Math.Abs(position.X - dragStart.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(position.Y - dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;
        PushArenaUndo();
        dragSurface = Content as UIElement;
        draggedContainer = source.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement;
        dragLayer = dragSurface is null ? null : AdornerLayer.GetAdornerLayer(dragSurface);
        if (dragLayer is not null && dragSurface is not null)
        {
            dragPreview = new DragPreviewAdorner(dragSurface, item);
            dragPreview.SetPosition(Mouse.GetPosition(dragSurface));
            dragLayer.Add(dragPreview);
        }
        if (draggedContainer is not null) draggedContainer.Opacity = 0.18;
        dragInProgress = true;
        source.GiveFeedback += DragSource_GiveFeedback;
        try { DragDrop.DoDragDrop(source, item, DragDropEffects.Move); }
        finally
        {
            source.GiveFeedback -= DragSource_GiveFeedback;
            if (draggedContainer is not null) draggedContainer.Opacity = 1;
            if (dragLayer is not null && dragPreview is not null) dragLayer.Remove(dragPreview);
            dragPreview = null;
            dragLayer = null;
            dragSurface = null;
            draggedContainer = null;
            draggedArenaPool = null;
            draggedArenaLeader = null;
            draggedArenaBan = null;
            draggedPresetCandidate = null;
            draggedPickRule = null;
            dragInProgress = false;
            AutoSaveArenaStrategy("Priority order saved automatically.");
        }
    }

    private void DragSource_GiveFeedback(object sender, GiveFeedbackEventArgs e)
    {
        if (dragPreview is not null && dragSurface is not null) dragPreview.SetPosition(Mouse.GetPosition(dragSurface));
        e.UseDefaultCursors = true;
        e.Handled = true;
    }

    private static T? ItemAt<T>(ItemsControl control, object source) where T : class
    {
        var container = source is DependencyObject dependency ? ItemsControl.ContainerFromElement(control, dependency) : null;
        return container is null ? null : control.ItemContainerGenerator.ItemFromContainer(container) as T;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match) return match;
            if (FindVisualChild<T>(child) is T descendant) return descendant;
        }
        return null;
    }

    private static bool IsButtonSource(object source)
    {
        for (var current = source as DependencyObject; current is not null; current = VisualTreeHelper.GetParent(current))
            if (current is ButtonBase) return true;
        return false;
    }

    private static void MoveAtPointer<T>(ItemsControl control, ObservableCollection<T> items, T source, DragEventArgs e, bool horizontal = false) where T : class
    {
        var sourceIndex = items.IndexOf(source);
        if (sourceIndex < 0) return;
        var target = ItemAt<T>(control, e.OriginalSource);
        var targetIndex = target is null ? items.Count : items.IndexOf(target);
        if (targetIndex < 0) return;
        if (target is not null && control.ItemContainerGenerator.ContainerFromItem(target) is FrameworkElement targetContainer
            && (horizontal ? e.GetPosition(targetContainer).X > targetContainer.ActualWidth / 2 : e.GetPosition(targetContainer).Y > targetContainer.ActualHeight / 2)) targetIndex++;
        if (sourceIndex < targetIndex) targetIndex--;
        targetIndex = Math.Clamp(targetIndex, 0, items.Count - 1);
        if (sourceIndex == targetIndex) return;

        var positions = items.Select(item => (item, container: control.ItemContainerGenerator.ContainerFromItem(item) as FrameworkElement))
            .Where(value => value.container is not null)
            .ToDictionary(value => value.item, value => horizontal
                ? value.container!.TranslatePoint(new Point(), control).X
                : value.container!.TranslatePoint(new Point(), control).Y);
        items.Move(sourceIndex, targetIndex);
        control.UpdateLayout();
        foreach (var (item, oldY) in positions)
        {
            if (control.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement container) continue;
            var current = horizontal ? container.TranslatePoint(new Point(), control).X : container.TranslatePoint(new Point(), control).Y;
            var delta = oldY - current;
            if (Math.Abs(delta) < 0.5) continue;
            var transform = new TranslateTransform();
            container.RenderTransform = transform;
            transform.BeginAnimation(horizontal ? TranslateTransform.XProperty : TranslateTransform.YProperty, new DoubleAnimation(delta, 0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }
    }

    private enum DraftRevealKind { Pick, Ban, Leader }

    private sealed record DraftReveal(LiveArenaHeroWire Hero, DraftRevealKind Kind, bool PlayerTeam);

    private sealed class DragPreviewAdorner : Adorner
    {
        private readonly string name;
        private readonly ImageSource? portrait;
        private Point position;

        public DragPreviewAdorner(UIElement adornedElement, object item) : base(adornedElement)
        {
            (name, portrait) = item switch
            {
                ArenaPoolRow row => (row.Name, row.Portrait),
                ArenaBanPriorityRow row => (row.Name, row.Portrait),
                PresetLineupCandidateRow row => (row.Name, row.Portrait),
                ArenaPickRuleRow row => (row.Name, row.Portrait),
                _ => ("Champion", null)
            };
            IsHitTestVisible = false;
        }

        public void SetPosition(Point value)
        {
            position = value;
            InvalidateVisual();
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            const double width = 310;
            const double height = 56;
            var card = new Rect(position.X + 16, position.Y + 14, width, height);
            drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(90, 0, 0, 0)), null,
                new Rect(card.X + 4, card.Y + 5, card.Width, card.Height), 7, 7);
            drawingContext.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(245, 23, 33, 41)),
                new Pen(new SolidColorBrush(Color.FromRgb(79, 179, 168)), 1.5), card, 7, 7);
            if (portrait is not null) drawingContext.DrawImage(portrait, new Rect(card.X + 7, card.Y + 7, 42, 42));
            var text = new FormattedText(name, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Bahnschrift SemiCondensed"), 16, new SolidColorBrush(Color.FromRgb(240, 235, 221)), VisualTreeHelper.GetDpi(this).PixelsPerDip)
            { MaxTextWidth = width - 68, Trimming = TextTrimming.CharacterEllipsis };
            drawingContext.DrawText(text, new Point(card.X + 59, card.Y + 18));
        }
    }

    private void UpdateArenaModeUi()
    {
        if (!IsInitialized) return;
        if (!continuousArenaSession && arenaSessionStartedAt is not null && arenaSessionEndedAt is null)
            arenaSessionEndedAt = DateTime.UtcNow;
        if (!continuousArenaSession) FinalizeArenaDashboardRun();
        UpdateDraftModeUi();
        ApplyLiveArenaLayout();
        var connected = probe is not null;
        var automationActive = arenaMode != LiveArenaAutomationMode.Off;
        var adaptive = arenaDraftMode == ArenaDraftMode.AdaptiveDraft;
        var pendingAutomation = pendingArenaDecision is not null || pendingArenaSessionDecision is not null || pendingBattleOpenerDecision is not null;
        ArenaDisconnectedPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        ArenaRulesPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ArenaRulesText.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ArenaStrip.Visibility = connected && automationActive ? Visibility.Visible : Visibility.Collapsed;
        ArenaSessionPanel.Visibility = connected && automationActive ? Visibility.Visible : Visibility.Collapsed;
        ArenaStrategyStatusPanel.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ArenaStrategyStatusText.Visibility = Visibility.Visible;
        ArenaReadinessText.Visibility = connected ? Visibility.Visible : Visibility.Collapsed;
        ArenaOverrideButton.Visibility = connected && (automationActive || pendingAutomation) ? Visibility.Visible : Visibility.Collapsed;
        ArenaDisarmButton.Visibility = connected && arenaMode == LiveArenaAutomationMode.Armed ? Visibility.Visible : Visibility.Collapsed;
        ArenaModeText.Text = continuousArenaSession ? "Running" : arenaMode switch
        {
            LiveArenaAutomationMode.DryRun => "Dry run",
            LiveArenaAutomationMode.Armed => "Armed",
            _ => "Inactive"
        };
        var draftReady = arenaDraftMode == ArenaDraftMode.AdaptiveDraft
            ? arenaPool.Count >= 5
            : presetLineupSlots.Count == 5 && presetLineupSlots.All(slot => slot.Candidates.Count > 0);
        ArenaDryRunButton.IsEnabled = arenaMode == LiveArenaAutomationMode.Off && connected && draftReady;
        ArenaRunButton.IsEnabled = arenaMode == LiveArenaAutomationMode.Off && connected && draftReady;
        ArenaDisarmButton.IsEnabled = arenaMode == LiveArenaAutomationMode.Armed;
        ArenaBattleLimitBox.IsEnabled = arenaMode == LiveArenaAutomationMode.Off;
        ArenaAutoRefillCheckBox.IsEnabled = connected;
        ArenaOverrideButton.IsEnabled = automationActive || pendingAutomation;
        ArenaUndoButton.Visibility = undoArenaStrategy is not null && arenaMode == LiveArenaAutomationMode.Off ? Visibility.Visible : Visibility.Collapsed;
        ArenaReadinessText.Foreground = !connected || !draftReady
            ? (Brush)FindResource("WarningBrush")
            : (Brush)FindResource("CyanBrush");
        ArenaReadinessText.Text = !connected
            ? "Connect RAID to start a verified session. Strategy editing remains available."
            : automationActive
                ? "Automation is active. Stop or disarm before changing the strategy."
                : draftReady
                    ? "Strategy ready. Run Live Arena is the primary session action; Start Dry Run is available for verification."
                    : arenaDraftMode == ArenaDraftMode.AdaptiveDraft
                        ? $"Add {Math.Max(0, 5 - arenaPool.Count)} more champion{(((5 - arenaPool.Count) == 1) ? string.Empty : "s")} to the Adaptive Draft pool before starting."
                        : $"Choose a primary champion for {presetLineupSlots.Count(slot => slot.Candidates.Count == 0)} remaining Preset Lineup slot{((presetLineupSlots.Count(slot => slot.Candidates.Count == 0) == 1) ? string.Empty : "s")} before starting.";
        ArenaBoardReadinessText.Text = ArenaReadinessText.Text;
        ArenaBoardReadinessText.Foreground = ArenaReadinessText.Foreground;
        ArenaBoardDisconnectedPanel.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        ArenaBoardEditorPanel.Visibility = Visibility.Collapsed;
        ArenaBoardLiveSessionPanel.Visibility = connected && automationActive ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardContextStatus.Visibility = Visibility.Collapsed;
        ArenaBoardReadinessText.Visibility = Visibility.Visible;
        ArenaBoardRunButton.Visibility = arenaMode == LiveArenaAutomationMode.Off ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardDryRunButton.Visibility = arenaMode == LiveArenaAutomationMode.Off ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardOverrideButton.Visibility = connected && (automationActive || pendingAutomation) ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardDisarmButton.Visibility = connected && arenaMode == LiveArenaAutomationMode.Armed ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardHeaderRunButton.Visibility = Visibility.Collapsed;
        ArenaBoardHeaderDryRunButton.Visibility = Visibility.Collapsed;
        ArenaBoardHeaderOverrideButton.Visibility = connected && (automationActive || pendingAutomation) ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardRunButton.IsEnabled = ArenaRunButton.IsEnabled;
        ArenaBoardDryRunButton.IsEnabled = ArenaDryRunButton.IsEnabled;
        ArenaBoardOverrideButton.IsEnabled = ArenaOverrideButton.IsEnabled;
        ArenaBoardDisarmButton.IsEnabled = ArenaDisarmButton.IsEnabled;
        ArenaBoardHeaderRunButton.IsEnabled = ArenaRunButton.IsEnabled;
        ArenaBoardHeaderDryRunButton.IsEnabled = ArenaDryRunButton.IsEnabled;
        ArenaBoardHeaderOverrideButton.IsEnabled = ArenaOverrideButton.IsEnabled;
        ArenaBoardBattleLimitBox.Text = ArenaBattleLimitBox.Text;
        ArenaBoardBattleLimitBox.IsEnabled = ArenaBattleLimitBox.IsEnabled;
        ArenaBoardAutoRefillCheckBox.IsChecked = ArenaAutoRefillCheckBox.IsChecked;
        ArenaBoardAutoRefillCheckBox.IsEnabled = ArenaAutoRefillCheckBox.IsEnabled;
        ArenaBoardAutoRefillWarningText.Visibility = ArenaBoardAutoRefillCheckBox.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardUndoButton.Visibility = undoArenaStrategy is not null && arenaMode == LiveArenaAutomationMode.Off ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardUndoButton.IsEnabled = undoArenaStrategy is not null && arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardHeaderSessionStateText.Text = !connected ? "OFFLINE" : continuousArenaSession ? "RUNNING" : arenaMode switch
        {
            LiveArenaAutomationMode.DryRun => "DRY RUN",
            LiveArenaAutomationMode.Armed => "ARMED",
            _ => "IDLE"
        };
        ArenaBoardPresetCount.Text = $"{presetLineupSlots.Count(slot => slot.HasPrimary)} / 5 CONFIGURED";
        ArenaDraftRosterEmptyState.Visibility = adaptive && arenaPool.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardPresetList.IsHitTestVisible = !adaptive && arenaMode == LiveArenaAutomationMode.Off;
        ArenaDraftRosterGrid.IsHitTestVisible = adaptive && arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardBanList.IsHitTestVisible = arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardLeaderList.IsHitTestVisible = arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardPickRulesList.IsHitTestVisible = !adaptive && arenaMode == LiveArenaAutomationMode.Off;
        UpdateArenaPriorityCounts();
        var readinessText = BuildArenaReadinessText(connected, automationActive, draftReady);
        ArenaReadinessText.Text = readinessText;
        ArenaBoardReadinessText.Text = readinessText;
        UpdateArenaBoardSummary(connected, automationActive, draftReady);
        UpdateArenaBoardStatus(connected, automationActive);
        UpdateArenaStepUi();
        UpdateArenaSessionDashboard();
    }

    private string BuildArenaReadinessText(bool connected, bool automationActive, bool draftReady)
    {
        if (!connected) return "Connect RAID to run a verified session Â· Strategy editing is available.";
        if (automationActive) return continuousArenaSession ? "Session running Â· Stop automation to change the strategy." : "Dry run active Â· Stop automation to change the strategy.";
        if (!draftReady)
        {
            return arenaDraftMode == ArenaDraftMode.AdaptiveDraft
                ? $"Not ready Â· Add {Math.Max(0, 5 - arenaPool.Count)} more champion{(arenaPool.Count == 4 ? string.Empty : "s")} to the Adaptive Draft pool."
                : $"Not ready Â· Choose a primary for {presetLineupSlots.Count(slot => !slot.HasPrimary)} remaining lane{(presetLineupSlots.Count(slot => !slot.HasPrimary) == 1 ? string.Empty : "s")}.";
        }
        var championCount = arenaDraftMode == ArenaDraftMode.AdaptiveDraft
            ? arenaPool.Count
            : presetLineupSlots.Count(slot => slot.HasPrimary);
        var preferredLeader = arenaLeaderPriority.FirstOrDefault()?.Name ?? "No leader";
        if (!leaderPriorityReviewed)
            return $"Executable Â· {championCount} champions Â· Leader order needs review Â· {preferredLeader} generated preferred leader";
        return $"Ready to run Â· {championCount} champions Â· {arenaBanPriority.Count} ban{(arenaBanPriority.Count == 1 ? string.Empty : "s")} Â· {preferredLeader} preferred leader";
    }

    private void UpdateArenaBoardSummary(bool connected, bool automationActive, bool draftReady)
    {
        var configuredSlots = presetLineupSlots.Count(slot => slot.HasPrimary);
        var missingSlots = 5 - configuredSlots;
        var activeRules = arenaPickRules.Count(rule => rule.Enabled);
        var firstBan = arenaBanPriority.FirstOrDefault()?.Name;
        var preferredLeader = arenaLeaderPriority.FirstOrDefault()?.Name;
        ArenaBoardSummaryLineupText.Text = arenaDraftMode == ArenaDraftMode.AdaptiveDraft
            ? $"{arenaPool.Count} champion{(arenaPool.Count == 1 ? string.Empty : "s")} Â· {(draftReady ? "READY" : "INCOMPLETE")}"
            : $"{configuredSlots} / 5 Â· {(draftReady ? "READY" : "INCOMPLETE")}";
        ArenaBoardSummaryLineupDetail.Text = arenaDraftMode == ArenaDraftMode.AdaptiveDraft
            ? (draftReady ? "Adaptive pool meets the minimum" : $"Add {Math.Max(0, 5 - arenaPool.Count)} more champion{(arenaPool.Count == 4 ? string.Empty : "s")}")
            : (draftReady ? "All primary champions configured" : $"Choose a primary for {missingSlots} lane{(missingSlots == 1 ? string.Empty : "s")}");
        ArenaBoardSummaryBanText.Text = arenaBanPriority.Count == 0 ? "OPTIONAL Â· NO TARGETS" : $"{arenaBanPriority.Count} CONFIGURED Â· READY";
        ArenaBoardSummaryBanDetail.Text = firstBan is null ? "First ban: not configured" : $"First ban: {firstBan}{(arenaBanPriority.Count > 1 ? $" Â· +{arenaBanPriority.Count - 1} more" : string.Empty)}";
        ArenaBoardSummaryRulesText.Text = arenaPickRules.Count == 0
            ? "OPTIONAL Â· NO OVERRIDES"
            : activeRules == 0 ? "OPTIONAL Â· ALL DISABLED" : $"{activeRules} ACTIVE Â· READY";
        ArenaBoardSummaryRulesDetail.Text = arenaPickRules.Count == 0
            ? "First matching rule wins when added"
            : $"{arenaPickRules.Count} rule{(arenaPickRules.Count == 1 ? string.Empty : "s")} configured";
        ArenaBoardSummaryLeaderText.Text = preferredLeader is null ? "INCOMPLETE Â· NO LEADER" : leaderPriorityReviewed ? "REVIEWED" : "NEEDS REVIEW";
        ArenaBoardSummaryLeaderDetail.Text = preferredLeader is null ? "Add a champion to establish priority" : $"Preferred leader: {preferredLeader}";
        ArenaBoardSummarySessionText.Text = !connected ? "OFFLINE Â· CONNECT RAID" : automationActive ? "ACTIVE" : !draftReady ? "BLOCKED Â· COMPLETE PLAN" : leaderPriorityReviewed ? "READY TO RUN" : "EXECUTABLE Â· REVIEW LEADER";
        ArenaBoardSummarySessionDetail.Text = $"Battle limit: {ArenaBattleLimitBox.Text.Trim()} Â· Auto-refill {(ArenaAutoRefillCheckBox.IsChecked == true ? "on" : "off")}";
    }

    private void UpdateArenaBoardStatus(bool connected, bool automationActive)
    {
        var status = !connected
            ? "Strategy saved Â· Connect RAID to run"
            : automationActive
                ? "Strategy saved Â· Automation on"
                : "Strategy saved Â· Automation off";
        if (ArenaStrategyStatusText.Text.StartsWith("Save failed", StringComparison.OrdinalIgnoreCase)) status = ArenaStrategyStatusText.Text;
        ArenaBoardStatusText.Text = status;
    }

    private void UpdateArenaPriorityCounts()
    {
        if (!IsInitialized) return;
        for (var index = 0; index < arenaBanPriority.Count; index++) arenaBanPriority[index].Order = index;
        ArenaBanPriorityCountText.Text = $"{arenaBanPriority.Count} items";
        PickRulesCountText.Text = $"{arenaPickRules.Count} items";
        ArenaLeaderPriorityCountText.Text = $"{arenaLeaderPriority.Count} items";
        ArenaBoardBanTabCount.Text = arenaBanPriority.Count == 0 ? "Optional" : $"{arenaBanPriority.Count} configured";
        ArenaBoardPickRulesTabCount.Text = arenaPickRules.Count == 0 || arenaPickRules.All(rule => !rule.Enabled)
            ? "Optional"
            : $"{arenaPickRules.Count(rule => rule.Enabled)} active";
        ArenaBoardLeaderTabCount.Text = arenaLeaderPriority.Count == 0 ? "Incomplete" : leaderPriorityReviewed ? "Reviewed" : "Needs review";
        ArenaBoardBanEmpty.Visibility = arenaBanPriority.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardPickRulesEmpty.Visibility = arenaPickRules.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardLeaderEmpty.Visibility = arenaLeaderPriority.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (ArenaBoardRailToggle.Visibility != Visibility.Collapsed)
            ArenaBoardRailToggle.Content = ArenaBoardStrategyRail.Visibility == Visibility.Visible
                ? "Close strategy workspace"
                : "Strategy workspace";
    }

    private void UpdateDiagnosticIndicator()
    {
        if (!IsInitialized) return;
        var activeTraces = new List<string>();
        if (rewardDiagnostics.IsRecording) activeTraces.Add("Recording reward trace");
        if (battleDiagnostics.ManualReferenceActive) activeTraces.Add("Recording manual battle trace");
        if (mythicalClickTrace.IsRecording) activeTraces.Add("Recording Mythical click path");
        var hasActiveTrace = activeTraces.Count > 0;
        var status = hasActiveTrace ? string.Join(" â€¢ ", activeTraces) : "No active diagnostic trace.";
        DiagnosticIndicatorButton.Content = hasActiveTrace ? string.Join(" + ", activeTraces) : "Recording diagnostics";
        DiagnosticIndicatorButton.Visibility = hasActiveTrace ? Visibility.Visible : Visibility.Collapsed;
        DeveloperTraceStatusText.Text = status;
    }

    private void UpdateDraftModeUi()
    {
        if (!IsInitialized) return;
        var adaptive = arenaDraftMode == ArenaDraftMode.AdaptiveDraft;
        AdaptiveDraftModeButton.IsChecked = adaptive;
        PresetLineupModeButton.IsChecked = !adaptive;
        AdaptiveDraftModeButton.IsEnabled = arenaMode == LiveArenaAutomationMode.Off;
        PresetLineupModeButton.IsEnabled = arenaMode == LiveArenaAutomationMode.Off;
        AdaptiveDraftModeButton.IsHitTestVisible = !adaptive;
        PresetLineupModeButton.IsHitTestVisible = adaptive;
        ArenaPoolGrid.Visibility = adaptive ? Visibility.Visible : Visibility.Collapsed;
        ArenaPoolEmptyState.Visibility = adaptive && arenaPool.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AdaptiveDraftControls.Visibility = adaptive ? Visibility.Visible : Visibility.Collapsed;
        PresetLineupPanel.Visibility = adaptive ? Visibility.Collapsed : Visibility.Visible;
        PickRulesTab.IsEnabled = !adaptive;
        AddPickRuleButton.IsEnabled = !adaptive && arenaMode == LiveArenaAutomationMode.Off && arenaCatalog.Count > 0 && teamCandidates.Count > 0;
        PickRulesList.IsHitTestVisible = arenaMode == LiveArenaAutomationMode.Off;
        PickRuleEditorPanel.IsEnabled = arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardAdaptiveButton.IsChecked = adaptive;
        ArenaBoardPresetButton.IsChecked = !adaptive;
        ArenaBoardAdaptiveButton.IsEnabled = arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardPresetButton.IsEnabled = arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardAdaptiveButton.IsHitTestVisible = !adaptive && arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardPresetButton.IsHitTestVisible = adaptive && arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardAdaptivePanel.Visibility = adaptive ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardPresetPanel.Visibility = adaptive ? Visibility.Collapsed : Visibility.Visible;
        ArenaBoardPickRulesTab.IsEnabled = !adaptive && arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardPickRulesList.IsHitTestVisible = !adaptive && arenaMode == LiveArenaAutomationMode.Off;
        if (adaptive && ArenaBoardStrategyTabs.SelectedIndex == 1) ArenaBoardStrategyTabs.SelectedIndex = 0;
        if (adaptive && arenaBoardStep == ArenaBoardStep.PickRules) arenaBoardStep = ArenaBoardStep.Lineup;
        ArenaDraftRosterGrid.IsHitTestVisible = adaptive && arenaMode == LiveArenaAutomationMode.Off;
        ArenaBoardPresetList.IsHitTestVisible = !adaptive && arenaMode == LiveArenaAutomationMode.Off;
        ArenaDraftRosterEmptyState.Visibility = adaptive && arenaPool.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ArenaBoardPresetCount.Text = $"{presetLineupSlots.Count(slot => slot.HasPrimary)} / 5 CONFIGURED";
    }

    private void CancelArenaAutomation_Click(object sender, RoutedEventArgs e)
    {
        var submitted = pendingArenaDecision is not null || pendingArenaSessionDecision is not null || pendingBattleOpenerDecision is not null;
        arenaMode = LiveArenaAutomationMode.Off;
        continuousArenaSession = false;
        pendingArenaDecision = null;
        pendingArenaSessionDecision = null;
        rewardBatchInProgress = false;
        rewardClaimBaseline = 0;
        pendingBattleOpenerDecision = null;
        pendingBattleOpenerSnapshot = null;
        battleSkillStabilizationPending = false;
        lastArenaDecisionKey = null;
        ArenaStrategyStatusText.Text = submitted
            ? "Automation stopped. The action already submitted to RAID cannot be retracted; no later action will run."
            : "Automation stopped by user override.";
        ArenaHudActionText.Text = "Automation stopped by user";
        AddBattleEvent(ArenaStrategyStatusText.Text);
        UpdateArenaModeUi();
    }

    private void StartDraftSimulation_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            draftSimulator = new(CaptureArenaStrategy(true), SimulatorFirstBox.SelectedIndex == 0, SimulatorRuleBox.SelectedIndex == 0, arenaRoleCatalog.RolesByBaseId);
            draftSimulationResolved = false;
            simulatorEvents.Clear();
            simulatorEvents.Add("Simulation started. No command will be sent to RAID.");
            RefreshDraftSimulation();
        }
        catch (Exception exception) { SimulatorStatusText.Text = exception.Message; }
    }

    private void RunSimulatorBotTurn_Click(object sender, RoutedEventArgs e)
    {
        if (draftSimulator is null) return;
        try
        {
            var decision = draftSimulator.RunPlayerTurn();
            simulatorEvents.Add($"BOT PICK â€¢ {decision.Explanation}");
            if (decision.RuleEvaluations is { Length: > 0 })
                foreach (var evaluation in decision.RuleEvaluations) simulatorEvents.Add($"RULE CHECK â€¢ {evaluation.Explanation}");
            RefreshDraftSimulation();
        }
        catch (Exception exception) { SimulatorStatusText.Text = exception.Message; }
    }

    private void SimulatorOpponentSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (simulatorOpponentView is null) return;
        simulatorOpponentView.Refresh();
        SimulatorOpponentCandidateBox.SelectedIndex = simulatorOpponentView.IsEmpty ? -1 : 0;
        foreach (var row in simulatorOpponentView.Cast<ArenaCatalogRow>().Take(24)) _ = LoadCatalogPortraitAsync(row);
    }

    private void AddSimulatorOpponentPick_Click(object sender, RoutedEventArgs e)
    {
        if (draftSimulator is null || SimulatorOpponentCandidateBox.SelectedItem is not ArenaCatalogRow champion) return;
        try
        {
            draftSimulator.AddOpponentPick(champion.TypeId, champion.BaseId, champion.Name);
            simulatorEvents.Add($"OPPONENT PICK â€¢ {champion.Name}");
            SimulatorOpponentCandidateBox.IsDropDownOpen = false;
            RefreshDraftSimulation();
        }
        catch (Exception exception) { SimulatorStatusText.Text = exception.Message; }
    }

    private void ResolveDraftSimulation_Click(object sender, RoutedEventArgs e)
    {
        if (draftSimulator is null || SimulatorPlayerBanBox.SelectedIndex < 0) return;
        try
        {
            var result = draftSimulator.Resolve(SimulatorPlayerBanBox.SelectedIndex);
            simulatorEvents.Add($"BOT BAN â€¢ {result.Ban.Explanation}");
            simulatorEvents.Add($"BOT LEADER â€¢ {result.Leader.Explanation}");
            draftSimulationResolved = true;
            RefreshDraftSimulation();
        }
        catch (Exception exception) { SimulatorStatusText.Text = exception.Message; }
    }

    private void RefreshDraftSimulation()
    {
        simulatorPlayerRows.Clear();
        simulatorOpponentRows.Clear();
        if (draftSimulator is null) return;
        foreach (var hero in draftSimulator.PlayerHeroes)
        {
            var source = teamCandidates.FirstOrDefault(row => row.Instance.BaseId == hero.BaseId);
            simulatorPlayerRows.Add(new(hero.BaseId, hero.Name, source?.Portrait));
        }
        foreach (var hero in draftSimulator.EnemyHeroes)
        {
            var source = arenaCatalog.FirstOrDefault(row => row.BaseId == hero.BaseId);
            simulatorOpponentRows.Add(new(hero.BaseId, hero.Name, source?.Portrait));
        }
        SimulatorRunBotButton.IsEnabled = !draftSimulationResolved && draftSimulator.CurrentActor == "player";
        SimulatorAddOpponentButton.IsEnabled = !draftSimulationResolved && draftSimulator.CurrentActor == "opponent";
        SimulatorResolveButton.IsEnabled = !draftSimulationResolved && draftSimulator.PicksComplete;
        if (draftSimulator.PicksComplete && SimulatorPlayerBanBox.SelectedIndex < 0 && simulatorPlayerRows.Count > 0)
            SimulatorPlayerBanBox.SelectedIndex = 0;
        SimulatorStatusText.Text = draftSimulationResolved ? "Simulation complete. Review the exact pick, ban, and leader explanations below."
            : draftSimulator.PicksComplete ? "All picks complete. Choose the champion banned by the opponent, then resolve."
            : $"{(draftSimulator.CurrentActor == "player" ? "Bot" : "Opponent")} turn â€¢ {draftSimulator.PicksRemainingThisTurn} pick(s) remaining.";
        if (simulatorEvents.Count > 0) SimulatorEventList.ScrollIntoView(simulatorEvents[^1]);
    }

    private void ApplyFilter()
    {
        if (!IsInitialized || view is null) return;
        view.Refresh();
        CountText.Text = $"Showing {view.Cast<object>().Count()} of {champions.Count} champions";
    }

    private void ShowError(string message, string? status = null)
    {
        StatusText.Text = status ?? message;
        MessageBox.Show(this, message, "ArenaDrafter", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private async void OnProbeError(string message)
    {
        Log.Error($"Native probe reported: {message}");
        await ResetProbeAsync();
        ConnectButton.IsEnabled = true;
        RefreshButton.IsEnabled = false;
        ShowError(message);
    }

    private void OpenLogs_Click(object sender, RoutedEventArgs e) => Log.OpenDirectory();

    private async Task ResetProbeAsync()
    {
        if (probe is not null)
        {
            try { await probe.DisposeAsync(); }
            catch (Exception exception) { Log.Error("Failed to dispose the previous probe connection.", exception); }
        }
        battleDiagnostics.Dispose();
        rewardDiagnostics.Dispose();
        mythicalClickTrace.Dispose();
        probe = null;
        connectedRaidProcessId = 0;
        lastLiveArena = null;
        liveArenaPlayerRows.Clear();
        liveArenaEnemyRows.Clear();
        liveDraftActive = false;
        arenaMode = LiveArenaAutomationMode.Off;
        continuousArenaSession = false;
        pendingArenaDecision = null;
        pendingArenaSessionDecision = null;
        rewardBatchInProgress = false;
        rewardClaimBaseline = 0;
        pendingBattleOpenerDecision = null;
        pendingBattleOpenerSnapshot = null;
        pendingBattleActionId = 0;
        lastBattleSnapshot = null;
        battleAutoRecoveryRequired = false;
        battleAutoRetryCount = 0;
        ResetBattleOpenerGuards();
        battleSkillStabilizationPending = false;
        diagnosticClickPathArmed = false;
        battleOpenerInitialized = false;
        battleOpenerProgress.Clear();
        ArenaAutoRefillCheckBox.IsChecked = false;
        BattleDiagnosticButton.Content = "Record manual trace";
        BattleDiagnosticClickButton.IsEnabled = true;
        MythicalClickTraceButton.Content = "Record Mythical click path";
        RewardDiagnosticButton.Content = "Record reward trace";
        BattleDiagnosticStatusText.Text = "Session diagnostics start when RAID connects.";
        ArenaRulesText.Text = "DRAFT RULE â€¢ WAITING FOR RAID";
        ConnectButton.Visibility = Visibility.Visible;
        ConnectButton.IsEnabled = true;
        UpdateDiagnosticIndicator();
        UpdateArenaModeUi();
    }

    private async void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (closing) return;
        e.Cancel = true;
        closing = true;
        Log.Info("Application is closing.");
        if (continuousArenaSession && arenaSessionEndedAt is null) arenaSessionEndedAt = DateTime.UtcNow;
        FinalizeArenaDashboardRun();
        lifetime.Cancel();
        sessionDashboardTimer.Stop();
        if (probe is not null)
        {
            try { await probe.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5)); }
            catch { }
        }
        battleDiagnostics.Dispose();
        rewardDiagnostics.Dispose();
        Close();
    }
}

