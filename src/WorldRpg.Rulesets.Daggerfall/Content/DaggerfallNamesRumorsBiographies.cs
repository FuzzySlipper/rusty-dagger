namespace WorldRpg.Rulesets.Daggerfall.Content;

/// <summary>How a name bank composes its sets, in the donor's documented patterns.</summary>
internal enum DaggerfallNameBankKind
{
    Standard,
    Redguard,
    Nord,
    Monster,
}

/// <summary>One fragment set with the text keys its parts resolve through.</summary>
/// <param name="Set">The set's position in its bank.</param>
/// <param name="Keys">The text keys per fragment, in file order.</param>
internal sealed record DaggerfallNameSetDefinition(int Set, IReadOnlyList<DaggerfallTextKey> Keys);

/// <summary>One name bank: its donor identity, its composition, and its present sets.</summary>
/// <param name="Bank">The bank's position in the file.</param>
/// <param name="Name">The bank's donor name, in the donor's bank order.</param>
/// <param name="Kind">How the bank composes its sets.</param>
/// <param name="Sets">The bank's present sets, in file order.</param>
internal sealed record DaggerfallNameBankDefinition(int Bank, string Name, DaggerfallNameBankKind Kind, IReadOnlyList<DaggerfallNameSetDefinition> Sets);

/// <summary>The normalized name tables, loaded from the pack alone.</summary>
/// <param name="Banks">The banks, in file order.</param>
internal sealed record DaggerfallNameTablesSet(IReadOnlyList<DaggerfallNameBankDefinition> Banks);

/// <summary>How a rumor record's type is accounted for.</summary>
internal enum DaggerfallRumorTypeDisposition
{
    Known,
    Unknown,
}

/// <summary>One normalized rumor: who it concerns, where it circulates, and the text key it reads through.</summary>
/// <param name="Index">The record's ordinal in the file.</param>
/// <param name="Type">The rumor type, in the donor's rumor-type numbering.</param>
/// <param name="TypeName">The donor's name for the type, empty when the table names none.</param>
/// <param name="TypeDisposition">Whether the donor's table names the type.</param>
/// <param name="Region">The region the rumor circulates in.</param>
/// <param name="Flags">The donor's import flags, verbatim.</param>
/// <param name="IsQuestRumor">Whether the quest-rumor bit is set, which the donor does not import.</param>
/// <param name="IsSignMessage">Whether the sign-message bit is set, which the donor does not import.</param>
/// <param name="Faction1">The first faction the rumor concerns; no faction catalog resolves it yet.</param>
/// <param name="Faction2">The second faction the rumor concerns; no faction catalog resolves it yet.</param>
/// <param name="QuestId">The quest the rumor belongs to, zero for none.</param>
/// <param name="QuestName">The quest's name field, empty for none.</param>
/// <param name="NpcId">The NPC a post-quest greeting addresses, zero for none.</param>
/// <param name="TimeLimit">The days the rumor stays current.</param>
/// <param name="TextKey">The text key the rumor's text resolves through.</param>
internal sealed record DaggerfallRumorDefinition(
    int Index,
    int Type,
    string TypeName,
    DaggerfallRumorTypeDisposition TypeDisposition,
    int Region,
    int Flags,
    bool IsQuestRumor,
    bool IsSignMessage,
    int Faction1,
    int Faction2,
    int QuestId,
    string QuestName,
    int NpcId,
    int TimeLimit,
    DaggerfallTextKey TextKey);

/// <summary>The normalized rumor catalog, loaded from the pack alone.</summary>
/// <param name="Entries">The entries, in file order.</param>
internal sealed record DaggerfallRumorCatalogSet(IReadOnlyList<DaggerfallRumorDefinition> Entries);

/// <summary>How a questionnaire answer effect is classified, following the donor's application order.</summary>
internal enum DaggerfallBiographyEffectKind
{
    Skill,
    Gold,
    Item,
    FactionReputation,
    SocialReputation,
    BiographyModifier,
    TextMacro,
    Unimplemented,
    Invalid,
}

/// <summary>How a biography text link is accounted for.</summary>
internal enum DaggerfallBiographyLinkDisposition
{
    Resolved,
    Unresolved,
}

