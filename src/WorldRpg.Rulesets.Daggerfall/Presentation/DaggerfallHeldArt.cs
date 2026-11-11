using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using Rusty.Engine;
using Rusty.Engine.FirstPerson;
using WorldRpg.Rulesets.Daggerfall.Modules.Combat;
using WorldRpg.Kit;
using WorldRpg.Kit.Presentation;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Policies;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

/// <summary>
/// The 3D first-person art: the authored held definition in the product's content (<see cref="DocumentPath"/>) drawn
/// with the models in the optional container installed beside the product (<see cref="ContainerName"/>): rigged arms
/// carrying their clips and the held weapons that replace the classic weapon sprites when the viewmodel tuning asks for
/// them. The container is built on the operator's machine by <c>authoring/tools/fp-arms/build.sh</c> from the old game's
/// first-person arms and melee clips, which may not be committed. The arms are clip-led: each weapon family plays its
/// idle loop at rest and a clip per classic strike, fitted to the strike's timing by its marks, and the look rides the
/// hand. Marks and holds are tuned in the developer workbench, which saves back to the definition.
/// </summary>
/// <remarks>
/// The definition names the arms rig and the Engine's held arms, and each weapon family: the
/// classic weapon visuals it draws, its look (its model, if it has one yet, and where and how the hand holds it), its
/// idle loop and its strike clips, keyed by the classic strike action ids. A visual only gives a weapon its default
/// family, as one visual covers weapons held differently: an item template's entry names the family it plays and its
/// own model, grip and hold. Its models are container paths. A
/// definition that names what cannot be drawn is refused by file and field rather than drawn wrong; a clip the arms
/// lack is refused when they are first drawn. Its records are the Engine's tunable first-person records, so edits to
/// the loaded art show on the next draw.
/// </remarks>
internal sealed class DaggerfallHeldArt
{
    /// <summary>The container's file name, installed beside the product.</summary>
    internal const string ContainerName = "first-person.rpak";

    /// <summary>The held definition: a content-root path, so <c>content/worldrpg/first-person/</c> in the repository.</summary>
    internal const string DocumentPath = "worldrpg/first-person/daggerfall.held.json";

    private DaggerfallHeldArt(ProductContentBundle container, DaggerfallHeldArtDocument document)
    {
        Container = container;
        Document = document;
    }

    internal ProductContentBundle Container { get; }

    internal DaggerfallHeldArtDocument Document { get; }

    /// <summary>The prefix of an item template's look id in the workbench (<c>item.120</c>).</summary>
    internal const string ItemLookPrefix = "item.";

    /// <summary>The family that draws the classic weapon visual <paramref name="resourceId"/>, or null when none does.</summary>
    internal string? Family(string resourceId) =>
        Document.Families.FirstOrDefault(family => family.Value.Weapons.Contains(resourceId, StringComparer.Ordinal)).Key;

    /// <summary>
    /// What draws a weapon: an item template's entry if it has one, else the family of its classic visual
    /// <paramref name="resourceId"/>, in <paramref name="metal"/>; null when neither draws it. A bow's model and draw are
    /// its item's, so a bow family's visual alone draws the family's first item.
    /// </summary>
    internal DaggerfallHeldDrawing? Drawing(string resourceId, int? template, string metal) =>
        template is int index && Document.Items.TryGetValue(index, out DaggerfallHeldItem? item) ? new(item.Family, index, metal)
        : Family(resourceId) is not { } family ? null
        : Document.Families[family].Bow is null ? new(family, null, metal)
        : new(family, Document.Items.Where(entry => entry.Value.Family == family).Min(entry => entry.Key), metal);

    /// <summary>
    /// What draws a weapon with a shield on the other arm: the shield's family, whose clips carry it, and the shield of
    /// <paramref name="template"/> in <paramref name="metal"/>; the weapon as it was when the art has no such shield.
    /// </summary>
    internal DaggerfallHeldDrawing WithShield(DaggerfallHeldDrawing drawing, int template, string metal) =>
        Document.Shield is { } shield && shield.Items.ContainsKey(template) ? drawing with { Family = shield.Family, Shield = template, ShieldMetal = metal } : drawing;

