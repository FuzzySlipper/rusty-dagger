using Rusty.Engine.Entities;
using Rusty.Engine.Mechanics;
using WorldRpg.Kit;
using WorldRpg.Kit.Inventory;
using WorldRpg.Kit.World;
using UniqueInventoryItem = WorldRpg.Kit.Inventory.UniqueInventoryItem;
using EquipmentSlotId = WorldRpg.Kit.Inventory.EquipmentSlotId;
using WorldRpg.Rulesets.Daggerfall.Content;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private readonly DaggerfallSessionComposition _composition;
    private bool _newGameInitialized;
    public bool RequiresCharacterInitialization => !_newGameInitialized;

    public bool HasCommittedCharacter => !_newGameInitialized && State.Character.Pending is null && State.Character.Background is not null;

    public void OpenCharacterCreation()
    {
        if (_newGameInitialized || State.Character.Pending is not null) return;
        State.Character.BeginFreshChoices(_random);
        Presentation.SetOutcome("Character choices opened.");
    }

    public IGameSession CreateNewGame()
    {
        if (_newGameInitialized || State.Character.Pending is not null || State.Character.Background is null)
            throw new ArgumentException("Commit a complete character before beginning the new game.");
        var character = State.Character.Capture();
        var choices = new DaggerfallCharacterCreationChoices(character.Name, character.RaceId, character.Gender,
            character.FaceIndex, character.Reflexes, character.CareerId, character.CustomCareer, character.Background);
        // Resolve and create the whole loadout before replacing the title session. The bundle's
        // existing start site constructs the world; a load restores its current inventory instead.
        var factory = new DaggerfallItemFactory(_definitions, _random);
        var grants = NewGameItems(choices, State.Character.Career).Select((request, index) =>
            (Item: factory.Create(request), Slot: request.Key.EndsWith(".shirt", StringComparison.Ordinal)
                ? "chest-clothes" : request.Key.EndsWith(".pants", StringComparison.Ordinal) ? "legs-clothes" : null)).ToArray();
        string[] spells = NewGameSpells(State.Character.Career, character.CustomCareer is not null);
        foreach (string spell in spells) _ = _definitions.Magic.Spells[spell];
        var replacement = StartNew(_engine, _composition);
        try
        {
            replacement.State.Character.ReplacePending(choices);
            replacement.State.Character.CommitChoices();
            replacement.ClearInitialPlayerLoadout();
            for (int index = 0; index < grants.Length; index++)
            {
                var grant = grants[index];
                DurableIdentityReference? identity = grant.Item.Stackable ? null : replacement._uniqueItems.AllocateReference();
                factory.Materialize(grant.Item, replacement.State.Inventory, replacement.State.ItemInstances,
                    grant.Item.Stackable ? InventoryStackId.Parse($"daggerfall.character.initial.{index}") : null, identity);
                if (grant.Slot is { } slot)
                {
                    var equipped = replacement.EquipmentMoves.MoveToSlot(new UniqueInventoryItem(
                        replacement.State.Actors.Entities.Resolve(identity!.Value).Value, grant.Item.Item), new EquipmentSlotId(slot));
                    if (equipped.Outcome != EquipmentMoveOutcome.Applied)
                        throw new ArgumentException($"Starting clothing could not be equipped: {equipped.Detail}");
                }
            }
            replacement.ApplyBackground(character.Background!);
            foreach (string spell in spells) replacement.State.Character.LearnSpell(spell);
            foreach (var (_, track) in replacement.State.Actors.Player.Stats.Tracks)
                track.SetCurrent(track.Maximum.Value, clamp: true);
            replacement._newGameInitialized = true;
            replacement.Presentation.SetOutcome($"New game initialized for {character.Name}.");
            return replacement;
        }
        catch { replacement.Dispose(); throw; }
    }

    private IEnumerable<DaggerfallItemCreateRequest> NewGameItems(DaggerfallCharacterCreationChoices choices,
        DaggerfallCareerDefinition career)
    {
        var config = _definitions.NewGame;
        bool female = choices.Gender == DaggerfallCharacterGender.Female;
        string gender = female ? "female" : "male";
        string clothing = female ? "WomensClothing" : "MensClothing";
        yield return new("MiscItems", "new-game.spellbook", DaggerfallItemOwner.Player, TemplateIndex: config.SpellbookTemplate);
        yield return new(clothing, "new-game.shirt", DaggerfallItemOwner.Player,
            TemplateIndex: female ? config.FemaleShirtTemplate : config.MaleShirtTemplate, Variant: 0, Race: choices.RaceId, Gender: gender, RandomizeClothingDye: true);
        yield return new(clothing, "new-game.pants", DaggerfallItemOwner.Player,
            TemplateIndex: female ? config.FemalePantsTemplate : config.MalePantsTemplate, Race: choices.RaceId, Gender: gender);
        var items = choices.CustomCareer is not null ? config.CustomItems : config.Careers.Single(value => value.Career == career.Id).Items;
        foreach (var (item, index) in items.Select((item, index) => (item, index)))
            yield return new("Weapons", $"new-game.weapon.{index}", DaggerfallItemOwner.Player,
                Quantity: item.Quantity, TemplateIndex: item.Template, Material: item.Material);
        if (config.Gold > 0) yield return new("Currency", "new-game.gold", DaggerfallItemOwner.Player, Quantity: (ulong)config.Gold, TemplateIndex: 276);
        foreach (var (grant, index) in choices.Background!.StartingGrants.Select((grant, index) => (grant, index)))
        {
            var definition = _definitions.RequireItem(new DaggerfallItemId(grant.ItemId));
            var template = definition.Template ?? throw new ArgumentException($"Starting grant '{grant.ItemId}' has no normalized template.");
            bool appearance = template.Groups.Any(group => group is "Armor" or "MensClothing" or "WomensClothing");
            yield return new(template.Groups[0], $"new-game.background.{index}", DaggerfallItemOwner.Player,
                Quantity: grant.Quantity, TemplateIndex: template.Index, Material: definition.Weapon?.Material ?? definition.Armor?.Material,
                Race: appearance ? choices.RaceId : null, Gender: appearance ? gender : null);
        }
    }

    private string[] NewGameSpells(DaggerfallCareerDefinition career, bool custom) => custom
        ? career.PrimarySkills.Concat(career.MajorSkills).Any(skill => skill is
            "destruction" or "restoration" or "illusion" or "alteration" or "thaumaturgy" or "mysticism")
            ? _definitions.NewGame.CustomMagicSpells : []
        : _definitions.NewGame.Careers.Single(value => value.Career == career.Id).Spells;

    private void ClearInitialPlayerLoadout()
    {
        var inventory = State.Inventory.Read();
        foreach (var item in inventory.UniqueItems)
        {
            var unique = new UniqueInventoryItem(item.Entity.Value, new InventoryItemId(item.Definition.Value));
            var identity = State.Inventory.GetDurableItemId(item.Entity);
            if (State.Equipment.Read().Assignments.Any(assignment => assignment.Item.EntityId == unique.EntityId))
                State.Equipment.Unequip(unique);
            State.Inventory.Destroy(unique);
            State.ItemInstances.RemoveUnique(identity.Value);
            State.Actors.Entities.Destroy(identity);
        }
        foreach (var item in inventory.Stacks)
        {
            State.Inventory.Consume(new InventoryConsume(item.Id, item.Quantity));
            State.ItemInstances.RemoveStack(DaggerfallItemOwner.Player, item.Id);
        }
    }
}