/// <summary>One normalized answer effect: its verbatim line, its kind, and its link state.</summary>
/// <param name="Text">The effect line exactly as the file states it.</param>
/// <param name="Kind">How the donor classifies the line.</param>
/// <param name="First">The first operand the kind carries, or empty.</param>
/// <param name="Second">The second operand the kind carries, or empty.</param>
/// <param name="Third">The third operand the kind carries, or empty.</param>
/// <param name="MacroTarget">The text key a text macro names, null for other kinds.</param>
/// <param name="MacroTargetDisposition">Whether that key resolves; null for other kinds.</param>
/// <param name="MacroTargetReason">Why the link does not resolve, empty when it does or does not apply.</param>
internal sealed record DaggerfallBiographyEffectDefinition(
    string Text,
    DaggerfallBiographyEffectKind Kind,
    string First,
    string Second,
    string Third,
    DaggerfallTextKey? MacroTarget,
    DaggerfallBiographyLinkDisposition? MacroTargetDisposition,
    string MacroTargetReason);

/// <summary>One normalized answer: its letter, its text key, and its effects.</summary>
/// <param name="Letter">The answer's letter.</param>
/// <param name="TextKey">The text key the answer's prose resolves through.</param>
/// <param name="Effects">The answer's effects, in file order.</param>
internal sealed record DaggerfallBiographyAnswerDefinition(string Letter, DaggerfallTextKey TextKey, IReadOnlyList<DaggerfallBiographyEffectDefinition> Effects);

/// <summary>One normalized question: its stated number, its prose keys, and its answers.</summary>
/// <param name="Number">The question number the file states.</param>
/// <param name="TextKeys">The text keys the question's prose lines resolve through, in file order.</param>
/// <param name="Answers">The question's answers, in file order.</param>
internal sealed record DaggerfallBiographyQuestionDefinition(int Number, IReadOnlyList<DaggerfallTextKey> TextKeys, IReadOnlyList<DaggerfallBiographyAnswerDefinition> Answers);

/// <summary>The backdrop a questionnaire displays on.</summary>
/// <param name="MediaId">The image identity the donor names.</param>
/// <param name="Source">The logical path of the file that carries it.</param>
/// <param name="Published">Whether any media publication admits the image; none does yet.</param>
/// <param name="Reason">Why the image does not resolve, empty when it does.</param>
internal sealed record DaggerfallBiographyImageDefinition(string MediaId, string Source, bool Published, string Reason);

/// <summary>One normalized biography questionnaire: its identity, its questions, and its links.</summary>
/// <param name="ClassIndex">The class index the file name states.</param>
/// <param name="BiographyIndex">The biography index the file name states.</param>
/// <param name="BackstoryId">The backstory text id: explicit or the class default.</param>
/// <param name="BackstoryExplicit">Whether the file states its backstory id.</param>
/// <param name="BackstoryKey">The text key the backstory record resolves through.</param>
/// <param name="BackstoryDisposition">Whether that record exists.</param>
/// <param name="Questions">The twelve questions, in file order.</param>
/// <param name="Image">The backdrop the questionnaire displays on.</param>
/// <param name="Warnings">The reader's recoverable defects, in file order.</param>
internal sealed record DaggerfallBiographyDefinition(
    int ClassIndex,
    int BiographyIndex,
    int BackstoryId,
    bool BackstoryExplicit,
    DaggerfallTextKey BackstoryKey,
    DaggerfallBiographyLinkDisposition BackstoryDisposition,
    IReadOnlyList<DaggerfallBiographyQuestionDefinition> Questions,
    DaggerfallBiographyImageDefinition Image,
    IReadOnlyList<string> Warnings);

/// <summary>The normalized biographies, loaded from the pack alone.</summary>
/// <param name="Biographies">The questionnaires, in class order.</param>
/// <param name="DefaultLines">How many default-biography lines the source states, trailing empties included.</param>
internal sealed record DaggerfallBiographiesSet(IReadOnlyList<DaggerfallBiographyDefinition> Biographies, int DefaultLines);