    /// <summary>What the workbench's look id draws, in steel: a classic visual or an item (<c>item.120</c>); null for neither.</summary>
    internal DaggerfallHeldDrawing? Look(string look) =>
        look.StartsWith(ItemLookPrefix, StringComparison.Ordinal)
            ? int.TryParse(look.AsSpan(ItemLookPrefix.Length), out int template) && Document.Items.TryGetValue(template, out DaggerfallHeldItem? item)
                ? new(item.Family, template, DaggerfallHeldDrawing.Steel) : null
            : Drawing(look, null, DaggerfallHeldDrawing.Steel);

    /// <summary>The arm set that draws a player of <paramref name="gender"/> with <paramref name="wear"/> on the arms.</summary>
    internal string Arms(DaggerfallCharacterGender gender, DaggerfallArmWear wear)
    {
        DaggerfallHeldWornArms worn = gender == DaggerfallCharacterGender.Female ? Document.ArmsWorn.Female : Document.ArmsWorn.Male;
        return wear switch { DaggerfallArmWear.Leather => worn.Leather, DaggerfallArmWear.Mail => worn.Mail, _ => worn.Cloth };
    }

    /// <summary>The arm set that draws the player transformed into <paramref name="beast"/>.</summary>
    internal string Arms(DaggerfallRacialKind beast) => beast == DaggerfallRacialKind.Wereboar ? Document.ArmsBeast.Wereboar : Document.ArmsBeast.Werewolf;

    /// <summary>The models of the arm set <paramref name="arms"/>; with none named, of the set the rig was measured on.</summary>
    internal DaggerfallHeldArmSet ArmSet(string? arms) =>
        arms is null ? Document.ArmSets.Values.First(set => set.Model == Document.Rig.Model) : Document.ArmSets[arms];

    /// <summary>
    /// What a model's lightning follows (<c>authoring/tools/weapons/arcs.py</c>) and the joint it rides, if it rides one;
    /// null for a model with none.
    /// </summary>
    internal (HeldArcShape Shape, string? Joint)? Lightning(string model)
    {
        string path = Path.ChangeExtension(model, ".arcs.json");
        if (!Container.Entries.ToArray().Any(entry => entry.Path == path)) return null;
        DaggerfallHeldArcsFile file = JsonSerializer.Deserialize(Container.ReadBytes(path).Span, DaggerfallHeldArtJson.Default.DaggerfallHeldArcsFile)
            ?? throw new InvalidDataException($"{path} is empty.");
        static HeldArcPath[] Paths(List<DaggerfallHeldArcFilePath> paths) => [.. paths.Select(arc => new HeldArcPath(arc.Group, arc.Points))];
        static Vector3[] Vectors(float[] flat) => [.. flat.Chunk(3).Select(v => new Vector3(v[0], v[1], v[2]))];
        return (new(Paths(file.Paths), Paths(file.Sparks), new(Vectors(file.Hull.Points), Vectors(file.Hull.Normals), file.Hull.Corners)), file.Joint);
    }

    /// <summary>The material slot of a model's <c>metal</c> material, which an item's metal tints; null when it has none.</summary>
    internal uint? MetalSlot(string model) => Materials(model).Metal;

    /// <summary>What a model's material slots are tinted before any light: white, and its <c>metal</c> slot in <paramref name="metal"/>.</summary>
    internal Color[] SlotTints(string model, DaggerfallDyeTint? metal)
    {
        (int count, uint? metalSlot) = Materials(model);
        Color[] tints = new Color[count];
        Array.Fill(tints, new Color(1, 1, 1, 1));
        if (metal is not null && metalSlot is uint slot) tints[slot] = MetalColor(metal);
        return tints;
    }

    // Each model's material count and metal slot, read from its glTF the first time it is drawn: a model is read whole
    // to find them, which a rider's reins or a swapped weapon would otherwise repeat in the update that draws it.
    private readonly Dictionary<string, (int Count, uint? Metal)> _materials = [];

