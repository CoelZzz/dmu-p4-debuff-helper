using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Windowing;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using P3 = DMUP3BlackholeHelper;

namespace DMUP4DebuffHelper.Windows;

public sealed class HelperWindow : Window, IDisposable
{
    private readonly Plugin plugin;
    private DmuHelperDisplayMode selectedPreviewMode = DmuHelperDisplayMode.P4Debuffs;
    private int selectedP3PreviewRoleIndex;
    private const uint CursedShriekStatusId = 5543;
    private static readonly Vector4 GoldColor = new(1.0f, 0.78f, 0.18f, 1.0f);
    private static readonly Vector4 RealColor = new(0.25f, 0.85f, 1.0f, 1.0f);
    private static readonly Vector4 FakeColor = new(1.0f, 0.28f, 0.22f, 1.0f);
    private static readonly Vector4 UnknownColor = new(0.75f, 0.75f, 0.75f, 1.0f);
    private static readonly Vector4 BlueSideColor = new(0.35f, 0.72f, 1.0f, 1.0f);
    private static readonly Vector4 PurpleSideColor = new(0.82f, 0.55f, 1.0f, 1.0f);
    private static readonly Vector4 TimerTextColor = new(0.94f, 0.98f, 1.0f, 1.0f);
    private static readonly Vector4 WaterCallColor = new(0.35f, 0.65f, 1.0f, 1.0f);
    private static readonly Vector4 DonutCallColor = new(0.25f, 0.85f, 0.5f, 1.0f);
    private static readonly Vector4 TwisterCallColor = new(1.0f, 0.6f, 0.25f, 1.0f);
    private static readonly Vector4 StopCallColor = new(1.0f, 0.45f, 0.4f, 1.0f);
    private static readonly Vector4 CallDividerColor = new(1.0f, 1.0f, 1.0f, 0.08f);
    private static readonly Vector4 PanelBorderColor = new(1.0f, 1.0f, 1.0f, 0.18f);
    private static readonly Vector4 PanelFillColor = new(0.02f, 0.025f, 0.03f, 0.32f);
    private static readonly Vector4 PreviewButtonColor = new(0.85f, 0.62f, 0.18f, 0.72f);
    private static readonly Vector4 PreviewButtonHoverColor = new(1.0f, 0.72f, 0.22f, 0.92f);
    private static readonly Vector4 PreviewButtonOffColor = new(0.18f, 0.18f, 0.2f, 0.78f);
    private static readonly Vector4 PreviewButtonOffHoverColor = new(0.25f, 0.25f, 0.28f, 0.92f);
    private const float HelperPadding = 10.0f;
    private const float AssignmentIconSize = 30.0f;
    private const float AccretionJobIconSize = 28.0f;
    private const float GazeDuplicateTimerToleranceSeconds = 2.0f;

    // PLD, WAR, WHM, SCH, DRG, NIN, BRD, BLM by preview party slot.
    private static readonly uint[] PreviewClassJobIds = [19, 21, 24, 28, 22, 30, 23, 25];
    private static readonly IReadOnlyList<P3PreviewRole> P3PreviewRoles =
    [
        new(P3.LineGroup.First, true, false, 41, "FIL DPS"),
        new(P3.LineGroup.Second, true, false, 41, "SIL DPS"),
        new(P3.LineGroup.Third, true, false, 41, "TIL DPS"),
        new(P3.LineGroup.First, false, false, 24, "FIL Support"),
        new(P3.LineGroup.Second, false, false, 24, "SIL Support"),
        new(P3.LineGroup.Third, false, false, 24, "TIL Support"),
        new(P3.LineGroup.First, true, true, 41, "FIL Accretion"),
        new(P3.LineGroup.Second, true, true, 41, "SIL Accretion"),
    ];

    public HelperWindow(Plugin plugin) : base("DMU Helper###DMUP4DebuffHelper")
    {
        this.plugin = plugin;

        Size = new Vector2(390, 250);
        SizeCondition = ImGuiCond.FirstUseEver;
        BgAlpha = GetHelperBackgroundOpacity();
    }

    public void Dispose()
    {
    }

    public override void Draw()
    {
        BgAlpha = GetHelperBackgroundOpacity();
        ImGui.SetWindowFontScale(Math.Clamp(plugin.Configuration.HelperFontScale, 0.75f, 2.0f));

        switch (plugin.DisplayMode)
        {
            case DmuHelperDisplayMode.P4Debuffs:
                DrawP4View(
                    GetOrderedAssignments(plugin.CurrentAssignments),
                    GetOrderedAssignments(plugin.CurrentGazeAssignments),
                    P4Calls.Build(
                        plugin.CurrentPartyCallAssignments.Concat(plugin.CurrentGazeAssignments),
                        plugin.CurrentAssignments,
                        plugin.CallSetHistory,
                        DateTime.UtcNow,
                        plugin.Configuration.CallsShowStopOnly),
                    isPreview: false);
                break;
            case DmuHelperDisplayMode.P1Calls:
                DrawP1View(plugin.P1.GetCalls(DateTime.UtcNow), isPreview: false);
                break;
            case DmuHelperDisplayMode.P2Calls:
                DrawP2View(plugin.P2.GetCalls(DateTime.UtcNow), isPreview: false);
                break;
            case DmuHelperDisplayMode.P3BlackHole:
                DrawP3View(
                    plugin.P3BlackHole.LocalAssignment,
                    plugin.P3BlackHole.CurrentAssignments,
                    plugin.P3BlackHole.HasLiveSignal ? P3LimitCutState.Inactive : plugin.P3LimitCut.CurrentState,
                    isPreview: false);
                break;
            case DmuHelperDisplayMode.Preview:
                DrawPreviewView();
                break;
            default:
                DrawHeader("DMU Helper", 0, isPreview: false);
                if (!plugin.Configuration.HelperCollapsed)
                {
                    ImGui.Spacing();
                    ImGui.TextDisabled(plugin.IsInDmu ? "Waiting for DMU helper data." : "Waiting for DMU.");
                }
                break;
        }
    }

    private void DrawP1View(IReadOnlyList<CallBlock> calls, bool isPreview)
    {
        DrawHeader(isPreview ? "P1 Preview" : "P1 Calls", calls.Count, isPreview);
        if (plugin.Configuration.HelperCollapsed)
        {
            return;
        }

        ImGui.Spacing();
        DrawP1Content(calls);
    }

    private void DrawP1Content(IReadOnlyList<CallBlock> calls)
    {
        DrawPhaseCalls(calls, "Waiting for the next P1 call.");
    }

    private void DrawP2View(IReadOnlyList<CallBlock> calls, bool isPreview)
    {
        DrawHeader(isPreview ? "P2 Preview" : "P2 Forsaken", calls.Count, isPreview);
        if (plugin.Configuration.HelperCollapsed)
        {
            return;
        }

        ImGui.Spacing();
        DrawPhaseCalls(calls, "Waiting for Forsaken.");
    }

    private void DrawPhaseCalls(IReadOnlyList<CallBlock> calls, string waitingText)
    {
        if (calls.Count == 0)
        {
            ImGui.TextDisabled(waitingText);
            return;
        }

        ImGui.TextColored(GoldColor, "Calls");
        DrawCallSetPanel(calls);
    }

    private static List<CallBlock> GetPreviewP2Calls()
    {
        // Other group (soaks sets 4-7) during tower set 2, holding a cone, with a circle buddy, while Kefka casts Past's End.
        return
        [
            P2Tracker.BuildSetCall(2, ForsakenMarker.Cone, soaksFirst: false, 6.3f),
            P2Tracker.BuildBuddyCall("Party Member 2", ForsakenMarker.Circle, ForsakenMarker.Cone, soaking: false, 6.3f),
            P2Tracker.BuildEndsCall(isPast: true, 4.1f),
        ];
    }

