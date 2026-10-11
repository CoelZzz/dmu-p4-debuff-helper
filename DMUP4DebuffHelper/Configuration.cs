using Dalamud.Configuration;
using System;
using DMUP3BlackholeHelper;

namespace DMUP4DebuffHelper;

[Serializable]
public sealed class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 4;

    // Retained for migrating configurations saved before the instance-only option.
    public bool ShowHelper { get; set; } = true;

    public bool OnlyShowInInstance { get; set; }

    public bool PreviewWhenInactive { get; set; }

    public bool HelperCollapsed { get; set; }

    public BlackHoleStrategyKind SelectedBlackHoleStrategy { get; set; } = BlackHoleStrategyKind.Standard;

    public float HelperFontScale { get; set; } = 1.0f;

    public float HelperIconScale { get; set; } = 1.0f;

    public float HelperBackgroundOpacity { get; set; } = 1.0f;

    public bool ShowP1Calls { get; set; } = true;

    public bool ShowP2Calls { get; set; } = true;

    // Character name of the P2 Forsaken buddy (MT/H1, OT/H2, M1/R1, M2/R2).
    public string P2BuddyName { get; set; } = string.Empty;

    // Ranged players don't flex, so they skip the buddy line and only need a buddy when the soak group is ambiguous.
    public bool P2RangedNoFlex { get; set; }

    public bool ShowP4Active { get; set; } = true;

    public bool ShowP4Calls { get; set; } = true;

    public bool ShowP4Gazes { get; set; } = true;

    public bool CallsShowStopOnly { get; set; }

    public P4CallPlayerDisplay CallsPlayerDisplay { get; set; } = P4CallPlayerDisplay.FullName;

    public bool DiscoveryLogging { get; set; }

    public void Save()
    {
        Plugin.PluginInterface.SavePluginConfig(this);
    }
}