    private (int Count, uint? Metal) Materials(string model)
    {
        if (_materials.TryGetValue(model, out var known)) return known;
        byte[] glb = Container.ReadBytes(model).ToArray();
        using JsonDocument gltf = JsonDocument.Parse(glb.AsMemory(20, BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12))));
        (int Count, uint? Metal) read = (0, null);
        if (gltf.RootElement.TryGetProperty("materials", out JsonElement materials))
        {
            uint slot = 0;
            foreach (JsonElement material in materials.EnumerateArray())
            {
                if (read.Metal is null && material.TryGetProperty("name", out JsonElement name) && name.ValueEquals("metal")) read.Metal = slot;
                slot++;
            }
            read.Count = (int)slot;
        }
        _materials.Add(model, read);
        return read;
    }

    /// <summary>
    /// The colour a dye tint multiplies a model's texture by (linear): the paper doll's tint, its colour brightened as it
    /// brightens the image.
    /// </summary>
    internal static Color MetalColor(DaggerfallDyeTint tint)
    {
        static float Linear(int srgb)
        {
            float value = srgb / 255F;
            return value <= .04045F ? value / 12.92F : MathF.Pow((value + .055F) / 1.055F, 2.4F);
        }
        int rgb = Convert.ToInt32(tint.Multiply[1..], 16);
        float bright = MathF.Pow((float)tint.Brightness, 2.2F);
        return new Color(Linear(rgb >> 16 & 255) * bright, Linear(rgb >> 8 & 255) * bright, Linear(rgb & 255) * bright, 1F);
    }

    /// <summary>The art a composition resolved over, or nothing when no first-person container was opened.</summary>
    internal static DaggerfallHeldArt? For(ResolvedGameComposition composition)
    {
        ArgumentNullException.ThrowIfNull(composition);
        return composition.Installed.FirstOrDefault(installed => installed.Name == ContainerName)?.Container is { } container
            ? Read(container, composition.Content.ReadBytes(DocumentPath)) : null;
    }

    /// <summary>Reads and checks the held definition against the container's models.</summary>
    /// <exception cref="InvalidDataException">The definition is unreadable, or names what cannot be drawn.</exception>
    internal static DaggerfallHeldArt Read(ProductContentBundle container, ReadOnlyMemory<byte> definition)
    {
        ArgumentNullException.ThrowIfNull(container);
        DaggerfallHeldArtDocument document;
        try
        {
            document = JsonSerializer.Deserialize(definition.Span, DaggerfallHeldArtJson.Default.DaggerfallHeldArtDocument)
                ?? throw new InvalidDataException($"{DocumentPath} is empty.");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"{DocumentPath} cannot be read: {exception.Message}", exception);
        }
        List<string> problems = [];
        void Add(IEnumerable<HeldProblem> found, string under) => problems.AddRange(found.Select(problem => $"{problem.Under(under).Field}: {problem.Message}"));
        Add(document.Rig.Problems(), "rig");
        if (!document.ArmSets.Values.Any(set => set.Model == document.Rig.Model)) problems.Add($"rig.model: '{document.Rig.Model}' is no arm set's model.");
        foreach (var (at, name) in new[] { ("male.cloth", document.ArmsWorn.Male.Cloth), ("male.leather", document.ArmsWorn.Male.Leather), ("male.mail", document.ArmsWorn.Male.Mail),
            ("female.cloth", document.ArmsWorn.Female.Cloth), ("female.leather", document.ArmsWorn.Female.Leather), ("female.mail", document.ArmsWorn.Female.Mail) })
            if (!document.ArmSets.ContainsKey(name)) problems.Add($"armsWorn.{at}: '{name}' is no arm set ({string.Join(", ", document.ArmSets.Keys)}).");
        foreach (var (at, name) in new[] { ("werewolf", document.ArmsBeast.Werewolf), ("wereboar", document.ArmsBeast.Wereboar) })
            if (!document.ArmSets.ContainsKey(name)) problems.Add($"armsBeast.{at}: '{name}' is no arm set ({string.Join(", ", document.ArmSets.Keys)}).");
        if (document.Families.Count == 0) problems.Add("families: names no weapon family.");
        foreach (var (name, family) in document.Families)
        {
            string at = $"families.{name}";
            if (family.Weapons.Count == 0) problems.Add($"{at}.weapons: names no classic weapon visual to replace.");
            foreach (string weapon in family.Weapons.Where(weapon => document.Families.Any(other => other.Key != name && other.Value.Weapons.Contains(weapon))))
                problems.Add($"{at}.weapons: '{weapon}' is drawn by another family too.");
            if (family.Idle.Clip.Length == 0) problems.Add($"{at}.idle.clip: must name a clip.");
            if (!(family.Idle.Seconds > 0)) problems.Add($"{at}.idle.seconds: must be positive.");
            if (family.Strikes.Count == 0) problems.Add($"{at}.strikes: names no strike clip.");
            foreach (var (action, clip) in family.Strikes)
            {
                if (!DaggerfallAttackIds.Held.Contains(action) || action == DaggerfallAttackIds.PowerHold)
                    problems.Add($"{at}.strikes.{action}: is not an attack with a timed clip ({string.Join(", ", DaggerfallAttackIds.Held.Except([DaggerfallAttackIds.PowerHold]))}).");
                Add(clip.Problems(), $"{at}.strikes.{action}");
            }
            if (!DaggerfallAttackTuning.Default.Classes.ContainsKey(family.Class))
                problems.Add($"{at}.class: '{family.Class}' is no weapon class ({string.Join(", ", DaggerfallAttackTuning.Default.Classes.Keys)}).");
            if (family.PowerHold is { } hold && (hold.Clip.Length == 0 || (hold.Seconds is > 0) == (hold.At is >= 0F and <= 1F)))
                problems.Add($"{at}.powerHold: must name a clip with either seconds to loop over or a point (0 to 1) to hold at.");
            foreach (string action in (family.LeftHand ?? []).Where(action => !family.Strikes.ContainsKey(action)))
                problems.Add($"{at}.leftHand: '{action}' has no strike clip here.");
        }
        void Guard(DaggerfallHeldGuard guard, string at)
        {
            foreach (var (phase, clip) in new[] { ("raise", guard.Raise), ("hold", guard.Hold), ("lower", guard.Lower) })
                if (clip.Clip.Length == 0 || !(clip.Seconds > 0)) problems.Add($"{at}.{phase}: must name a clip and how many seconds it plays over.");
        }
        foreach (var (name, family) in document.Families)
            if (family.Guard is { } guard) Guard(guard, $"families.{name}.guard");
        if (document.Shield is { } shield)
        {
            if (!document.Families.ContainsKey(shield.Family)) problems.Add($"shield.family: names no family ({string.Join(", ", document.Families.Keys)}).");
            Guard(shield.Guard, "shield.guard");
        }
        foreach (var (template, item) in document.Items)
        {
            if (!document.Families.TryGetValue(item.Family, out DaggerfallHeldFamily? family))
                problems.Add($"items.{template}.family: names no family ({string.Join(", ", document.Families.Keys)}).");
            else if (family.Bow is not null)
            {
                if (item.Model is null) problems.Add($"items.{template}.model: a bow needs a model.");
                if (item.Drawn is not (> 0F and <= 1F)) problems.Add($"items.{template}.drawn: a bow needs how much of its draw clip a full draw reaches (over 0, up to 1).");
            }
            else if (DaggerfallAttackIds.Fast.Except(family.Strikes.Keys).ToArray() is { Length: > 0 } missing)
                problems.Add($"items.{template}.family: {item.Family} has no clip for {string.Join(", ", missing)}.");
        }
        foreach (string name in document.Families.Where(family => family.Value.Bow is not null).Select(family => family.Key).Where(name => !document.Items.Values.Any(item => item.Family == name)))
            problems.Add($"families.{name}.bow: needs an item of the family to name the bow's model and draw.");
        IEnumerable<string> models = [.. document.ArmSets.Values.SelectMany(set => new[] { set.Model, set.RetroModel }), .. document.Families.Values.Select(family => family.Look.Model).OfType<string>(), .. document.Families.Values.Select(family => family.Bow?.Arrow).OfType<string>(),
            .. document.Items.Values.Select(item => item.Model).OfType<string>(), .. document.Shield?.Items.Values.Select(item => item.Model) ?? [],
            .. document.Enchant is { } enchant ? new[] { enchant.Shader } : [],
            .. document.Mount is { } mount ? new[] { mount.Horse.Model, mount.Horse.RetroModel, mount.Harnessed.Model, mount.Harnessed.RetroModel, mount.Cart.Model, mount.Cart.RetroModel } : []];
        foreach (string model in models.Where(model => !container.Entries.ToArray().Any(entry => entry.Path == model)))
            problems.Add($"'{model}' is not in {ContainerName}.");
        return problems.Count == 0
            ? new DaggerfallHeldArt(container, document)
            : throw new InvalidDataException($"{DocumentPath}: {string.Join(" ", problems)}");
    }
}

