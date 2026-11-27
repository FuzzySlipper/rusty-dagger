using Rusty.Engine;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using Xunit;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// The notice board's art and live sheets (#11138): its wood is the art pack's refreshed planks, its sheets the pack's
/// parchment, and it shows a sheet for each posting its town has now, the kind of sheet following the posting.
/// </summary>
public sealed class NoticeBoardArtTests
{
    private static DaggerfallArtReplacements Art() => DaggerfallArtReplacements.Read(TestContentFiles.Read(Path.Combine(TestData.RepositoryRoot, "content",
        "worldrpg/packs/daggerfall.art-replacements/payload.json")), "daggerfall.art-replacements");

    [Fact]
    public void The_board_is_made_of_the_art_packs_planks_and_parchment()
    {
        DaggerfallArtReplacements art = Art();
        DaggerfallNoticeBoardArt board = Assert.IsType<DaggerfallNoticeBoardArt>(art.NoticeBoard);
        Assert.All(new[] { board.Frame, board.Board, board.Roof }, record => Assert.True(art.Surfaces.ContainsKey(record), $"{record} has no refreshed surface."));
        Assert.All(new[] { board.Bounty, board.Decree, board.Notice }, file =>
        {
            byte[] png = TestContentFiles.ReadCopy(Path.Combine(TestData.RepositoryRoot, "content", file.Path));
            // The IHDR chunk: bit depth 8, colour type 6 (RGBA), which the Engine admits for a separate PNG.
            Assert.Equal((8, 6), (png[24], png[25]));
        });
    }

    [Fact]
    public void A_board_shows_a_sheet_for_each_live_posting_of_its_kind()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create(configure: composition => composition with { ArtReplacements = Art() });
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallTownNoticeBoardView view = session.NoticeBoard.BuildForSite(session.Site.ActiveSite!, session.Calendar.MinuteNumber);
        IReadOnlyList<DaggerfallNoticeSheet> shown = session.ActiveAppearance.NoticeSheetsShown;

        Assert.NotEmpty(shown);
        Assert.Equal(Math.Min(view.Entries.Count, DaggerfallSiteAppearance.MaxNoticeSheets), shown.Count);
        Assert.Equal(view.Entries.Take(shown.Count).Select(entry => entry.Kind switch
        {
            DaggerfallNoticeEntryKind.Bounty => DaggerfallNoticeSheet.Bounty,
            DaggerfallNoticeEntryKind.Decree or DaggerfallNoticeEntryKind.OfficeNotice => DaggerfallNoticeSheet.Decree,
            _ => DaggerfallNoticeSheet.Notice,
        }), shown);

        // The mesh drawn carries one sheet for each shown, in the groups of their kinds.
        DaggerfallNoticeBoardMeshData mesh = DaggerfallSiteAppearance.BuildNoticeBoardMeshData(shown);
        uint sheetIndices = (uint)mesh.Groups.Where(group => group.MaterialSlot >= DaggerfallNoticeBoardMeshData.SheetSlot(DaggerfallNoticeSheet.Bounty)).Sum(group => group.Count);
        Assert.Equal((uint)shown.Count * 6, sheetIndices);
        List<AppearanceFact> facts = [];
        session.ActiveAppearance.AppendLocationFacts(facts, DaggerfallPresentationObjectIds.ActiveLocationSlot);
        Assert.NotEmpty(facts);
    }

    [Fact]
    public void A_posting_made_or_taken_down_within_the_minute_shows_on_the_board_at_once()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create(configure: composition => composition with { ArtReplacements = Art() });
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        long minute = session.Calendar.MinuteNumber;
        IReadOnlyList<DaggerfallNoticeSheet> before = session.ActiveAppearance.NoticeSheetsShown;
        Assert.True(before.Count < DaggerfallSiteAppearance.MaxNoticeSheets);
        Assert.DoesNotContain(DaggerfallNoticeSheet.Bounty, before);

        // A monster surge's bounty is posted: its wanted poster goes up without the clock moving.
        int today = checked((int)session.Calendar.DayNumber);
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            TokenId: "cond:surge:undead:board-sheets",
            Kind: DaggerfallWorldConditionKind.MonsterSurge,
            Region: charing.Id.Region,
            SubjectTag: "undead",
            Severity: 2,
            StartedDay: today,
            ExpiresDay: today + 14));
        IReadOnlyList<DaggerfallNoticeSheet> posted = session.ActiveAppearance.NoticeSheetsShown;
        Assert.Equal(before.Count + 1, posted.Count);
        Assert.Contains(DaggerfallNoticeSheet.Bounty, posted);
        Assert.Equal(session.NoticeBoard!.BuildForSite(charing, session.Calendar.MinuteNumber).Entries.Count, posted.Count);

        // The surge is put down: the poster comes down again, still within the minute.
        Assert.True(session.WorldConditions.Remove("cond:surge:undead:board-sheets"));
        Assert.Equal(before, session.ActiveAppearance.NoticeSheetsShown);
        Assert.Equal(minute, session.Calendar.MinuteNumber);
    }
}
