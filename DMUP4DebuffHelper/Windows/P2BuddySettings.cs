using Dalamud.Bindings.ImGui;
using System;
using System.Linq;

namespace DMUP4DebuffHelper.Windows;

// P2 buddy controls shared by the settings window and the P2 helper view.
internal static class P2BuddySettings
{
    public static void DrawBuddyCombo(Plugin plugin)
    {
        var configuration = plugin.Configuration;

        // Offer the current party, plus the saved buddy if they are not in the party right now.
        var names = Plugin.PartyList
            .Select(member => member.Name.TextValue)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .ToList();
        var localName = Plugin.ObjectTable.LocalPlayer?.Name.TextValue;
        names.RemoveAll(name => string.Equals(name, localName, StringComparison.OrdinalIgnoreCase));
        if (!string.IsNullOrWhiteSpace(configuration.P2BuddyName) && !names.Contains(configuration.P2BuddyName))
        {
            names.Insert(0, configuration.P2BuddyName);
        }

        ImGui.SetNextItemWidth(180.0f);
        var preview = string.IsNullOrWhiteSpace(configuration.P2BuddyName) ? "(none)" : configuration.P2BuddyName;
        if (!ImGui.BeginCombo("P2 buddy", preview))
        {
            return;
        }

        if (ImGui.Selectable("(none)", string.IsNullOrWhiteSpace(configuration.P2BuddyName)))
        {
            configuration.P2BuddyName = string.Empty;
            plugin.SaveConfiguration();
        }

        foreach (var name in names)
        {
            var isSelected = string.Equals(name, configuration.P2BuddyName, StringComparison.OrdinalIgnoreCase);
            if (ImGui.Selectable(name, isSelected))
            {
                configuration.P2BuddyName = name;
                plugin.SaveConfiguration();
            }

            if (isSelected)
            {
                ImGui.SetItemDefaultFocus();
            }
        }

        ImGui.EndCombo();
    }

    public static void DrawRangedCheckbox(Plugin plugin)
    {
        var rangedNoFlex = plugin.Configuration.P2RangedNoFlex;
        if (ImGui.Checkbox("I'm ranged, no flex", ref rangedNoFlex))
        {
            plugin.Configuration.P2RangedNoFlex = rangedNoFlex;
            plugin.SaveConfiguration();
        }

        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("Hides the buddy line. Your soak group is worked out from the melee players' markers instead,\nand the buddy picker only appears when exactly one melee has the stack.");
        }
    }
}