/// <summary>The held definition (<see cref="DaggerfallHeldArt.DocumentPath"/>).</summary>
/// <param name="Rig">How to drive the arms; its model is the arm set it was measured on, a container path.</param>
/// <param name="ArmSets">The arm sets by name, each carrying every clip the families play.</param>
/// <param name="ArmsWorn">The arm set a player's gender and arm armour draw.</param>
/// <param name="ArmsBeast">The arm set each beast form draws, whatever the player wears.</param>
/// <param name="Arms">The shoulder (where the clips' right upper arm sits from the eye), elbow pole, default hold and hand closures.</param>
/// <param name="Families">The weapon families by name, each playing its own clips.</param>
internal sealed record DaggerfallHeldArtDocument(ArmRig Rig, Dictionary<string, DaggerfallHeldArmSet> ArmSets, DaggerfallHeldArmsWorn ArmsWorn, DaggerfallHeldArmsBeast ArmsBeast, HeldArms Arms, Dictionary<string, DaggerfallHeldFamily> Families)
{
    /// <summary>The weapons a classic visual alone can't settle, by item template index.</summary>
    public Dictionary<int, DaggerfallHeldItem> Items { get; init; } = [];
    /// <summary>How a shield on the off arm draws; without one a shield is not drawn.</summary>
    public DaggerfallHeldShield? Shield { get; init; }
    /// <summary>How an enchanted weapon's lightning draws; without it an enchanted weapon draws as a plain one.</summary>
    public DaggerfallHeldEnchant? Enchant { get; init; }
    /// <summary>What the player rides; without it nothing is drawn while riding.</summary>
    public DaggerfallHeldMountArt? Mount { get; init; }
}