    private static List<CallBlock> GetPreviewP1Calls()
    {
        // One of each P1 call (they never all show at once in a pull):
        // stack/spread ring fake + lightning ring real with spread markers, a real statue gaze,
        // supports tethered to the rock statue, and a cleave on the west half.
        return
        [
            P1Tracker.BuildRingCall(
                new HashSet<uint> { P1Tracker.StackSpreadFakeIcon, P1Tracker.LightningRealIcon },
                P1Tracker.SpreadMarkerIcon,
                4.2f),
            P1Tracker.BuildCleaveCall(cleavesWest: true, 4.8f),
            P1Tracker.BuildGazeCall(RealityState.Real, 8.6f),
            P1Tracker.BuildRocksCall(
                [new("Preview Player", true), new("Party Member 2", true), new("Party Member 3", true), new("Party Member 4", true)],
                10.1f),
        ];
    }

    private void DrawP4View(
        IReadOnlyList<P4DebuffAssignment> assignments,
        IReadOnlyList<P4DebuffAssignment> gazeAssignments,
        IReadOnlyList<P4Call> calls,
        bool isPreview)
    {
        var displayedGazes = GetRepresentativeGazeAssignments(gazeAssignments);
        DrawHeader(isPreview ? "P4 Debuffs Preview" : "P4 Debuffs", GetVisibleP4Count(assignments.Count, displayedGazes.Count, calls.Count), isPreview);
        if (plugin.Configuration.HelperCollapsed)
        {
            return;
        }

        ImGui.Spacing();
        if (!plugin.IsInDmu && !isPreview)
        {
            ImGui.TextDisabled("Waiting for DMU.");
            return;
        }

        DrawP4Content(assignments, displayedGazes, calls);
    }

    private void DrawP4Content(
        IReadOnlyList<P4DebuffAssignment> assignments,
        IReadOnlyList<GazeDisplayAssignment> gazeAssignments,
        IReadOnlyList<P4Call> calls)
    {
        var configuration = plugin.Configuration;
        if (GetVisibleP4Count(assignments.Count, gazeAssignments.Count, calls.Count) == 0)
        {
            ImGui.TextDisabled(configuration.ShowP4Active || configuration.ShowP4Calls || configuration.ShowP4Gazes
                ? "Waiting for P4 debuffs."
                : "All P4 sections are hidden in settings.");
            return;
        }

        var drewSection = false;
        if (configuration.ShowP4Active)
        {
            DrawSection("Active", assignments, "Active");
            drewSection = true;
        }

        if (configuration.ShowP4Calls && calls.Count > 0)
        {
            if (drewSection)
            {
                ImGui.Spacing();
            }

            DrawCallSection("Calls", calls);
            drewSection = true;
        }

        if (configuration.ShowP4Gazes)
        {
            if (drewSection)
            {
                ImGui.Spacing();
            }

            DrawGazeSection("Gazes", gazeAssignments, "Gazes");
        }
    }

    private int GetVisibleP4Count(int assignmentCount, int gazeCount, int callCount)
    {
        var configuration = plugin.Configuration;
        return (configuration.ShowP4Active ? assignmentCount : 0)
            + (configuration.ShowP4Gazes ? gazeCount : 0)
            + (configuration.ShowP4Calls ? callCount : 0);
    }

    private void DrawP3View(
        P3.LocalPlayerBlackHoleAssignment? assignment,
        IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> assignments,
        P3LimitCutState limitCutState,
        bool isPreview)
    {
        var instructionCount = P3.BlackHoleStrategy.GetInstructionsFor(assignment, plugin.Configuration.SelectedBlackHoleStrategy).Count;
        var titleBase = limitCutState.IsActive && (assignment is null || !assignment.HasLine)
            ? "P3 Limit Cut"
            : "P3 Black Hole";
        var headerCount = titleBase == "P3 Limit Cut" ? 0 : instructionCount;
        DrawHeader(isPreview ? "P3 Black Hole Preview" : titleBase, headerCount, isPreview);
        if (plugin.Configuration.HelperCollapsed)
        {
            return;
        }

        ImGui.Spacing();
        DrawP3Content(assignment, assignments, limitCutState);
    }

