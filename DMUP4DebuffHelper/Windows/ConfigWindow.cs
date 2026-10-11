using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using P3 = DMUP3BlackholeHelper;

namespace DMUP4DebuffHelper.Windows;

public sealed class ConfigWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private readonly Configuration configuration;
    private static readonly Vector4 ActiveTextColor = new(0.25f, 1.0f, 0.35f, 1.0f);
    private static readonly Vector4 RealTextColor = new(0.25f, 0.85f, 1.0f, 1.0f);
    private static readonly Vector4 FakeTextColor = new(1.0f, 0.65f, 0.2f, 1.0f);
    private static readonly Vector4 ErrorTextColor = new(1.0f, 0.25f, 0.25f, 1.0f);
    private static readonly Vector4 DisabledTextColor = new(0.65f, 0.65f, 0.65f, 1.0f);

    public ConfigWindow(Plugin plugin) : base("DMU Helper###DMUP4DebuffConfig")
    {
        this.plugin = plugin;
        configuration = plugin.Configuration;

        Size = new Vector2(520, 360);
        SizeCondition = ImGuiCond.FirstUseEver;
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        if (!ImGui.BeginTabBar("##DMUHelperTabs"))
        {
            return;
        }

        if (ImGui.BeginTabItem("Settings"))
        {
            DrawSettingsTab();
            ImGui.EndTabItem();
        }

        if (ImGui.BeginTabItem("Review"))
        {
            DrawBuffSummaryTab();
            ImGui.EndTabItem();
        }

        ImGui.EndTabBar();
    }

    private void DrawSettingsTab()
    {
        if (ImGui.Button("Open helper"))
        {
            plugin.OpenHelperUi();
        }

        var onlyShowInInstance = configuration.OnlyShowInInstance;
        if (ImGui.Checkbox("Only show in instance", ref onlyShowInInstance))
        {
            plugin.SetOnlyShowInInstance(onlyShowInInstance);
        }

        ImGui.TextDisabled("Hides the helper outside Dancing Mad (Ultimate) unless preview is enabled.");

        var showPreview = configuration.PreviewWhenInactive;
        if (ImGui.Checkbox("Preview helper", ref showPreview))
        {
            plugin.SetPreviewWhenInactive(showPreview);
        }

        var helperFontScale = configuration.HelperFontScale;
        if (ImGui.SliderFloat("Helper font scale", ref helperFontScale, 0.75f, 2.0f, "%.2f"))
        {
            plugin.SetHelperFontScale(helperFontScale);
        }

        var helperIconScale = configuration.HelperIconScale;
        if (ImGui.SliderFloat("Helper icon scale", ref helperIconScale, 0.75f, 3.0f, "%.2f"))
        {
            plugin.SetHelperIconScale(helperIconScale);
        }

        var helperBackgroundOpacity = configuration.HelperBackgroundOpacity;
        if (ImGui.SliderFloat("Helper background opacity", ref helperBackgroundOpacity, 0.15f, 1.0f, "%.2f"))
        {
            plugin.SetHelperBackgroundOpacity(helperBackgroundOpacity);
        }

        ImGui.Separator();
        ImGui.TextUnformatted("P1 Kefka");
        var showP1Calls = configuration.ShowP1Calls;
        if (ImGui.Checkbox("Show P1 calls", ref showP1Calls))
        {
            configuration.ShowP1Calls = showP1Calls;
            plugin.SaveConfiguration();
        }

        ImGui.TextDisabled("Calls Kefka's rings (stack/spread, ice, lightning), the statue's look away/look at, rocks and cleaves.");

        ImGui.Separator();
        ImGui.TextUnformatted("P2 Forsaken");
        var showP2Calls = configuration.ShowP2Calls;
        if (ImGui.Checkbox("Show P2 calls", ref showP2Calls))
        {
            configuration.ShowP2Calls = showP2Calls;
            plugin.SaveConfiguration();
        }

        P2BuddySettings.DrawBuddyCombo(plugin);
        ImGui.TextDisabled("Buddies: MT/H1, OT/H2, M1/R1, M2/R2. Used for your soak group and the buddy's marker.");

        ImGui.Separator();
        ImGui.TextUnformatted("P3 Black Hole");
        DrawStrategySetting();
        ImGui.TextDisabled("Does not support double tethers, yet.");

        ImGui.Separator();
        ImGui.TextUnformatted("P4 Debuffs");
        ImGui.TextDisabled("P4 debuff detection is automatic. The helper only keeps the P4 debuffs it knows how to resolve.");
        DrawP4Settings();

        ImGui.Separator();
        ImGui.TextUnformatted("Diagnostics");
        var discoveryLogging = configuration.DiscoveryLogging;
        if (ImGui.Checkbox("Discovery logging", ref discoveryLogging))
        {
            configuration.DiscoveryLogging = discoveryLogging;
            plugin.SaveConfiguration();
        }

        ImGui.TextDisabled("Writes boss/NPC statuses, casts and actions, plus headmarkers, tethers and arena effects in DMU to /xllog as [DMU discovery].");

        ImGui.Separator();
        ImGui.TextWrapped("The helper only scans while you are in DMU.");
        ImGui.TextWrapped("P3 Black Hole appears while assignment data is detected, then disappears when that information is no longer active.");
        ImGui.TextWrapped("P4 debuffs appear automatically when known P4 debuffs or boss tell status 2056 are detected.");
    }

    private void DrawP4Settings()
    {
        var showActive = configuration.ShowP4Active;
        if (ImGui.Checkbox("Show Active", ref showActive))
        {
            configuration.ShowP4Active = showActive;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showCalls = configuration.ShowP4Calls;
        if (ImGui.Checkbox("Show Calls", ref showCalls))
        {
            configuration.ShowP4Calls = showCalls;
            plugin.SaveConfiguration();
        }

        ImGui.SameLine();
        var showGazes = configuration.ShowP4Gazes;
        if (ImGui.Checkbox("Show Gazes", ref showGazes))
        {
            configuration.ShowP4Gazes = showGazes;
            plugin.SaveConfiguration();
        }

        if (!configuration.ShowP4Calls)
        {
            ImGui.BeginDisabled();
        }

        var showStopOnly = configuration.CallsShowStopOnly;
        if (ImGui.Checkbox("Calls: only show accel STOP players", ref showStopOnly))
        {
            configuration.CallsShowStopOnly = showStopOnly;
            plugin.SaveConfiguration();
        }

        ImGui.SetNextItemWidth(180.0f);
        var playerDisplayLabel = GetPlayerDisplayLabel(configuration.CallsPlayerDisplay);
        if (ImGui.BeginCombo("Calls: show players as", playerDisplayLabel))
        {
            foreach (var display in Enum.GetValues<P4CallPlayerDisplay>())
            {
                var isSelected = display == configuration.CallsPlayerDisplay;
                if (ImGui.Selectable(GetPlayerDisplayLabel(display), isSelected))
                {
                    configuration.CallsPlayerDisplay = display;
                    plugin.SaveConfiguration();
                }

                if (isSelected)
                {
                    ImGui.SetItemDefaultFocus();
                }
            }

            ImGui.EndCombo();
        }

        if (!configuration.ShowP4Calls)
        {
            ImGui.EndDisabled();
        }
    }

    private static string GetPlayerDisplayLabel(P4CallPlayerDisplay display)
    {
        return display switch
        {
            P4CallPlayerDisplay.FirstName => "First name",
            P4CallPlayerDisplay.JobIcon => "Job icon",
            _ => "Full name",
        };
    }

    private void DrawStrategySetting()
    {
        var selectedOption = P3.BlackHoleStrategy.GetOption(configuration.SelectedBlackHoleStrategy);

        ImGui.SetNextItemWidth(180.0f);
        if (!ImGui.BeginCombo("Black Hole strat", selectedOption.Label))
        {
            return;
        }

        foreach (var option in P3.BlackHoleStrategy.Options)
        {
            var isSelected = option.Kind == selectedOption.Kind;
            if (ImGui.Selectable(option.Label, isSelected))
            {
                plugin.SetSelectedBlackHoleStrategy(option.Kind);
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private void DrawBuffSummaryTab()
    {
        DrawP3Review();
        ImGui.Separator();
        ImGui.TextUnformatted("P4 Debuffs");

        if (!plugin.IsInDmu)
        {
            ImGui.TextDisabled("Waiting for DMU.");
            DrawPullHistory(plugin.PullSnapshots);
            return;
        }

        if (HasCurrentPullSummary())
        {
            DrawCurrentPullSummary();
        }
        else
        {
            ImGui.TextDisabled("Waiting for P4 debuffs.");
        }

        DrawPullHistory(plugin.PullSnapshots);
    }

    private void DrawP3Review()
    {
        ImGui.TextUnformatted("P3 Black Hole");
        if (HasCurrentP3Summary())
        {
            DrawCurrentP3Summary();
        }
        else
        {
            ImGui.TextDisabled("Waiting for P3 Black Hole data.");
        }

        DrawP3PullHistory(plugin.P3BlackHole.PullSnapshots);
    }

    private bool HasCurrentP3Summary()
    {
        return plugin.P3BlackHole.CurrentAssignments.Any(HasP3AssignmentData);
    }

    private void DrawCurrentP3Summary()
    {
        var header = $"Current P3 - Timer {FormatCombatTimer(plugin.P3BlackHole.CurrentPullElapsedSeconds)}###CurrentP3PullSummary";
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        DrawPostP3OrderButton(plugin.P3BlackHole.CurrentAssignments, "Current");
        DrawP3AssignmentSummary(plugin.P3BlackHole.CurrentAssignments, "Current");
    }

    private void DrawP3PullHistory(IReadOnlyList<P3.BlackHolePullSnapshot> snapshots)
    {
        var snapshotsWithAssignments = snapshots
            .Select((snapshot, index) => (Snapshot: snapshot, PullNumber: index + 1))
            .Where(entry => entry.Snapshot.Assignments.Any(HasP3AssignmentData))
            .ToList();
        if (snapshotsWithAssignments.Count == 0)
        {
            return;
        }

        ImGui.TextUnformatted("Recorded P3 pulls");
        foreach (var (snapshot, pullNumber) in snapshotsWithAssignments.OrderByDescending(entry => entry.PullNumber))
        {
            var header = $"P3 Pull {pullNumber} - Timer {FormatCombatTimer(snapshot.CombatElapsedSeconds)}###P3PullSnapshot{pullNumber}";
            if (!ImGui.CollapsingHeader(header))
            {
                continue;
            }

            ImGui.TextDisabled($"{snapshot.Reason} - {snapshot.CapturedAtUtc:HH:mm:ss} UTC");
            DrawPostP3OrderButton(snapshot.Assignments, $"Pull{pullNumber}");
            DrawP3AssignmentSummary(snapshot.Assignments, $"Pull{pullNumber}");
        }
    }

    private void DrawPostP3OrderButton(IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> assignments, string idSuffix)
    {
        if (ImGui.Button($"Post order to chat##P3PostOrder{idSuffix}"))
        {
            plugin.PostP3AssignmentOrderToChat(assignments);
        }
    }

    private static void DrawP3AssignmentSummary(IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> assignments, string idSuffix)
    {
        var lineAssignments = assignments
            .Where(HasP3AssignmentData)
            .OrderBy(GetP3AssignmentSortKey)
            .ThenBy(assignment => assignment.PartyIndex)
            .ToList();
        ImGui.TextUnformatted("Assignments");
        if (lineAssignments.Count == 0)
        {
            ImGui.TextDisabled("No line assignments recorded.");
            return;
        }

        if (!ImGui.BeginTable($"##P3Assignments{idSuffix}", 3, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Player");
        ImGui.TableSetupColumn("Line");
        ImGui.TableSetupColumn("Role");
        ImGui.TableHeadersRow();

        foreach (var assignment in lineAssignments)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(assignment.MemberName);
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(assignment.HasLine ? assignment.LineName : "Line not captured");
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(assignment.HasLine ? assignment.RoleName : "Accretion");
        }

        ImGui.EndTable();
    }

    private static bool HasP3AssignmentData(P3.LocalPlayerBlackHoleAssignment assignment)
    {
        return assignment.HasLine || assignment.HadAccretion;
    }

    private static int GetP3AssignmentSortKey(P3.LocalPlayerBlackHoleAssignment assignment)
    {
        return (assignment.HadAccretion, assignment.LineGroup) switch
        {
            (false, P3.LineGroup.First) => 0,
            (false, P3.LineGroup.Second) => 1,
            (false, P3.LineGroup.Third) => 2,
            (true, P3.LineGroup.First) => 3,
            (true, P3.LineGroup.Second) => 4,
            (true, P3.LineGroup.Third) => 5,
            (true, P3.LineGroup.None) => 6,
            _ => 99,
        };
    }

    private bool HasCurrentPullSummary()
    {
        return plugin.CurrentPullDebuffRecords.Count > 0 || plugin.CurrentPullBossTells.Count > 0;
    }

    private void DrawCurrentPullSummary()
    {
        var header = $"Current pull - Timer {FormatCombatTimer(plugin.CurrentPullElapsedSeconds)}###CurrentP4PullSummary";
        if (!ImGui.CollapsingHeader(header, ImGuiTreeNodeFlags.DefaultOpen))
        {
            return;
        }

        ImGui.TextWrapped("This records all party P4 debuffs seen this pull so the mechanic can be reviewed after a wipe.");
        DrawBossTellSummary(plugin.CurrentPullBossTells);
        ImGui.Separator();
        DrawDebuffRecordSummary(plugin.CurrentPullDebuffRecords, "Current");
    }

    private static void DrawPullHistory(IReadOnlyList<P4PullSnapshot> snapshots)
    {
        if (snapshots.Count == 0)
        {
            return;
        }

        ImGui.Separator();
        ImGui.TextUnformatted("Recorded pulls");
        for (var i = snapshots.Count - 1; i >= 0; i--)
        {
            DrawPullSnapshot(snapshots[i], i + 1);
        }
    }

    private static void DrawPullSnapshot(P4PullSnapshot snapshot, int pullNumber)
    {
        var header = $"Pull {pullNumber} - Timer {FormatCombatTimer(snapshot.CombatElapsedSeconds)}###P4PullSnapshot{pullNumber}";
        if (!ImGui.CollapsingHeader(header))
        {
            return;
        }

        ImGui.TextDisabled($"{snapshot.Reason} - {snapshot.CapturedAtUtc:HH:mm:ss} UTC");
        DrawBossTellSummary(snapshot.BossTells);
        ImGui.Separator();
        DrawDebuffRecordSummary(snapshot.Debuffs, $"Pull{pullNumber}");
    }

    private static void DrawBossTellSummary(IReadOnlyList<BossTellSnapshot> bossTells)
    {
        ImGui.TextUnformatted("Boss tells");
        if (bossTells.Count == 0)
        {
            ImGui.TextDisabled("No boss tells recorded.");
            return;
        }

        foreach (var tell in bossTells.OrderBy(tell => tell.SeenAtUtc))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, GetRealityColor(tell.Reality));
            ImGui.BulletText($"{tell.SeenAtUtc:HH:mm:ss} - {FormatBoss(tell.Boss)} {FormatGroup(tell.Group)}: {tell.Param} ({FormatReality(tell.Reality)})");
            ImGui.PopStyleColor();
        }
    }

    private static void DrawDebuffRecordSummary(IReadOnlyList<P4DebuffRecord> records, string idSuffix)
    {
        ImGui.TextUnformatted("Debuff records");
        if (records.Count == 0)
        {
            ImGui.TextDisabled("No P4 debuffs recorded.");
            return;
        }

        var unknownCount = records.Count(record => record.Group != P4MechanicGroup.Flood && record.Reality == RealityState.Unknown);
        if (unknownCount > 0)
        {
            ImGui.TextColored(ErrorTextColor, $"{unknownCount} debuff record(s) were Unknown because no matching boss tell was captured.");
        }

        foreach (var group in records
            .OrderBy(record => record.Group)
            .ThenBy(record => record.SeenAtUtc)
            .GroupBy(record => record.Group))
        {
            ImGui.TextUnformatted($"{FormatGroup(group.Key)} ({group.Count()})");

            if (!ImGui.BeginTable($"##P4DebuffRecordSummary{idSuffix}{group.Key}", 6, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
            {
                continue;
            }

            ImGui.TableSetupColumn("Player");
            ImGui.TableSetupColumn("Debuff");
            ImGui.TableSetupColumn("State");
            ImGui.TableSetupColumn("Tell");
            ImGui.TableSetupColumn("Timer");
            ImGui.TableSetupColumn("Instruction");
            ImGui.TableHeadersRow();

            foreach (var record in group
                .OrderBy(record => record.SeenAtUtc)
                .ThenBy(record => record.PartyIndex)
                .ThenBy(record => record.StatusId))
            {
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.MemberName);
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.StatusName);
                ImGui.TableNextColumn();
                ImGui.PushStyleColor(ImGuiCol.Text, GetStateColor(record));
                ImGui.TextUnformatted(FormatState(record));
                ImGui.PopStyleColor();
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(record.TellParam?.ToString() ?? "--");
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(FormatTime(record.RemainingTimeAtCapture));
                ImGui.TableNextColumn();
                ImGui.TextWrapped(record.Instruction);
            }

            ImGui.EndTable();
        }
    }

    private static Vector4 GetRealityColor(RealityState reality)
    {
        return reality switch
        {
            RealityState.Real => RealTextColor,
            RealityState.Fake => FakeTextColor,
            RealityState.Unknown => ErrorTextColor,
            _ => DisabledTextColor,
        };
    }

    private static Vector4 GetStateColor(P4DebuffRecord record)
    {
        if (record.Group == P4MechanicGroup.Flood)
        {
            var floodSide = record.FloodSide != FloodSide.None
                ? record.FloodSide
                : P4Flood.GetWoundSide(P4Flood.GetStatusWoundColor(record.StatusId));
            if (floodSide == FloodSide.None)
            {
                floodSide = P4Flood.GetWoundSide(record.WoundColor);
            }

            return floodSide switch
            {
                FloodSide.Blue => RealTextColor,
                FloodSide.Purple => FakeTextColor,
                _ => DisabledTextColor,
            };
        }

        return GetRealityColor(record.Reality);
    }

    private static string FormatState(P4DebuffRecord record)
    {
        if (record.Group == P4MechanicGroup.Flood)
        {
            var destinationWound = P4Flood.ResolveDestinationWound(record.StatusId, record.WoundColor, record.Reality);
            if (destinationWound == WoundColor.None)
            {
                destinationWound = P4Flood.GetWoundColorForSide(record.FloodSide);
            }

            if (destinationWound != WoundColor.None)
            {
                return $"Go {P4Flood.FormatWound(destinationWound)}";
            }

            var statusWound = P4Flood.GetStatusWoundColor(record.StatusId);
            if (statusWound != WoundColor.None)
            {
                return P4Flood.FormatWoundDebuff(statusWound);
            }

            return record.StatusId switch
            {
                P4Flood.AllaganFieldStatusId => "Allagan Field",
                P4Flood.BeyondDeath1StatusId => "Opposite Wound",
                P4Flood.BeyondDeath2StatusId => "Same Wound",
                _ => "--",
            };
        }

        return FormatReality(record.Reality);
    }

    private static string FormatReality(RealityState reality)
    {
        return reality switch
        {
            RealityState.Real => "Real",
            RealityState.Fake => "Fake",
            _ => "Unknown",
        };
    }

    private static string FormatBoss(P4Boss boss)
    {
        return boss switch
        {
            P4Boss.NeoExdeath => "Neo Exdeath",
            P4Boss.Chaos => "Chaos",
            P4Boss.Kefka => "Kefka",
            _ => "Unknown boss",
        };
    }

    private static string FormatGroup(P4MechanicGroup group)
    {
        return group switch
        {
            P4MechanicGroup.GrandCross => "Grand Cross",
            P4MechanicGroup.Chaos => "Chaos",
            P4MechanicGroup.Flood => "Flood",
            _ => "Raw",
        };
    }

    private static string FormatTime(float remainingTime)
    {
        return remainingTime > 0.0f ? $"{remainingTime:0.0}s" : "--";
    }

    private static string FormatCombatTimer(float elapsedSeconds)
    {
        var totalSeconds = (int)MathF.Max(0.0f, elapsedSeconds);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }
}