/// <summary>
/// The horse and cart a rider sees: their models and the horse without its saddle that pulls the cart, the eye's place above
/// the ground on each (the model's origin at the ground under the middle of it), where the horse stands from the cart, and
/// the horse's gaits, slowest first.
/// </summary>
internal sealed record DaggerfallHeldMountArt(DaggerfallHeldArmSet Horse, DaggerfallHeldArmSet Harnessed, DaggerfallHeldArmSet Cart, Vector3 Seat, Vector3 CartSeat, Vector3 CartHorse, List<DaggerfallHeldGait> Gaits);

/// <summary>A gait: its loop's clip and seconds, the speed in metres a second it starts at and the speed it was made for, and how far it bobs the rider.</summary>
internal sealed record DaggerfallHeldGait(string Clip, float Seconds, float From, float Speed, float Bob);

/// <summary>An enchanted weapon's lightning: its shader (a container path), and how it draws in each style.</summary>
internal sealed record DaggerfallHeldEnchant(string Shader, HeldArcLook Held, HeldArcLook Retro);

/// <summary>A model's lightning, as <c>authoring/tools/weapons/arcs.py</c> writes it.</summary>
internal sealed record DaggerfallHeldArcsFile(List<DaggerfallHeldArcFilePath> Paths, List<DaggerfallHeldArcFilePath> Sparks, DaggerfallHeldArcFileHull Hull)
{
    /// <summary>The model's joint it all rides, where it rides one.</summary>
    public string? Joint { get; init; }
}

/// <summary>A model's welded surface, as flat lists of coordinates and corner indices.</summary>
internal sealed record DaggerfallHeldArcFileHull(float[] Points, float[] Normals, uint[] Corners);

/// <summary>One line of a model's lightning: its arc, and its points in the model's space.</summary>
internal sealed record DaggerfallHeldArcFilePath(int Group, List<Vector3> Points);

/// <summary>
/// What draws one weapon: the family whose clips it plays, the item template that names its own look (null for the
/// family's), and its metal. A change of any of them draws the weapon anew.
/// </summary>
internal sealed record DaggerfallHeldDrawing(string Family, int? Item, string Metal)
{
    /// <summary>The arm set that draws it; null for the one the player wears.</summary>
    public string? Arms { get; init; }

