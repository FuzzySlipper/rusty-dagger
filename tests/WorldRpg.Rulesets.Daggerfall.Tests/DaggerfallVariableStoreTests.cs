using WorldRpg.Rulesets.Daggerfall;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Scoped variables: identical keys in different scopes, donor global names, guarded mutation
/// and save round trips.
/// </summary>
public sealed class DaggerfallVariableStoreTests
{
    [Fact]
    public void Scopes_hold_identical_keys_with_defaults_and_guarded_mutation()
    {
        DaggerfallVariableStore variables = Create();

        // Untouched variables read false in every scope.
        Assert.False(variables.Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Global, 0, 0)));
        Assert.False(variables.Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Region, 17, 3)));
        Assert.False(variables.Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Faction, 40, 3)));

        // The donor's global names resolve to their keys.
        Assert.Equal(64, Tables().Globals.Rows.Select(row => row.Id).Distinct().Count());
        Assert.False(variables.ReadGlobal("LiftedCurse"));
        Assert.True(variables.WriteGlobal("LiftedCurse", true));
        Assert.True(variables.ReadGlobal("LiftedCurse"));
        Assert.False(variables.WriteGlobal("LiftedCurse", true));
        Assert.Throws<ArgumentOutOfRangeException>(() => variables.ReadGlobal("NoSuchGlobal"));

        // Identical numeric keys in different scopes are different variables.
        DaggerfallVariableAddress region = new(DaggerfallVariableScope.Region, 17, 3);
        DaggerfallVariableAddress faction = new(DaggerfallVariableScope.Faction, 40, 3);
        Assert.True(variables.Write(region, true));
        Assert.True(variables.Read(region));
        Assert.False(variables.Read(faction));
        Assert.False(variables.Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Region, 18, 3)));

        // Unguarded addresses are refused rather than stored.
        Assert.Throws<ArgumentOutOfRangeException>(() => variables.Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Global, 0, 64)));
        Assert.Throws<ArgumentOutOfRangeException>(() => variables.Write(new DaggerfallVariableAddress(DaggerfallVariableScope.Region, 62, 0), true));
        Assert.Throws<ArgumentOutOfRangeException>(() => variables.Write(new DaggerfallVariableAddress(DaggerfallVariableScope.Faction, -1, 0), true));
        Assert.Throws<ArgumentOutOfRangeException>(() => variables.Write(new DaggerfallVariableAddress((DaggerfallVariableScope)99, 0, 0), true));
    }

    [Fact]
    public void Written_variables_round_trip_through_save_records()
    {
        DaggerfallVariableStore variables = Create();
        variables.WriteGlobal("GothrydGotTotem", true);
        variables.Write(new DaggerfallVariableAddress(DaggerfallVariableScope.Region, 17, 3), true);
        variables.Write(new DaggerfallVariableAddress(DaggerfallVariableScope.Faction, 40, 3), true);

        DaggerfallVariablesSave saved = new([.. variables.Capture().Select(entry =>
            new DaggerfallVariableSave((int)entry.Address.Scope, entry.Address.Owner, entry.Address.Key, entry.Value))]);
        DaggerfallVariableStore restored = Create();
        restored.Restore(saved.Entries.Select(entry => (entry.Require(), entry.Value)));

        Assert.True(restored.ReadGlobal("GothrydGotTotem"));
        Assert.True(restored.Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Region, 17, 3)));
        Assert.True(restored.Read(new DaggerfallVariableAddress(DaggerfallVariableScope.Faction, 40, 3)));
        Assert.False(restored.ReadGlobal("LiftedCurse"));

        // A save that names no scope is refused on restore.
        Assert.Throws<ArgumentOutOfRangeException>(() => restored.Restore([(new DaggerfallVariableAddress((DaggerfallVariableScope)99, 0, 0), true)]));
    }

    [Fact]
    public void Saved_variables_validate_at_payload_read()
    {
        // A malformed variables section names itself at read rather than dying unnamed in the factory.
        DaggerfallVariablesSave empty = new([]);
        empty.Validate();
        Assert.Throws<ArgumentNullException>(() => new DaggerfallVariablesSave(null!).Validate());
    }
    private static DaggerfallVariableStore Create() => new(Tables().Globals.Lookup);

    private static WorldRpg.Rulesets.Daggerfall.Content.DaggerfallQuestTables Tables()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "AGENTS.md"))) root = root.Parent;
        return TestPayload.Definitions.QuestSources.Tables;
    }

    [Fact]
    public void Published_aliases_share_saved_numeric_slots()
    {
        DaggerfallVariableStore variables = Create();
        variables.WriteGlobal("TookTheCure", true);
        variables.WriteGlobal("OpenedShapeshifters", true);
        Assert.True(variables.ReadGlobal("Unused1"));
        Assert.True(variables.ReadGlobal("Unused2"));
        DaggerfallVariableStore restored = Create();
        restored.Restore(variables.Capture());
        Assert.True(restored.ReadGlobal("TookTheCure"));
        Assert.True(restored.ReadGlobal("OpenedShapeshifters"));
        Assert.Equal(new[] { 5, 10 }, restored.Capture().Select(entry => entry.Address.Key));
    }
}