    private void DrawP3Content(
        P3.LocalPlayerBlackHoleAssignment? assignment,
        IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> assignments,
        P3LimitCutState limitCutState)
    {
        if (limitCutState.IsActive)
        {
            DrawP3LimitCutBlock(limitCutState);
            ImGui.Spacing();
        }

        if (assignment is null)
        {
            if (!limitCutState.IsActive)
            {
                ImGui.TextUnformatted("Your Black Hole assignment");
                ImGui.TextDisabled("Local player not found.");
            }

            return;
        }

        if (!assignment.HasLine)
        {
            if (limitCutState.IsActive)
            {
                return;
            }

            if (assignments.Any(assignment => assignment.HadAccretion))
            {
                DrawP3AccretionBlock(assignments);
                ImGui.Spacing();
            }

            ImGui.TextUnformatted("Your Black Hole assignment");
            ImGui.TextDisabled("Waiting for your line debuff.");
            return;
        }

        DrawP3AssignmentOverview(assignment, assignments);

        ImGui.Separator();
        ImGui.TextUnformatted("Your Black Hole instructions");
        var instructions = P3.BlackHoleStrategy.GetInstructionsFor(assignment, plugin.Configuration.SelectedBlackHoleStrategy);
        if (instructions.Count == 0)
        {
            ImGui.TextDisabled("No tether assignment matched.");
            return;
        }

        if (!ImGui.BeginTable("##HelperPersonalBlackHoleInstructions", 4, ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.RowBg))
        {
            return;
        }

        ImGui.TableSetupColumn("Set");
        ImGui.TableSetupColumn("Tether");
        ImGui.TableSetupColumn("Wave");
        ImGui.TableSetupColumn("Action");
        ImGui.TableHeadersRow();

        foreach (var instruction in instructions)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Set.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Tether.ToString());
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(instruction.Wave.ToString());
            ImGui.TableNextColumn();
            ImGui.TextWrapped(instruction.Action);
        }

        ImGui.EndTable();
    }

    private void DrawP3AssignmentOverview(
        P3.LocalPlayerBlackHoleAssignment assignment,
        IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> assignments)
    {
        var overviewAssignments = assignments.Count > 0
            ? assignments
            : [assignment];
        var availableWidth = ImGui.GetContentRegionAvail().X;
        if (availableWidth >= 280.0f &&
            ImGui.BeginTable("##P3AssignmentOverview", 2, ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Assignment");
            ImGui.TableSetupColumn("Accretion");
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            DrawP3LocalAssignmentBlock(assignment);
            ImGui.TableNextColumn();
            DrawP3AccretionBlock(overviewAssignments);
            ImGui.EndTable();
            return;
        }

        DrawP3LocalAssignmentBlock(assignment);
        ImGui.Spacing();
        DrawP3AccretionBlock(overviewAssignments);
    }

    private static void DrawP3LimitCutBlock(P3LimitCutState state)
    {
        ImGui.TextUnformatted("Limit Cut orientation");
        var panelStart = ImGui.GetCursorScreenPos();
        var availableWidth = MathF.Max(180.0f, ImGui.GetContentRegionAvail().X);
        var style = ImGui.GetStyle();
        var lineHeight = ImGui.GetTextLineHeight();
        var panelHeight = HelperPadding * 2.0f + lineHeight * 2.0f + style.ItemSpacing.Y;

        ImGui.GetWindowDrawList().AddRectFilled(
            panelStart,
            panelStart + new Vector2(availableWidth, panelHeight),
            ImGui.GetColorU32(PanelFillColor),
            6.0f);
        ImGui.GetWindowDrawList().AddRect(
            panelStart,
            panelStart + new Vector2(availableWidth, panelHeight),
            ImGui.GetColorU32(PanelBorderColor),
            6.0f);

        ImGui.SetCursorScreenPos(panelStart + new Vector2(HelperPadding, HelperPadding));
        ImGui.TextDisabled("New north");
        ImGui.SameLine();
        ImGui.TextColored(GoldColor, FormatLimitCutNorth(state));

        ImGui.TextDisabled("Player rotation");
        ImGui.SameLine();
        ImGui.TextColored(GetLimitCutRotationColor(state.Rotation), FormatLimitCutRotation(state.Rotation));

        ImGui.SetCursorScreenPos(panelStart + new Vector2(0.0f, panelHeight + style.ItemSpacing.Y));
    }

    private void DrawP3LocalAssignmentBlock(P3.LocalPlayerBlackHoleAssignment assignment)
    {
        ImGui.TextUnformatted("Your Black Hole assignment");
        DrawStatusIcon(assignment.LineStatusId, assignment.LineName);
        if (assignment.HadAccretion)
        {
            ImGui.SameLine();
            DrawStatusIcon(1604, "Accretion");
        }

        ImGui.TextColored(GoldColor, assignment.RoleName);
    }

    private static void DrawP3AccretionBlock(IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> assignments)
    {
        ImGui.TextUnformatted("Accretions");
        ImGui.Separator();
        var accretions = assignments
            .Where(assignment => assignment.HadAccretion)
            .OrderBy(assignment => assignment.IsDps)
            .ThenBy(assignment => assignment.HasLine ? 0 : 1)
            .ThenBy(assignment => assignment.PartyIndex)
            .ToList();
        var healerAccretion = accretions.FirstOrDefault(assignment => !assignment.IsDps);
        var dpsAccretion = accretions.FirstOrDefault(assignment => assignment.IsDps);

        DrawAccretionJobIcon(healerAccretion, "Waiting for healer Accretion", "Healer Accretion");
        ImGui.SameLine(0.0f, ImGui.GetStyle().ItemSpacing.X);
        DrawAccretionJobIcon(dpsAccretion, "Waiting for DPS Accretion", "DPS Accretion");
    }

    private static void DrawAccretionJobIcon(
        P3.LocalPlayerBlackHoleAssignment? assignment,
        string waitingTooltip,
        string readyTooltip)
    {
        if (assignment is null)
        {
            DrawClassJobPlaceholder(AccretionJobIconSize, waitingTooltip);
        }
        else
        {
            var tooltip = assignment.HasLine
                ? readyTooltip
                : $"{readyTooltip} (line not captured)";
            DrawClassJobIcon(assignment.ClassJobId, AccretionJobIconSize, tooltip);
        }
    }

    private static void DrawClassJobIcon(uint classJobId, float iconSize, string tooltip)
    {
        var iconId = GetClassJobIconId(classJobId);
        if (iconId != 0)
        {
            try
            {
                var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                var wrap = texture.GetWrapOrDefault();
                if (wrap is not null)
                {
                    ImGui.Image(wrap.Handle, new Vector2(iconSize));
                    if (ImGui.IsItemHovered())
                    {
                        ImGui.SetTooltip(tooltip);
                    }

                    return;
                }
            }
            catch
            {
            }
        }

        DrawClassJobPlaceholder(iconSize, tooltip);
    }

    private static void DrawClassJobPlaceholder(float iconSize, string tooltip)
    {
        var size = new Vector2(iconSize);
        var start = ImGui.GetCursorScreenPos();
        ImGui.Dummy(size);

        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRect(
            start,
            start + size,
            ImGui.GetColorU32(PanelBorderColor),
            4.0f);
        var text = "?";
        var textSize = ImGui.CalcTextSize(text);
        drawList.AddText(
            start + new Vector2(MathF.Max(0.0f, (size.X - textSize.X) * 0.5f), MathF.Max(0.0f, (size.Y - textSize.Y) * 0.5f)),
            ImGui.GetColorU32(UnknownColor),
            text);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static uint GetClassJobIconId(uint classJobId)
    {
        return classJobId == 0 ? 0 : 62100u + classJobId;
    }

    private void DrawPreviewView()
    {
        if (selectedPreviewMode is not (DmuHelperDisplayMode.P1Calls or DmuHelperDisplayMode.P2Calls or DmuHelperDisplayMode.P3BlackHole or DmuHelperDisplayMode.P4Debuffs))
        {
            selectedPreviewMode = DmuHelperDisplayMode.P4Debuffs;
        }

        if (selectedPreviewMode == DmuHelperDisplayMode.P1Calls)
        {
            DrawHeader("P1 Preview", GetPreviewP1Calls().Count, isPreview: true);
        }
        else if (selectedPreviewMode == DmuHelperDisplayMode.P2Calls)
        {
            DrawHeader("P2 Preview", GetPreviewP2Calls().Count, isPreview: true);
        }
        else if (selectedPreviewMode == DmuHelperDisplayMode.P3BlackHole)
        {
            DrawHeader("P3 Preview", P3.BlackHoleStrategy.GetInstructionsFor(CreateP3PreviewAssignment(), plugin.Configuration.SelectedBlackHoleStrategy).Count, isPreview: true);
        }
        else
        {
            var previewAssignments = GetPreviewAssignments();
            var previewGazes = GetRepresentativeGazeAssignments(GetPreviewGazeAssignments());
            var previewCalls = GetPreviewCalls();
            DrawHeader("P4 Debuffs Preview", GetVisibleP4Count(previewAssignments.Count, previewGazes.Count, previewCalls.Count), isPreview: true);
        }

        if (plugin.Configuration.HelperCollapsed)
        {
            return;
        }

        ImGui.Spacing();
        DrawPreviewModeSelector();
        ImGui.Separator();

        if (selectedPreviewMode == DmuHelperDisplayMode.P1Calls)
        {
            DrawP1Content(GetPreviewP1Calls());
            return;
        }

        if (selectedPreviewMode == DmuHelperDisplayMode.P2Calls)
        {
            DrawPhaseCalls(GetPreviewP2Calls(), "Waiting for Forsaken.");
            return;
        }

        if (selectedPreviewMode == DmuHelperDisplayMode.P3BlackHole)
        {
            DrawP3PreviewControls();
            var previewAssignment = CreateP3PreviewAssignment();
            DrawP3Content(previewAssignment, CreateP3PreviewAssignments(previewAssignment), CreateP3PreviewLimitCutState());
            return;
        }

        var p4PreviewAssignments = GetPreviewAssignments();
        DrawP4Content(
            p4PreviewAssignments,
            GetRepresentativeGazeAssignments(GetPreviewGazeAssignments()),
            GetPreviewCalls());
    }

    private void DrawPreviewModeSelector()
    {
        var p1Selected = selectedPreviewMode == DmuHelperDisplayMode.P1Calls;
        if (ImGui.RadioButton("P1", p1Selected))
        {
            selectedPreviewMode = DmuHelperDisplayMode.P1Calls;
        }

        ImGui.SameLine();
        var p2Selected = selectedPreviewMode == DmuHelperDisplayMode.P2Calls;
        if (ImGui.RadioButton("P2", p2Selected))
        {
            selectedPreviewMode = DmuHelperDisplayMode.P2Calls;
        }

        ImGui.SameLine();
        var p3Selected = selectedPreviewMode == DmuHelperDisplayMode.P3BlackHole;
        if (ImGui.RadioButton("P3", p3Selected))
        {
            selectedPreviewMode = DmuHelperDisplayMode.P3BlackHole;
        }

        ImGui.SameLine();
        var p4Selected = selectedPreviewMode == DmuHelperDisplayMode.P4Debuffs;
        if (ImGui.RadioButton("P4", p4Selected))
        {
            selectedPreviewMode = DmuHelperDisplayMode.P4Debuffs;
        }
    }

    private void DrawP3PreviewControls()
    {
        var selectedRole = P3PreviewRoles[selectedP3PreviewRoleIndex];
        ImGui.SetNextItemWidth(160.0f * Math.Clamp(plugin.Configuration.HelperFontScale, 0.75f, 2.0f));
        if (!ImGui.BeginCombo("Role##P3PreviewRole", selectedRole.Label))
        {
            return;
        }

        for (var i = 0; i < P3PreviewRoles.Count; i++)
        {
            var role = P3PreviewRoles[i];
            if (ImGui.Selectable(role.Label, selectedP3PreviewRoleIndex == i))
            {
                selectedP3PreviewRoleIndex = i;
            }

            if (selectedP3PreviewRoleIndex == i)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    private P3.LocalPlayerBlackHoleAssignment CreateP3PreviewAssignment()
    {
        var role = P3PreviewRoles[selectedP3PreviewRoleIndex];
        var lineStatusId = GetLineStatusId(role.LineGroup);
        return new P3.LocalPlayerBlackHoleAssignment(
            "preview",
            "You",
            0,
            role.IsDps,
            role.ClassJobId,
            role.HadAccretion,
            role.LineGroup,
            lineStatusId,
            GetLineName(lineStatusId),
            30.0f);
    }

    private static P3LimitCutState CreateP3PreviewLimitCutState()
    {
        return new P3LimitCutState(
            true,
            "SW",
            "4",
            P3LimitCutRotation.Clockwise,
            2);
    }

    private static IReadOnlyList<P3.LocalPlayerBlackHoleAssignment> CreateP3PreviewAssignments(
        P3.LocalPlayerBlackHoleAssignment selectedAssignment)
    {
        if (selectedAssignment.HadAccretion)
        {
            return selectedAssignment.IsDps
                ? [CreatePreviewAccretion("preview-healer", 1, false, 24, selectedAssignment.LineGroup), selectedAssignment]
                : [selectedAssignment, CreatePreviewAccretion("preview-dps", 1, true, 41, selectedAssignment.LineGroup)];
        }

        return
        [
            selectedAssignment,
            CreatePreviewAccretion("preview-healer", 1, false, 24, P3.LineGroup.First),
            CreatePreviewAccretion("preview-dps", 2, true, 41, P3.LineGroup.Second),
        ];
    }

    private static P3.LocalPlayerBlackHoleAssignment CreatePreviewAccretion(
        string memberKey,
        int partyIndex,
        bool isDps,
        uint classJobId,
        P3.LineGroup lineGroup)
    {
        var lineStatusId = GetLineStatusId(lineGroup);
        return new P3.LocalPlayerBlackHoleAssignment(
            memberKey,
            "Preview",
            partyIndex,
            isDps,
            classJobId,
            true,
            lineGroup,
            lineStatusId,
            GetLineName(lineStatusId),
            0.0f);
    }

    private void DrawHeader(string titleBase, int assignmentCount, bool isPreview)
    {
        var isExpanded = !plugin.Configuration.HelperCollapsed;
        var title = assignmentCount > 0
            ? $"{titleBase} ({assignmentCount})"
            : titleBase;
        var frameHeight = ImGui.GetFrameHeight();
        var start = ImGui.GetCursorScreenPos();
        var hitSize = new Vector2(frameHeight, frameHeight);

        ImGui.InvisibleButton("##DMUP4HelperCollapse", hitSize);
        var hovered = ImGui.IsItemHovered();
        if (ImGui.IsItemClicked())
        {
            plugin.SetHelperCollapsed(isExpanded);
        }

        var drawList = ImGui.GetWindowDrawList();
        var arrowCenter = start + new Vector2(hitSize.X * 0.5f, hitSize.Y * 0.52f);
        var arrowSize = MathF.Max(5.0f, frameHeight * 0.22f);
        if (isExpanded)
        {
            drawList.AddTriangleFilled(
                arrowCenter + new Vector2(-arrowSize, -arrowSize * 0.45f),
                arrowCenter + new Vector2(arrowSize, -arrowSize * 0.45f),
                arrowCenter + new Vector2(0.0f, arrowSize * 0.65f),
                ImGui.GetColorU32(GoldColor));
        }
        else
        {
            drawList.AddTriangleFilled(
                arrowCenter + new Vector2(-arrowSize * 0.45f, -arrowSize),
                arrowCenter + new Vector2(-arrowSize * 0.45f, arrowSize),
                arrowCenter + new Vector2(arrowSize * 0.65f, 0.0f),
                ImGui.GetColorU32(GoldColor));
        }

        ImGui.SameLine(0.0f, 4.0f);
        ImGui.TextColored(GoldColor, title);
        DrawHeaderActions();
        if (hovered)
        {
            ImGui.SetTooltip(isExpanded ? "Collapse DMU helper." : "Expand DMU helper.");
        }
    }

    private void DrawHeaderActions()
    {
        var previewButtonWidth = GetPreviewToggleButtonWidth();
        var settingsButtonWidth = GetSettingsButtonWidth();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        var totalWidth = previewButtonWidth + spacing + settingsButtonWidth;
        var contentRight = ImGui.GetWindowContentRegionMax().X;
        var currentLineStart = ImGui.GetCursorPosX();
        var targetX = MathF.Max(currentLineStart, contentRight - totalWidth);

        ImGui.SameLine();
        ImGui.SetCursorPosX(targetX);
        DrawPreviewToggleButton(previewButtonWidth);

        ImGui.SameLine(0.0f, spacing);
        if (ImGuiComponents.IconButton("##OpenDMUHelperSettings", FontAwesomeIcon.Cog))
        {
            plugin.OpenConfigUi();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Open DMU Helper settings.");
        }
    }

    private float GetPreviewToggleButtonWidth()
    {
        var enabled = plugin.Configuration.PreviewWhenInactive;
        var label = enabled ? "Preview on" : "Preview off";
        var style = ImGui.GetStyle();
        return MathF.Max(92.0f, ImGui.CalcTextSize(label).X + style.FramePadding.X * 2.0f + 8.0f);
    }

    private static float GetSettingsButtonWidth()
    {
        var style = ImGui.GetStyle();
        return ImGui.CalcTextSize(FontAwesomeIcon.Cog.ToIconString()).X + style.FramePadding.X * 2.0f;
    }

    private void DrawPreviewToggleButton(float buttonWidth)
    {
        var enabled = plugin.Configuration.PreviewWhenInactive;
        var label = enabled ? "Preview on" : "Preview off";
        ImGui.PushStyleColor(ImGuiCol.Button, enabled ? PreviewButtonColor : PreviewButtonOffColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, enabled ? PreviewButtonHoverColor : PreviewButtonOffHoverColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, enabled ? PreviewButtonHoverColor : PreviewButtonOffHoverColor);
        if (enabled)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.08f, 0.06f, 0.02f, 1.0f));
        }

        if (ImGui.Button(label, new Vector2(buttonWidth, 0.0f)))
        {
            plugin.SetPreviewWhenInactive(!enabled);
        }

        if (enabled)
        {
            ImGui.PopStyleColor();
        }

        ImGui.PopStyleColor(3);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Shows sample DMU helper data when no live mechanic data is available.");
        }
    }

    private List<P4DebuffAssignment> GetPreviewAssignments()
    {
        var previews = new (uint StatusId, RealityState Reality, ushort TellParam, float Time, string MemberName, int PartyIndex, WoundColor WoundColor)[]
        {
            (P4Flood.WhiteWound2StatusId, RealityState.Unknown, 0, 6.8f, "Preview Player", 0, WoundColor.White),
            (P4Flood.AllaganFieldStatusId, RealityState.Real, 1122, 7.4f, "Preview Player", 0, WoundColor.White),
            (5545, RealityState.Real, 1120, 10.4f, "Preview Player", 0, WoundColor.None),
            (5546, RealityState.Fake, 1119, 20.4f, "Preview Player", 0, WoundColor.None),
            (5548, RealityState.Real, 1122, 23.2f, "Preview Player", 0, WoundColor.None),
            (5547, RealityState.Real, 1122, 18.0f, "Preview Player", 0, WoundColor.None),
        };

        return BuildPreviewAssignments(previews);
    }

    private List<P4DebuffAssignment> GetPreviewGazeAssignments()
    {
        var previews = new (uint StatusId, RealityState Reality, ushort TellParam, float Time, string MemberName, int PartyIndex, WoundColor WoundColor)[]
        {
            (CursedShriekStatusId, RealityState.Real, 1120, 9.2f, "Short Gaze A", 1, WoundColor.None),
            (CursedShriekStatusId, RealityState.Real, 1120, 9.5f, "Short Gaze B", 2, WoundColor.None),
            (CursedShriekStatusId, RealityState.Fake, 1119, 17.8f, "Long Gaze A", 3, WoundColor.None),
            (CursedShriekStatusId, RealityState.Fake, 1119, 18.1f, "Long Gaze B", 4, WoundColor.None),
        };

        var gazes = BuildPreviewAssignments(previews);
        StableSortByTimerWithTolerance(gazes);
        return gazes;
    }

    private List<P4Call> GetPreviewCalls()
    {
        return P4Calls.Build(
            GetPreviewPartyCallAssignments().Concat(GetPreviewGazeAssignments()),
            GetPreviewAssignments(),
            new P4CallSetHistory(),
            DateTime.UtcNow,
            plugin.Configuration.CallsShowStopOnly);
    }

    private List<P4DebuffAssignment> GetPreviewPartyCallAssignments()
    {
        var previews = new List<(uint StatusId, RealityState Reality, ushort TellParam, float Time, string MemberName, int PartyIndex, WoundColor WoundColor)>
        {
            // 1st set: water stacks, lightning spreads, no accel.
            (5545, RealityState.Real, 1120, 10.4f, "Preview Player", 0, WoundColor.None),
            (5545, RealityState.Real, 1120, 10.5f, "Party Member 2", 1, WoundColor.None),
            (5544, RealityState.Real, 1120, 10.4f, "Party Member 3", 2, WoundColor.None),
            (5544, RealityState.Real, 1120, 10.6f, "Party Member 4", 3, WoundColor.None),

            // 2nd set: water spreads, lightning stacks, and accels from two batches resolve differently.
            (5545, RealityState.Fake, 1119, 20.4f, "Party Member 5", 4, WoundColor.None),
            (5545, RealityState.Fake, 1119, 20.5f, "Party Member 6", 5, WoundColor.None),
            (5544, RealityState.Fake, 1119, 20.4f, "Party Member 7", 6, WoundColor.None),
            (5544, RealityState.Fake, 1119, 20.6f, "Party Member 8", 7, WoundColor.None),
            (5546, RealityState.Fake, 1119, 20.4f, "Preview Player", 0, WoundColor.None),
            (5546, RealityState.Fake, 1119, 20.5f, "Party Member 3", 2, WoundColor.None),
            (5546, RealityState.Real, 1120, 20.4f, "Party Member 6", 5, WoundColor.None),
            (5546, RealityState.Real, 1120, 20.6f, "Party Member 8", 7, WoundColor.None),
        };

        for (var partyIndex = 0; partyIndex < 8; partyIndex++)
        {
            var memberName = partyIndex == 0 ? "Preview Player" : $"Party Member {partyIndex + 1}";
            previews.Add((5548, RealityState.Real, 1122, 23.2f + partyIndex * 0.1f, memberName, partyIndex, WoundColor.None));
            previews.Add((5547, RealityState.Real, 1122, 18.0f + partyIndex * 0.1f, memberName, partyIndex, WoundColor.None));
        }

        return BuildPreviewAssignments(previews);
    }

    private List<P4DebuffAssignment> BuildPreviewAssignments(
        IReadOnlyList<(uint StatusId, RealityState Reality, ushort TellParam, float Time, string MemberName, int PartyIndex, WoundColor WoundColor)> previews)
    {
        var assignments = new List<P4DebuffAssignment>(previews.Count);
        foreach (var preview in previews)
        {
            if (!Plugin.WatchedStatuses.TryGetValue(preview.StatusId, out var rule))
            {
                continue;
            }

            var entry = new PartyStatusEntry(
                $"Preview:{preview.PartyIndex}:{preview.StatusId}",
                $"Preview:{preview.PartyIndex}",
                preview.MemberName,
                preview.PartyIndex,
                preview.StatusId,
                rule.Name,
                plugin.GetStatusIconId(preview.StatusId),
                preview.Time,
                true,
                rule.SortOrder,
                DateTime.UtcNow,
                PreviewClassJobIds[preview.PartyIndex % PreviewClassJobIds.Length]);

            var floodSide = P4Flood.ResolveSide(rule.Id, preview.WoundColor, preview.Reality);
            assignments.Add(new P4DebuffAssignment(
                entry,
                rule,
                preview.Reality,
                preview.TellParam == 0 ? null : preview.TellParam,
                rule.Group == P4MechanicGroup.Flood
                    ? P4Flood.FormatInstruction(rule.Id, preview.WoundColor, floodSide, preview.Reality)
                    : "Preview only.",
                preview.WoundColor,
                floodSide));
        }

        return assignments;
    }

    private static List<P4DebuffAssignment> GetOrderedAssignments(IReadOnlyList<P4DebuffAssignment> assignments)
    {
        var ordered = assignments
            .Where(assignment => assignment.Entry.RemainingTime > 0.0f)
            .ToList();
        StableSortByTimerWithTolerance(ordered);
        return ordered;
    }

    private static List<GazeDisplayAssignment> GetRepresentativeGazeAssignments(IReadOnlyList<P4DebuffAssignment> assignments)
    {
        var ordered = assignments
            .Where(assignment => assignment.Entry.RemainingTime > 0.0f)
            .ToList();
        StableSortByTimerWithTolerance(ordered);

        var groups = new List<List<P4DebuffAssignment>>();
        foreach (var assignment in ordered)
        {
            if (groups.Count == 0 ||
                MathF.Abs(assignment.Entry.RemainingTime - groups[^1][0].Entry.RemainingTime) > GazeDuplicateTimerToleranceSeconds)
            {
                groups.Add([]);
            }

            groups[^1].Add(assignment);
        }

        if (groups.Count == 0)
        {
            return [];
        }

        var selectedGroups = groups.Count <= 2
            ? groups
            : [groups[0], groups[^1]];

        return selectedGroups
            .Select(CreateGazeDisplayAssignment)
            .ToList();
    }

    private static GazeDisplayAssignment CreateGazeDisplayAssignment(IReadOnlyList<P4DebuffAssignment> group)
    {
        var orderedGroup = group
            .OrderBy(assignment => assignment.Entry.PartyIndex)
            .ThenBy(assignment => assignment.Entry.MemberName, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var representative = orderedGroup
            .OrderBy(assignment => assignment.Reality == RealityState.Unknown ? 1 : 0)
            .ThenBy(assignment => assignment.Entry.PartyIndex)
            .ThenBy(assignment => assignment.Entry.MemberName, StringComparer.OrdinalIgnoreCase)
            .First();

        return new GazeDisplayAssignment(representative, orderedGroup);
    }

    private static void StableSortByTimerWithTolerance(List<P4DebuffAssignment> assignments)
    {
        for (var index = 1; index < assignments.Count; index++)
        {
            var current = assignments[index];
            var insertAt = index;
            while (insertAt > 0
                && current.Entry.RemainingTime + 1.0f < assignments[insertAt - 1].Entry.RemainingTime)
            {
                assignments[insertAt] = assignments[insertAt - 1];
                insertAt--;
            }

            assignments[insertAt] = current;
        }
    }

    private static void DrawSection(string label, IReadOnlyList<P4DebuffAssignment> assignments, string idSuffix)
    {
        ImGui.TextColored(GoldColor, label);
        var panelStart = ImGui.GetCursorScreenPos();
        var availableWidth = MathF.Max(160.0f, ImGui.GetContentRegionAvail().X);
        var panelWidth = availableWidth;
        var panelHeight = GetAssignmentPanelHeight(assignments, availableWidth);
        ImGui.GetWindowDrawList().AddRectFilled(
            panelStart,
            panelStart + new Vector2(panelWidth, panelHeight),
            ImGui.GetColorU32(PanelFillColor),
            6.0f);
        ImGui.GetWindowDrawList().AddRect(
            panelStart,
            panelStart + new Vector2(panelWidth, panelHeight),
            ImGui.GetColorU32(PanelBorderColor),
            6.0f);

        ImGui.SetCursorScreenPos(panelStart + new Vector2(HelperPadding, HelperPadding));
        DrawAssignmentStrip(assignments, availableWidth - HelperPadding * 2.0f, idSuffix);
        ImGui.SetCursorScreenPos(panelStart + new Vector2(0.0f, panelHeight + ImGui.GetStyle().ItemSpacing.Y));
    }

    private static void DrawGazeSection(string label, IReadOnlyList<GazeDisplayAssignment> assignments, string idSuffix)
    {
        ImGui.TextColored(GoldColor, label);
        var panelStart = ImGui.GetCursorScreenPos();
        var availableWidth = MathF.Max(160.0f, ImGui.GetContentRegionAvail().X);
        var panelHeight = GetGazePanelHeight(assignments.Count);
        ImGui.GetWindowDrawList().AddRectFilled(
            panelStart,
            panelStart + new Vector2(availableWidth, panelHeight),
            ImGui.GetColorU32(PanelFillColor),
            6.0f);
        ImGui.GetWindowDrawList().AddRect(
            panelStart,
            panelStart + new Vector2(availableWidth, panelHeight),
            ImGui.GetColorU32(PanelBorderColor),
            6.0f);

        ImGui.SetCursorScreenPos(panelStart + new Vector2(HelperPadding, HelperPadding));
        DrawGazeRows(assignments, availableWidth - HelperPadding * 2.0f, idSuffix);
        ImGui.SetCursorScreenPos(panelStart + new Vector2(0.0f, panelHeight + ImGui.GetStyle().ItemSpacing.Y));
    }

    private void DrawCallSection(string label, IReadOnlyList<P4Call> calls)
    {
        ImGui.TextColored(GoldColor, label);
        foreach (var set in calls.GroupBy(call => call.SetNumber))
        {
            DrawCallSetPanel(set.Select(call => new CallBlock(call.RemainingTime, call.Lines, call.Tooltip)).ToList());
        }
    }

    private void DrawCallSetPanel(IReadOnlyList<CallBlock> calls)
    {
        var style = ImGui.GetStyle();
        var mechanicGap = MathF.Round(ImGui.GetTextLineHeight() * 0.75f);
        var panelStart = ImGui.GetCursorScreenPos();
        var availableWidth = MathF.Max(160.0f, ImGui.GetContentRegionAvail().X);
        var lineCount = calls.Sum(call => call.Lines.Count);
        var panelHeight = HelperPadding * 2.0f
            + lineCount * ImGui.GetTextLineHeight()
            + MathF.Max(0, lineCount - calls.Count) * style.ItemSpacing.Y
            + MathF.Max(0, calls.Count - 1) * mechanicGap;
        ImGui.GetWindowDrawList().AddRectFilled(
            panelStart,
            panelStart + new Vector2(availableWidth, panelHeight),
            ImGui.GetColorU32(PanelFillColor),
            6.0f);
        ImGui.GetWindowDrawList().AddRect(
            panelStart,
            panelStart + new Vector2(availableWidth, panelHeight),
            ImGui.GetColorU32(PanelBorderColor),
            6.0f);

        var contentLeft = panelStart.X + HelperPadding;
        var contentRight = panelStart.X + availableWidth - HelperPadding;
        var cursorY = panelStart.Y + HelperPadding;
        for (var callIndex = 0; callIndex < calls.Count; callIndex++)
        {
            var call = calls[callIndex];
            if (callIndex > 0)
            {
                var dividerY = MathF.Round(cursorY + mechanicGap * 0.5f);
                ImGui.GetWindowDrawList().AddLine(
                    new Vector2(contentLeft, dividerY),
                    new Vector2(contentRight, dividerY),
                    ImGui.GetColorU32(CallDividerColor));
                cursorY += mechanicGap;
            }

            for (var lineIndex = 0; lineIndex < call.Lines.Count; lineIndex++)
            {
                ImGui.SetCursorScreenPos(new Vector2(contentLeft, cursorY));
                ImGui.BeginGroup();
                DrawCallLine(call.Lines[lineIndex]);
                ImGui.EndGroup();
                if (ImGui.IsItemHovered())
                {
                    ImGui.SetTooltip(call.Tooltip);
                }

                if (lineIndex == 0)
                {
                    var timerText = FormatRemainingTime(call.RemainingTime);
                    ImGui.SetCursorScreenPos(new Vector2(contentRight - ImGui.CalcTextSize(timerText).X, cursorY));
                    ImGui.TextColored(TimerTextColor, timerText);
                }

                cursorY += ImGui.GetTextLineHeight();
                if (lineIndex < call.Lines.Count - 1)
                {
                    cursorY += style.ItemSpacing.Y;
                }
            }
        }

        ImGui.SetCursorScreenPos(panelStart + new Vector2(0.0f, panelHeight + style.ItemSpacing.Y));
    }

    private void DrawCallLine(P4CallLine line)
    {
        var spacing = ImGui.GetStyle().ItemSpacing.X * 0.5f;
        for (var index = 0; index < line.Segments.Count; index++)
        {
            var segment = line.Segments[index];
            if (index > 0)
            {
                ImGui.SameLine(0.0f, spacing);
            }

            if (segment.StatusId != 0 && DrawInlineStatusIcon(segment.StatusId))
            {
                ImGui.SameLine(0.0f, spacing);
            }

            if (segment.Players is not null)
            {
                DrawCallPlayers(segment.Players);
            }
            else if (segment.Tone == P4CallTone.Muted)
            {
                ImGui.TextDisabled(segment.Text);
            }
            else
            {
                ImGui.TextColored(GetCallToneColor(segment.Tone), segment.Text);
            }
        }
    }

    private void DrawCallPlayers(IReadOnlyList<P4CallPlayer> players)
    {
        var display = plugin.Configuration.CallsPlayerDisplay;
        if (display != P4CallPlayerDisplay.JobIcon)
        {
            var names = players.Select(player => display == P4CallPlayerDisplay.FirstName ? GetFirstName(player.Name) : player.Name);
            ImGui.TextColored(TimerTextColor, string.Join(", ", names));
            return;
        }

        var iconSize = ImGui.GetTextLineHeight();
        for (var index = 0; index < players.Count; index++)
        {
            if (index > 0)
            {
                ImGui.SameLine(0.0f, ImGui.GetStyle().ItemSpacing.X * 0.35f);
            }

            DrawClassJobIcon(players[index].ClassJobId, iconSize, players[index].Name);
        }
    }

    private static string GetFirstName(string name)
    {
        var spaceIndex = name.IndexOf(' ');
        return spaceIndex > 0 ? name[..spaceIndex] : name;
    }

    private bool DrawInlineStatusIcon(uint statusId)
    {
        var iconId = plugin.GetStatusIconId(statusId);
        if (iconId == 0)
        {
            return false;
        }

        var wrap = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId)).GetWrapOrDefault();
        if (wrap is null)
        {
            return false;
        }

        var height = ImGui.GetTextLineHeight();
        var width = wrap.Height > 0 ? height * wrap.Width / wrap.Height : height;
        ImGui.Image(wrap.Handle, new Vector2(width, height));
        return true;
    }

    private static Vector4 GetCallToneColor(P4CallTone tone)
    {
        return tone switch
        {
            P4CallTone.Water => WaterCallColor,
            P4CallTone.Lightning => GoldColor,
            P4CallTone.LookAway => PurpleSideColor,
            P4CallTone.LookAt => GoldColor,
            P4CallTone.Donut => DonutCallColor,
            P4CallTone.Twister => TwisterCallColor,
            P4CallTone.Stop => StopCallColor,
            P4CallTone.Stack => RealColor,
            P4CallTone.Spread => PurpleSideColor,
            P4CallTone.In => DonutCallColor,
            P4CallTone.Out => StopCallColor,
            P4CallTone.Ice => WaterCallColor,
            P4CallTone.Rocks => TwisterCallColor,
            P4CallTone.Direction => GoldColor,
            P4CallTone.FloodBlue => BlueSideColor,
            P4CallTone.FloodPurple => PurpleSideColor,
            P4CallTone.Circle => PurpleSideColor,
            P4CallTone.Cone => TwisterCallColor,
            P4CallTone.Motion => RealColor,
            _ => TimerTextColor,
        };
    }

    private static float GetGazePanelHeight(int assignmentCount)
    {
        var rows = Math.Max(1, assignmentCount);
        var style = ImGui.GetStyle();
        var rowHeight = MathF.Max(AssignmentIconSize, ImGui.GetTextLineHeight() * 2.0f + style.ItemSpacing.Y);
        return HelperPadding * 2.0f + rows * rowHeight + MathF.Max(0, rows - 1) * style.ItemSpacing.Y;
    }

    private static void DrawGazeRows(IReadOnlyList<GazeDisplayAssignment> assignments, float availableWidth, string idSuffix)
    {
        if (assignments.Count == 0)
        {
            ImGui.TextDisabled("Waiting for gazes.");
            return;
        }

        if (!ImGui.BeginTable(
            $"##P4GazeAssignments{idSuffix}",
            4,
            ImGuiTableFlags.SizingStretchProp,
            new Vector2(availableWidth, 0.0f)))
        {
            return;
        }

        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, AssignmentIconSize + ImGui.GetStyle().ItemSpacing.X);
        ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthStretch, 1.2f);
        ImGui.TableSetupColumn("Call", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Timer", ImGuiTableColumnFlags.WidthFixed, ImGui.CalcTextSize("99.9s").X + 8.0f);

        for (var index = 0; index < assignments.Count; index++)
        {
            var displayAssignment = assignments[index];
            var assignment = displayAssignment.Representative;
            var tooltip = FormatGazeTooltip(displayAssignment, index, assignments.Count);
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawStatusIconWithBorder(
                assignment.Entry.IconId,
                AssignmentIconSize,
                GetRealityColor(assignment.Reality),
                tooltip);

            ImGui.TableNextColumn();
            DrawGazePlayerColumn(displayAssignment, index, assignments.Count, tooltip);

            ImGui.TableNextColumn();
            DrawGazeCallColumn(assignment, tooltip);

            ImGui.TableNextColumn();
            ImGui.TextColored(TimerTextColor, FormatRemainingTime(assignment.Entry.RemainingTime));
            if (ImGui.IsItemHovered())
            {
                ImGui.SetTooltip(tooltip);
            }
        }

        ImGui.EndTable();
    }

    private static void DrawGazePlayerColumn(GazeDisplayAssignment assignment, int index, int assignmentCount, string tooltip)
    {
        ImGui.TextUnformatted(GetGazeTimingLabel(index, assignmentCount));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        var playerText = assignment.Assignments.Count == 1
            ? assignment.Representative.Entry.MemberName
            : $"{assignment.Assignments.Count} players";
        ImGui.TextDisabled(playerText);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static void DrawGazeCallColumn(P4DebuffAssignment assignment, string tooltip)
    {
        var call = assignment.Reality == RealityState.Unknown
            ? "Unknown"
            : $"{FormatReality(assignment.Reality)}: {GetResolutionLabel(assignment)}";
        ImGui.TextColored(GetRealityColor(assignment.Reality), call);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }

        ImGui.TextDisabled("Cursed Shriek");
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static float GetAssignmentPanelHeight(IReadOnlyList<P4DebuffAssignment> assignments, float availableWidth)
    {
        if (assignments.Count == 0)
        {
            return AssignmentIconSize + HelperPadding * 2.0f;
        }

        var rowHeight = GetAssignmentHeight();
        var spacingX = ImGui.GetStyle().ItemSpacing.X * 1.25f;
        var spacingY = ImGui.GetStyle().ItemSpacing.Y;
        var contentWidth = MathF.Max(1.0f, availableWidth - HelperPadding * 2.0f);
        var rowWidth = 0.0f;
        var rows = 1;
        foreach (var assignment in assignments)
        {
            var itemWidth = GetAssignmentWidth(assignment);
            if (rowWidth > 0.0f && rowWidth + spacingX + itemWidth > contentWidth)
            {
                rows++;
                rowWidth = itemWidth;
            }
            else
            {
                rowWidth += rowWidth > 0.0f ? spacingX + itemWidth : itemWidth;
            }
        }

        return HelperPadding * 2.0f + rows * rowHeight + MathF.Max(0, rows - 1) * spacingY;
    }

    private static void DrawAssignmentStrip(IReadOnlyList<P4DebuffAssignment> assignments, float availableWidth, string idSuffix)
    {
        if (assignments.Count == 0)
        {
            ImGui.TextDisabled("-");
            return;
        }

        var spacing = ImGui.GetStyle().ItemSpacing.X * 1.25f;
        var rowWidth = 0.0f;
        for (var index = 0; index < assignments.Count; index++)
        {
            var assignment = assignments[index];
            var itemWidth = GetAssignmentWidth(assignment);
            if (rowWidth > 0.0f && rowWidth + spacing + itemWidth > availableWidth)
            {
                rowWidth = 0.0f;
            }
            else if (rowWidth > 0.0f)
            {
                ImGui.SameLine(0.0f, spacing);
                rowWidth += spacing;
            }

            ImGui.PushID($"{idSuffix}{assignment.Entry.Key}{index}");
            DrawAssignment(assignment, itemWidth);
            ImGui.PopID();
            rowWidth += itemWidth;
        }
    }

    private static void DrawAssignment(P4DebuffAssignment assignment, float width)
    {
        var label = GetRealityLine(assignment);
        var timerText = FormatRemainingTime(assignment.Entry.RemainingTime);
        var labelSize = ImGui.CalcTextSize(label);
        var timerSize = ImGui.CalcTextSize(timerText);
        var start = ImGui.GetCursorPos();
        var labelColor = GetAssignmentLabelColor(assignment);
        var borderColor = assignment.FloodSide != FloodSide.None
            ? GetFloodSideColor(assignment.FloodSide)
            : assignment.Reality == RealityState.Fake
                ? FakeColor
                : assignment.Reality == RealityState.Real
                    ? GoldColor
                    : UnknownColor;

        ImGui.BeginGroup();
        ImGui.SetCursorPosX(start.X + MathF.Max(0.0f, (width - labelSize.X) * 0.5f));
        ImGui.TextColored(labelColor, label);
        ImGui.SetCursorPosX(start.X + MathF.Max(0.0f, (width - AssignmentIconSize) * 0.5f));
        DrawStatusIconWithBorder(assignment.Entry.IconId, AssignmentIconSize, borderColor, FormatAssignmentTooltip(assignment));
        ImGui.SetCursorPosX(start.X + MathF.Max(0.0f, (width - timerSize.X) * 0.5f));
        ImGui.TextColored(TimerTextColor, timerText);
        ImGui.EndGroup();

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(FormatAssignmentTooltip(assignment));
        }
    }

    private static void DrawStatusIconWithBorder(uint iconId, float iconSize, Vector4 borderColor, string tooltip)
    {
        var size = new Vector2(iconSize, iconSize);
        var start = ImGui.GetCursorScreenPos();
        if (iconId == 0)
        {
            ImGui.Dummy(size);
        }
        else
        {
            try
            {
                var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
                var wrap = texture.GetWrapOrDefault();
                if (wrap is null)
                {
                    ImGui.Dummy(size);
                }
                else
                {
                    ImGui.Image(wrap.Handle, size);
                }
            }
            catch
            {
                ImGui.Dummy(size);
            }
        }

        ImGui.GetWindowDrawList().AddRect(
            start - new Vector2(1.0f),
            start + size + new Vector2(1.0f),
            ImGui.GetColorU32(borderColor),
            4.0f,
            ImDrawFlags.None,
            2.0f);

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private static float GetAssignmentWidth(P4DebuffAssignment assignment)
    {
        var label = GetRealityLine(assignment);
        var timerText = FormatRemainingTime(assignment.Entry.RemainingTime);
        return MathF.Max(
            AssignmentIconSize,
            MathF.Max(ImGui.CalcTextSize(label).X, ImGui.CalcTextSize(timerText).X)) + 8.0f;
    }

    private static float GetAssignmentHeight()
    {
        return ImGui.GetTextLineHeight() * 2.0f + AssignmentIconSize + ImGui.GetStyle().ItemSpacing.Y * 2.0f;
    }

    private void DrawStatusIcon(uint statusId, string tooltip)
    {
        var iconId = plugin.GetStatusIconId(statusId);
        if (iconId == 0)
        {
            ImGui.TextUnformatted(tooltip);
            return;
        }

        var iconScale = Math.Clamp(plugin.Configuration.HelperIconScale, 0.75f, 3.0f);
        var iconSize = new Vector2(ImGui.GetTextLineHeight() * 1.45f * iconScale);
        var texture = Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(iconId));
        var wrap = texture.GetWrapOrDefault();
        if (wrap is null)
        {
            ImGui.TextUnformatted(tooltip);
            return;
        }

        ImGui.Image(wrap.Handle, iconSize);
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip(tooltip);
        }
    }

    private float GetHelperBackgroundOpacity()
    {
        return Math.Clamp(plugin.Configuration.HelperBackgroundOpacity, 0.15f, 1.0f);
    }

    private static string FormatAssignmentTooltip(P4DebuffAssignment assignment)
    {
        var timerText = FormatRemainingTime(assignment.Entry.RemainingTime);
        var realityLine = assignment.Rule.Group != P4MechanicGroup.Flood && assignment.Reality == RealityState.Unknown
            ? "Tell not captured."
            : GetRealityLine(assignment);
        return $"{assignment.Entry.MemberName}\n{assignment.Rule.Name}\nTimer: {timerText}\n{realityLine}\n{assignment.Instruction}";
    }

    private static string FormatGazeTooltip(GazeDisplayAssignment displayAssignment, int index, int assignmentCount)
    {
        var assignment = displayAssignment.Representative;
        var timerText = FormatRemainingTime(assignment.Entry.RemainingTime);
        var timingLabel = GetGazeTimingLabel(index, assignmentCount);
        var playerLine = displayAssignment.Assignments.Count == 1
            ? displayAssignment.Representative.Entry.MemberName
            : $"Players: {string.Join(", ", displayAssignment.Assignments.Select(assignment => assignment.Entry.MemberName))}";
        var realityLine = assignment.Reality == RealityState.Unknown
            ? "Tell not captured."
            : $"{FormatReality(assignment.Reality)}: {GetResolutionLabel(assignment)}";
        return $"{timingLabel}\n{playerLine}\n{assignment.Rule.Name}\nTimer: {timerText}\n{realityLine}\n{assignment.Instruction}";
    }

    private static string GetGazeTimingLabel(int index, int assignmentCount)
    {
        if (assignmentCount < 2)
        {
            return "Gaze";
        }

        return index == 0 ? "Short gaze" : "Long gaze";
    }

    private static string GetRealityLine(P4DebuffAssignment assignment)
    {
        var destinationWound = P4Flood.ResolveDestinationWound(assignment.Rule.Id, assignment.WoundColor, assignment.Reality);
        if (destinationWound == WoundColor.None)
        {
            destinationWound = P4Flood.GetWoundColorForSide(assignment.FloodSide);
        }

        if (destinationWound != WoundColor.None)
        {
            return $"Go {P4Flood.FormatWound(destinationWound)}";
        }

        var statusWound = P4Flood.GetStatusWoundColor(assignment.Rule.Id);
        if (statusWound != WoundColor.None)
        {
            return P4Flood.FormatWoundDebuff(statusWound);
        }

        if (assignment.Rule.Group == P4MechanicGroup.Flood)
        {
            return assignment.Rule.Id switch
            {
                P4Flood.AllaganFieldStatusId => "Allagan Field",
                P4Flood.BeyondDeath1StatusId => "Opposite Wound",
                P4Flood.BeyondDeath2StatusId => "Same Wound",
                P4Flood.BlackWound1StatusId or P4Flood.BlackWound2StatusId => "Black Wound",
                P4Flood.WhiteWound1StatusId or P4Flood.WhiteWound2StatusId => "White Wound",
                _ => "Flood",
            };
        }

        return assignment.Reality switch
        {
            RealityState.Real => $"Real: {GetResolutionLabel(assignment)}",
            RealityState.Fake => $"Fake: {GetResolutionLabel(assignment)}",
            _ => "Unknown",
        };
    }

    private static string GetResolutionLabel(P4DebuffAssignment assignment)
    {
        return assignment.Rule.Id switch
        {
            5545 => assignment.Reality == RealityState.Real ? "Stack" : "Spread",
            5544 => assignment.Reality == RealityState.Real ? "Spread" : "Stack",
            5543 => assignment.Reality == RealityState.Real ? "Look away" : "Look toward",
            5546 => assignment.Reality == RealityState.Real ? "Stop" : "Move",
            5548 => assignment.Reality == RealityState.Real ? "Donut" : "Point-blank",
            5547 => assignment.Reality == RealityState.Real ? "Point-blank" : "Donut",
            _ => FormatReality(assignment.Reality),
        };
    }

    private static string FormatRemainingTime(float remainingTime)
    {
        return remainingTime > 0.0f ? $"{remainingTime:0.0}s" : "-";
    }

    private static string FormatLimitCutRotation(P3LimitCutRotation rotation)
    {
        return rotation switch
        {
            P3LimitCutRotation.Clockwise => "CW",
            P3LimitCutRotation.CounterClockwise => "CCW",
            _ => "Waiting",
        };
    }

    private static string FormatLimitCutNorth(P3LimitCutState state)
    {
        var direction = string.IsNullOrWhiteSpace(state.NewNorth) ? "Unknown" : state.NewNorth;
        return string.IsNullOrWhiteSpace(state.NewNorthMarker)
            ? direction
            : $"{state.NewNorthMarker} ({direction})";
    }

    private static Vector4 GetLimitCutRotationColor(P3LimitCutRotation rotation)
    {
        return rotation == P3LimitCutRotation.Unknown ? UnknownColor : RealColor;
    }

    private static Vector4 GetRealityColor(RealityState reality)
    {
        return reality switch
        {
            RealityState.Real => RealColor,
            RealityState.Fake => FakeColor,
            _ => UnknownColor,
        };
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

    private static Vector4 GetAssignmentLabelColor(P4DebuffAssignment assignment)
    {
        if (assignment.FloodSide != FloodSide.None)
        {
            return GetFloodSideColor(assignment.FloodSide);
        }

        var displayedWoundSide = P4Flood.GetWoundSide(P4Flood.GetStatusWoundColor(assignment.Rule.Id));
        if (displayedWoundSide != FloodSide.None)
        {
            return GetFloodSideColor(displayedWoundSide);
        }

        var woundSide = P4Flood.GetWoundSide(assignment.WoundColor);
        return woundSide != FloodSide.None
            ? GetFloodSideColor(woundSide)
            : GetRealityColor(assignment.Reality);
    }

    private static Vector4 GetFloodSideColor(FloodSide side)
    {
        return side switch
        {
            FloodSide.Blue => BlueSideColor,
            FloodSide.Purple => PurpleSideColor,
            _ => UnknownColor,
        };
    }

    private static uint GetLineStatusId(P3.LineGroup lineGroup)
    {
        return lineGroup switch
        {
            P3.LineGroup.First => 3004,
            P3.LineGroup.Second => 3005,
            P3.LineGroup.Third => 3006,
            _ => 0,
        };
    }

    private static string GetLineName(uint statusId)
    {
        return statusId switch
        {
            3004 => "First in Line",
            3005 => "Second in Line",
            3006 => "Third in Line",
            _ => "No line debuff",
        };
    }

    private sealed record P3PreviewRole(P3.LineGroup LineGroup, bool IsDps, bool HadAccretion, uint ClassJobId, string Label);

    private sealed record GazeDisplayAssignment(
        P4DebuffAssignment Representative,
        IReadOnlyList<P4DebuffAssignment> Assignments);
}