    /// <summary>Whether the weapon is enchanted, which draws its lightning.</summary>
    public bool Enchanted { get; init; }

    /// <summary>The item template of the shield on the off arm, null for none, and its metal.</summary>
    public int? Shield { get; init; }
    public string? ShieldMetal { get; init; }

    /// <summary>The bare metal the models are painted in, which no tint changes.</summary>
    internal const string Steel = "steel";
}

/// <summary>One set of arms: its model, and the same arms with the retro look (their colour map alone, small and drawn nearest), which the heldRetro style draws; container paths.</summary>
internal sealed record DaggerfallHeldArmSet(string Model, string RetroModel);

/// <summary>The arm set each gender draws.</summary>
internal sealed record DaggerfallHeldArmsWorn(DaggerfallHeldWornArms Male, DaggerfallHeldWornArms Female);

/// <summary>The arm set drawn for what is worn on the arms, by name.</summary>
internal sealed record DaggerfallHeldWornArms(string Cloth, string Leather, string Mail);

/// <summary>The arm set each beast form draws, by name.</summary>
internal sealed record DaggerfallHeldArmsBeast(string Werewolf, string Wereboar);

/// <summary>
/// A shield on the off arm: the family whose clips it was made for, which every one-handed weapon plays with a shield up;
/// where it sits on the off hand's joint; where the hand carries it at rest and through a swing, since those clips hold
/// it out of view; how the arms raise it to guard; and each shield's model, by item template index.
/// </summary>
internal sealed record DaggerfallHeldShield(string Family, DaggerfallHeldSeat Seat, DaggerfallHeldCarry Carry, DaggerfallHeldGuard Guard, Dictionary<int, DaggerfallHeldShieldItem> Items);

/// <summary>Where the off hand carries a shield in view when no clip moves it (camera space), and how the hand is turned there (degrees).</summary>
internal sealed record DaggerfallHeldCarry(Vector3 Grip, Vector3 Hold)
{
    public Vector3 Grip { get; set; } = Grip;
    public Vector3 Hold { get; set; } = Hold;
}

/// <summary>Where a model sits on a joint: from the joint, in its own frame (metres), and how it turns there (degrees about X, Y and Z).</summary>
internal sealed record DaggerfallHeldSeat(Vector3 At, Vector3 Turn)
{
    public Vector3 At { get; set; } = At;
    public Vector3 Turn { get; set; } = Turn;
    /// <summary>The model's scale on the joint, against the scale the joint carries from its skeleton.</summary>
    public float Scale { get; init; } = 1;
}

/// <summary>One shield: its model (a container path) and its own seat where it differs from the shields'.</summary>
internal sealed record DaggerfallHeldShieldItem(string Model)
{
    public DaggerfallHeldSeat? Seat { get; init; }
}

/// <summary>
/// A family's bow, carried in the off hand rather than the main: its arrow's model (a container path), where the bow sits
/// on the off hand's joint, the bow's joint the arrow rides and where it sits on that joint, how much of the arrow's width
/// it keeps so close to the eye, and how far through the draw clip the string starts to come back, as the arms' hand takes
/// it. An item of the family names the bow's own model.
/// </summary>
internal sealed record DaggerfallHeldBow(string Arrow, DaggerfallHeldSeat Seat, string Joint, DaggerfallHeldSeat Nock, float ArrowWidth, float PullFrom);

/// <summary>A raised guard: the clip that raises it, the loop it holds on and the clip that lowers it, each over its seconds.</summary>
internal sealed record DaggerfallHeldGuard(DaggerfallIdleClip Raise, DaggerfallIdleClip Hold, DaggerfallIdleClip Lower);

/// <summary>
/// An item template's own place in the held art: the family whose clips it plays, as its template holds it (one hand or
/// two), and its model (a container path), grip and hold, each the family look's when not given, and any chain on the model.
/// </summary>
internal sealed record DaggerfallHeldItem(string Family)
{
    public string? Model { get; init; }
    /// <summary>How much of a bow's draw clip a full draw reaches, as the arms pull a bow of its length to the same place.</summary>
    public float? Drawn { get; init; }
    public HeldChainDefinition? Chain { get; init; }
    public Vector3? Grip { get; set; }
    public Vector3? Hold { get; set; }
}

