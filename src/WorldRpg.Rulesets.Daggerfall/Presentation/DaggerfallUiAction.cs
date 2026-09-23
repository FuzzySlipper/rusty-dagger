using System.Text.Json;

namespace WorldRpg.Rulesets.Daggerfall.Presentation;

internal sealed record DaggerfallPlayerUiAction(string Action, string? Revision = null, string? Item = null, int? TargetGrid = null, string? TargetEquipment = null, string? Container = null, string? Key = null, string? Label = null, bool Confirm = false,
    string? Name = null, string? Race = null, string? Gender = null, int? FaceIndex = null, int? Reflexes = null, string? Career = null, string? Mode = null,
    string? PrimarySkills = null, string? MajorSkills = null, string? MinorSkills = null, string? Advantages = null, string? Disadvantages = null, int? HitPointsPerLevel = null,
    string? Attribute = null, string? BackgroundAnswers = null, string? AttributeAllocations = null, string? SkillAllocations = null, ulong? Amount = null,
    string? QuestInstance = null, int? QuestMessage = null, int? QuestChoice = null, string? QuestPrompt = null,
    string? Note = null, string? Text = null, int? Page = null, int? Destination = null,
    string? Tone = null, string? Topic = null, int? Hours = null);

/// <summary>The small Daggerfall player-action wire contract, consumed during admitted updates.</summary>
internal static class DaggerfallUiAction
{
    internal const string BeginAction = "begin";
    internal static DaggerfallPlayerUiAction? Parse(ReadOnlySpan<byte> payload)
    {
        if (payload.IsEmpty || payload.Length > 4096) return null;
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray());
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            HashSet<string> fields = new(StringComparer.Ordinal);
            string? action = null, revision = null, item = null, targetEquipment = null, container = null, key = null, label = null, name = null, race = null, gender = null, career = null, mode = null, primarySkills = null, majorSkills = null, minorSkills = null, advantages = null, disadvantages = null, attribute = null, backgroundAnswers = null, attributeAllocations = null, skillAllocations = null, questInstance = null, questPrompt = null, note = null, text = null, tone = null, topic = null;
            int? targetGrid = null, faceIndex = null, reflexes = null, hitPointsPerLevel = null, questMessage = null, page = null, destination = null, hours = null;
            ulong? amount = null;
            bool confirm = false;
            int? questChoice = null;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (!fields.Add(property.Name)) return null;
                if (property.Name is "targetGrid" or "faceIndex" or "reflexes" or "hitPointsPerLevel" or "questMessage" or "page" or "destination" or "hours")
                {
                    if (!property.Value.TryGetInt32(out int grid)) return null;
                    if (property.Name == "targetGrid") targetGrid = grid;
                    else if (property.Name == "faceIndex") faceIndex = grid;
                    else if (property.Name == "reflexes") reflexes = grid;
                    else if (property.Name == "hitPointsPerLevel") hitPointsPerLevel = grid;
                    else if (property.Name == "questMessage") questMessage = grid;
                    else if (property.Name == "page") page = grid;
                    else if (property.Name == "destination") destination = grid;
                    else hours = grid;
                    continue;
                }
                if (property.Name == "amount")
                {
                    if (!property.Value.TryGetUInt64(out ulong parsed) || parsed == 0) return null;
                    amount = parsed;
                    continue;
                }
                if (property.Name == "confirm")
                {
                    if (property.Value.ValueKind is not JsonValueKind.True and not JsonValueKind.False) return null;
                    confirm = property.Value.GetBoolean();
                    continue;
                }
                if (property.Name == "questChoice")
                {
                    if (!property.Value.TryGetInt32(out int selected) || selected < 0) return null;
                    questChoice = selected;
                    continue;
                }
                if (property.Value.ValueKind != JsonValueKind.String) return null;
                string? value = property.Value.GetString();
                switch (property.Name)
                {
                    case "action": action = value; break;
                    case "revision": revision = value; break;
                    case "item": item = value; break;
                    case "targetEquipment": targetEquipment = value; break;
                    case "container": container = value; break;
                    case "key": key = value; break;
                    case "label": label = value; break;
                    case "name": name = value; break;
                    case "race": race = value; break;
                    case "gender": gender = value; break;
                    case "career": career = value; break;
                    case "mode": mode = value; break;
                    case "primarySkills": primarySkills = value; break;
                    case "majorSkills": majorSkills = value; break;
                    case "minorSkills": minorSkills = value; break;
                    case "advantages": advantages = value; break;
                    case "disadvantages": disadvantages = value; break;
                    case "attribute": attribute = value; break;
                    case "backgroundAnswers": backgroundAnswers = value; break;
                    case "attributeAllocations": attributeAllocations = value; break;
                    case "skillAllocations": skillAllocations = value; break;
                    case "questInstance": questInstance = value; break;
                    case "questPrompt": questPrompt = value; break;
                    case "note": note = value; break;
                    case "text": text = value; break;
                    case "tone": tone = value; break;
                    case "topic": topic = value; break;
                    default: return null;
                }
            }
            if (action == "inventory-move")
            {
                if (string.IsNullOrWhiteSpace(revision) || string.IsNullOrWhiteSpace(item)
                    || (targetGrid is null) == (targetEquipment is null) || fields.Count != 4) return null;
                return new(action, revision, item, targetGrid, targetEquipment);
            }
            if (action == "inventory-inspect")
                return fields.SetEquals(["action", "revision", "item"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    ? new(action, revision, item) : null;
            if (action == "inventory-use")
                return fields.SetEquals(["action", "revision", "item"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    ? new(action, revision, item) : null;
            if (action == "notebook-page")
                return fields.SetEquals(["action", "revision", "page"]) && !string.IsNullOrWhiteSpace(revision) && page is >= 0
                    ? new(action, Revision: revision, Page: page) : null;
            if (action == "notebook-add")
                return fields.SetEquals(["action", "revision", "text"]) && !string.IsNullOrWhiteSpace(revision) && ValidNotebookText(text)
                    ? new(action, Revision: revision, Text: text) : null;
            if (action == "notebook-edit")
                return fields.SetEquals(["action", "revision", "note", "text"]) && !string.IsNullOrWhiteSpace(revision)
                    && !string.IsNullOrWhiteSpace(note) && ValidNotebookText(text)
                    ? new(action, Revision: revision, Note: note, Text: text) : null;
            if (action == "notebook-remove")
                return fields.SetEquals(["action", "revision", "note"]) && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(note)
                    ? new(action, Revision: revision, Note: note) : null;
            if (action == "notebook-move")
                return fields.SetEquals(["action", "revision", "note", "destination"]) && !string.IsNullOrWhiteSpace(revision)
                    && !string.IsNullOrWhiteSpace(note) && destination is >= 0
                    ? new(action, Revision: revision, Note: note, Destination: destination) : null;
            if (action == "inventory-drop")
                return fields.SetEquals(["action", "revision", "item", "amount"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item) && amount is not null
                    ? new(action, revision, item, Amount: amount) : null;
            if (action == "loot-take")
            {
                bool shape = fields.SetEquals(["action", "revision", "item", "container"])
                    || fields.SetEquals(["action", "revision", "item", "container", "amount"]);
                return shape && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(container)
                    ? new(action, revision, item, Container: container, Amount: amount) : null;
            }
            if (action is "currency-deposit-gold" or "currency-withdraw-gold" or "currency-withdraw-letter")
                return fields.SetEquals(["action", "amount"]) && amount is not null ? new(action, Amount: amount) : null;
            if (action == "currency-deposit-letters")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "bank-transfer")
                return fields.SetEquals(["action", "amount", "destination"]) && amount is not null && destination is >= 0
                    ? new(action, Amount: amount, Destination: destination) : null;
            if (action == "dungeon-text-answer")
                return fields.SetEquals(["action", "revision", "item", "text"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    && text is { Length: <= 256 }
                    ? new(action, Revision: revision, Item: item, Text: text) : null;
            if (action == "dungeon-text-close")
                return fields.SetEquals(["action", "revision", "item"])
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    ? new(action, Revision: revision, Item: item) : null;
            if (action == "art-request")
                return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision)
                    ? new(action, revision) : null;
            if (action == "loot-close")
                return fields.SetEquals(["action", "container"]) && !string.IsNullOrWhiteSpace(container)
                    ? new(action, Container: container) : null;
            if (action == "controls-rebind")
                return fields.IsSubsetOf(["action", "item", "key", "confirm"])
                    && !string.IsNullOrWhiteSpace(item) && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Item: item, Key: key, Confirm: confirm) : null;
            if (action == "controls-reset") return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "save-slots") return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "save-slot")
                return fields.IsSubsetOf(["action", "key", "label", "confirm"])
                    && !string.IsNullOrWhiteSpace(label)
                    && (key is null || !string.IsNullOrWhiteSpace(key))
                    ? new(action, Key: key, Label: label, Confirm: confirm) : null;
            if (action == "load-slot")
                return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Key: key) : null;
            if (action == "delete-slot")
                return fields.IsSubsetOf(["action", "key", "confirm"])
                    && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Key: key, Confirm: confirm) : null;
            if (action is "death-new-game" or "death-quit")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "death-load-game")
                return fields.SetEquals(["action", "key"]) && !string.IsNullOrWhiteSpace(key)
                    ? new(action, Key: key) : null;
            if (action is "character-begin" or "character-cancel")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "character-level-allocate")
                return fields.SetEquals(["action", "attribute"]) && !string.IsNullOrWhiteSpace(attribute)
                    ? new(action, Attribute: attribute) : null;
            if (action == "character-level-commit")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action is "character-update" or "character-commit" or "character-background-reroll")
            {
                HashSet<string> required = career == DaggerfallCustomCareerPolicy.CareerId
                    ? ["action", "name", "race", "gender", "faceIndex", "reflexes", "career", "primarySkills", "majorSkills", "minorSkills", "hitPointsPerLevel", "advantages", "disadvantages"]
                    : ["action", "name", "race", "gender", "faceIndex", "reflexes", "career"];
                bool background = fields.Contains("backgroundAnswers") || fields.Contains("attributeAllocations") || fields.Contains("skillAllocations");
                if (background) required.UnionWith(["backgroundAnswers", "attributeAllocations", "skillAllocations"]);
                return fields.SetEquals(required)
                    && !string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(race)
                    && !string.IsNullOrWhiteSpace(gender) && !string.IsNullOrWhiteSpace(career)
                    && faceIndex is not null && reflexes is not null
                    ? new(action, Name: name, Race: race, Gender: gender, FaceIndex: faceIndex, Reflexes: reflexes, Career: career,
                        PrimarySkills: primarySkills, MajorSkills: majorSkills, MinorSkills: minorSkills, Advantages: advantages, Disadvantages: disadvantages, HitPointsPerLevel: hitPointsPerLevel,
                        BackgroundAnswers: backgroundAnswers, AttributeAllocations: attributeAllocations, SkillAllocations: skillAllocations) : null;
            }
            if (action == "activation-mode")
                return fields.SetEquals(["action", "mode"])
                    && mode is "grab" or "info" or "talk" or "steal" or "lockpick" or "bash"
                    ? new(action, Mode: mode) : null;
            if (action == "transport-select")
                return fields.SetEquals(["action", "mode"])
                    && mode is "foot" or "horse" or "cart"
                    ? new(action, Mode: mode) : null;
            if (action is "transport-toggle" or "transport-leave-ship")
                return fields.SetEquals(["action"]) ? new(action) : null;
            if (action == "rest")
            {
                bool timedOrLoiter = mode is "timed" or "loiter";
                bool untilHealed = mode == "until-healed";
                bool shape = timedOrLoiter
                    ? fields.SetEquals(["action", "mode", "hours"]) && hours is >= 0
                    : untilHealed && fields.SetEquals(["action", "mode"]);
                return shape ? new(action, Mode: mode, Hours: hours) : null;
            }
            if (action is "wagon-put" or "wagon-take")
                return (fields.SetEquals(["action", "revision", "item"])
                    || fields.SetEquals(["action", "revision", "item", "amount"]))
                    && !string.IsNullOrWhiteSpace(revision) && !string.IsNullOrWhiteSpace(item)
                    ? new(action, Revision: revision, Item: item, Amount: amount) : null;
            if (action == "dialogue-tone")
                return fields.SetEquals(["action", "revision", "tone"])
                    && !string.IsNullOrWhiteSpace(revision)
                    && tone is "polite" or "normal" or "blunt"
                    ? new(action, Revision: revision, Tone: tone) : null;
            if (action == "dialogue-topic")
                return fields.SetEquals(["action", "revision", "topic"])
                    && !string.IsNullOrWhiteSpace(revision)
                    && topic is "directions" or "news"
                    ? new(action, Revision: revision, Topic: topic) : null;
            if (action == "dialogue-close")
                return fields.SetEquals(["action", "revision"]) && !string.IsNullOrWhiteSpace(revision)
                    ? new(action, Revision: revision) : null;
            if (action == "quest-choice")
                return fields.SetEquals(["action", "questInstance", "questMessage", "questPrompt", "questChoice"])
                    && !string.IsNullOrWhiteSpace(questInstance) && !string.IsNullOrWhiteSpace(questPrompt) && questMessage is > 0 && questChoice is not null
                    ? new(action, QuestInstance: questInstance, QuestMessage: questMessage, QuestChoice: questChoice, QuestPrompt: questPrompt) : null;
            // "begin" is the entry screen's own action, which the product answers: the session accepts the
            // shape so a slice carrying it is a known action it does not act on, rather than an
            // unrecognized one it reports over the screen that asked.
            return fields.Count == 1 && action is "attack" or "inventory" or "character" or "loot" or "begin" or "cinematic-skip" or "save-game" or "load-game"
                ? new(action) : null;
        }
        catch (Exception error) when (error is JsonException or InvalidOperationException) { return null; }
    }

    private static bool ValidNotebookText(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 2048;
}
