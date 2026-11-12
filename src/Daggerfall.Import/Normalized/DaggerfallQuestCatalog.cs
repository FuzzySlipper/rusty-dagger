using System.Globalization;
using System.Text;
using Daggerfall.Import.Arena2;
using Daggerfall.Import.Publication;

namespace Daggerfall.Import.Normalized;

public sealed record DaggerfallQuestCatalogRow(string Name, string Group, string? Membership,
    int MinimumRequirement, string RequirementKind, bool Adult, bool OneTime, bool Active,
    string SourceDisposition, string Notes, int SourceLine);

public sealed record DaggerfallQuestCatalog(PublishedSource Source, IReadOnlyList<DaggerfallQuestCatalogRow> Rows);