/// <summary>
/// A weapon family: the classic weapon visuals (resource ids) it draws, its look, the clip the arms loop at rest and
/// the clip each classic strike plays (Engine <see cref="ArmClip"/>, its phases marked in the clip). The off hand rests
/// out of view, unless the look is held in both hands (<see cref="TwoHanded"/>: it plays every clip) or the strike is
/// one of <see cref="LeftHand"/>.
/// </summary>
internal sealed record DaggerfallHeldFamily(IReadOnlyList<string> Weapons, DaggerfallHeldLook Look, DaggerfallIdleClip Idle, Dictionary<string, ArmClip> Strikes)
{
    public bool TwoHanded { get; init; }

    /// <summary>How the family carries a bow, for one that does.</summary>
    public DaggerfallHeldBow? Bow { get; init; }
    public string[]? LeftHand { get; init; }
    /// <summary>The weapon class the workbench times this family's attacks by, as play times them.</summary>
    public string Class { get; init; } = DaggerfallSkills.ShortBlade;
    /// <summary>Where a charged power attack waits for release; without one its sprite shows the hold.</summary>
    public DaggerfallPowerHold? PowerHold { get; init; }
    /// <summary>How the arms raise the weapon to guard; without one the guard shows on nothing.</summary>
    public DaggerfallHeldGuard? Guard { get; init; }

    /// <summary>Whether the off hand plays the clip of <paramref name="action"/> (null at rest).</summary>
    internal bool MovesLeft(string? action) => TwoHanded || action is not null && (LeftHand?.Contains(action) ?? false);
}

/// <summary>
/// A charged power attack's hold: <see cref="Clip"/> looped once every <see cref="Seconds"/>, or held still at the
/// point <see cref="At"/> of it (0 to 1), for a clip whose own hold doesn't join its charge.
/// </summary>
internal sealed record DaggerfallPowerHold(string Clip, float? Seconds = null, float? At = null);

/// <summary>A clip the arms loop at rest, once every <see cref="Seconds"/> of world time.</summary>
internal sealed record DaggerfallIdleClip(string Clip, float Seconds)
{
    public float Seconds { get; set; } = Seconds;
}

/// <summary>
/// A held weapon: its model (a container path, in look space: origin at the grip, length along -Z, up side +Y; null
/// while the family has none, when the hand plays its clips holding nothing), the point the hand holds (look space) and
/// how the hand is turned on it (<see cref="HandAim.Hold"/>, degrees). The build measures both where the clips' weapon
/// bone sits in the hand, so a look rides the clip's swing as the pack's own weapon did; they are tuned in place.
/// </summary>
internal sealed record DaggerfallHeldLook(string? Model, Vector3 Grip, Vector3 Hold)
{
    public Vector3 Grip { get; set; } = Grip;
    public Vector3 Hold { get; set; } = Hold;
}

/// <summary>A <see cref="Vector3"/> as three numbers, <c>[x, y, z]</c>.</summary>
internal sealed class DaggerfallVector3Json : JsonConverter<Vector3>
{
    public override Vector3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.StartArray) throw new JsonException("expected [x, y, z].");
        Span<float> values = stackalloc float[3];
        for (int i = 0; i < 3; i++)
        {
            if (!reader.Read() || reader.TokenType != JsonTokenType.Number) throw new JsonException("expected [x, y, z].");
            values[i] = reader.GetSingle();
        }
        if (!reader.Read() || reader.TokenType != JsonTokenType.EndArray) throw new JsonException("expected [x, y, z].");
        return new Vector3(values[0], values[1], values[2]);
    }

    public override void Write(Utf8JsonWriter writer, Vector3 value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        writer.WriteNumberValue(value.X);
        writer.WriteNumberValue(value.Y);
        writer.WriteNumberValue(value.Z);
        writer.WriteEndArray();
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true,
    Converters = [typeof(DaggerfallVector3Json)], RespectRequiredConstructorParameters = true, RespectNullableAnnotations = true,
    UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow)]
[JsonSerializable(typeof(DaggerfallHeldArtDocument))]
[JsonSerializable(typeof(DaggerfallHeldArcsFile))]
internal sealed partial class DaggerfallHeldArtJson : JsonSerializerContext;
