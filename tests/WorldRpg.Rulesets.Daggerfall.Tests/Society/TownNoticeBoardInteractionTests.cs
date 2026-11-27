using System.Numerics;
using System.Text;
using Rusty.Engine;
using WorldRpg.Kit;
using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.Modules.Interaction;
using WorldRpg.Rulesets.Daggerfall.Presentation;
using WorldRpg.Rulesets.Daggerfall.World;
using Xunit;
using static WorldRpg.Rulesets.Daggerfall.Tests.TestSessions;

namespace WorldRpg.Rulesets.Daggerfall.Tests;

/// <summary>
/// Physical in-world Town &amp; Tavern Notice Board interaction and lead pickup (#11082):
/// - Deterministic notice board prop + interaction target placement in exterior towns and tavern interiors.
/// - Dedicated 3D wooden notice board mesh (posts, pitched canopy, backboard, pinned parchments, ink lines, wax seals)
///   published directly by <see cref="DaggerfallSiteAppearance"/> rather than reusing a lamp-post sprite.
/// - Ordinary Grab/Info activation, HUD prompt verbs, and transient modal UI projection (`DaggerfallNoticeBoardPanel`).
/// - Direct pickup of posted courier delivery offers and regional bounty/decree leads with zero save bytes when untouched.
/// </summary>
public sealed class TownNoticeBoardInteractionTests
{
    private const int DaggerfallRegion = 17;

    [Fact]
    public void Exterior_town_and_tavern_interior_assemble_deterministic_physical_notice_boards_with_dedicated_mesh_and_zero_save_bytes()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);

        // 1. Exterior town profile carries one deterministic physical notice board with NO substituted lamp-post decoration.
        DaggerfallSiteNoticeBoard board = Assert.Single(fixture.ExteriorProfile.NoticeBoards);
        Assert.Equal("notice-board/town", board.Id);
        Assert.Contains(session.Site.ActiveSite!.Name, board.Label, StringComparison.Ordinal);
        Assert.DoesNotContain(fixture.ExteriorProfile.Decorations, d => d.Id.StartsWith("prop/notice-board/", StringComparison.Ordinal));
        Assert.InRange(board.SightHeight, DaggerfallNoticeBoardMeshData.BackboardMinY, DaggerfallNoticeBoardMeshData.BackboardMaxY);

        // 2. Site appearance publishes the dedicated 3D notice board mesh co-located with the live rebased interactive board.
        DaggerfallSiteNoticeBoard liveBoard = Assert.Single(session.Sites.Projection.NoticeBoards.All).Board;
        Assert.Equal(DaggerfallSceneOrigin.Shift(board.Position, session.Sites.Projection.WorldOffset), liveBoard.Position);
        List<AppearanceFact> locationFacts = [];
        session.Sites.Projection.Appearance.AppendLocationFacts(locationFacts, DaggerfallPresentationObjectIds.ActiveLocationSlot);
        Quaternion expectedRotation = DaggerfallSiteAppearance.NoticeBoardRotation(liveBoard.SurfaceNormal);
        Vector3 expectedFacing = Vector3.Normalize(liveBoard.SurfaceNormal with { Y = 0f });
        Vector3 actualFacing = Vector3.Normalize(Vector3.Transform(Vector3.UnitZ, expectedRotation));
        Assert.True(Vector3.Dot(expectedFacing, actualFacing) > 0.999f);
        Assert.Contains(locationFacts, fact =>
            Vector3.Distance(fact.Transform.Translation, liveBoard.Position.ToVector()) < 1e-3f
            && MathF.Abs(Quaternion.Dot(fact.Transform.Rotation, expectedRotation)) > 0.999f
            && fact.ShadowCasting == ShadowCasting.Cast);

        // 3. Dedicated 3D mesh carries the wood (posts and frame, canopy, backboard) and one group for each kind of sheet
        //    posted, rests on Y = 0, and winds every triangle counter-clockwise around its outward normal.
        DaggerfallNoticeBoardMeshData mesh = DaggerfallSiteAppearance.BuildNoticeBoardMeshData(
            [DaggerfallNoticeSheet.Bounty, DaggerfallNoticeSheet.Notice, DaggerfallNoticeSheet.Decree, DaggerfallNoticeSheet.Notice, DaggerfallNoticeSheet.Bounty]);
        Assert.Equal(6, mesh.Groups.Length);
        Assert.Equal(3, DaggerfallSiteAppearance.BuildNoticeBoardMeshData([]).Groups.Length);
        Assert.All(mesh.Groups, group => Assert.True(group.Count > 0));
        Assert.InRange(mesh.BoundsMin.Y, -1e-4f, 1e-4f);
        Assert.InRange(mesh.BoundsMax.Y, 1.95f, 2.20f);
        Assert.InRange(mesh.BoundsMax.X - mesh.BoundsMin.X, 1.30f, 1.65f);
        for (int i = 0; i < mesh.Indices.Length; i += 3)
        {
            Vector3 a = mesh.Positions[mesh.Indices[i]];
            Vector3 b = mesh.Positions[mesh.Indices[i + 1]];
            Vector3 c = mesh.Positions[mesh.Indices[i + 2]];
            Vector3 geometricNormal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            Assert.True(Vector3.Dot(geometricNormal, mesh.Normals[mesh.Indices[i]]) > 0.999f,
                $"Triangle {i / 3} has inverted winding against its outward normal.");
        }

        // 4. Activation module exposes the physical board with Read/Examine prompt verbs and refuses Bash/Steal/Talk.
        DaggerfallActivationTarget target = Assert.Single(
            session.Activation.Module!.InspectTargets(),
            t => t.Kind == DaggerfallActivationTargetKind.NoticeBoard);
        Assert.Equal(board.Label, target.Label);
        Assert.Equal("Read", DaggerfallActivationPrompt.Action(DaggerfallActivationMode.Grab, target.Kind));
        Assert.Equal("Examine", DaggerfallActivationPrompt.Action(DaggerfallActivationMode.Info, target.Kind));
        Assert.Null(DaggerfallActivationPrompt.Action(DaggerfallActivationMode.Bash, target.Kind));
        Assert.Null(DaggerfallActivationPrompt.Action(DaggerfallActivationMode.Steal, target.Kind));
        Assert.Null(DaggerfallActivationPrompt.Action(DaggerfallActivationMode.Talk, target.Kind));

        // 5. Tavern interior (BuildingType == 15) also assembles a deterministic Tavern Notice Board with no lamp-post substitution!
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        DaggerfallSiteBuildingSource? tavernBuilding = charing.Exterior!.Buildings.Values
            .OrderBy(b => b.Id.BlockX).ThenBy(b => b.Id.BlockY).ThenBy(b => b.Id.Index)
            .FirstOrDefault(b => b.Source.BuildingType == DaggerfallLodgingState.TavernBuildingType);
        Assert.NotNull(tavernBuilding);
        DaggerfallWorldProfileKey tavernProfileKey = DaggerfallWorldProfileIds.Interior(charing.Id, tavernBuilding!.Id);
        if (session.Sites.Profiles is { } profiles && profiles.TryGet(tavernProfileKey, out DaggerfallSiteProfile? tavernProfile))
        {
            DaggerfallSiteNoticeBoard tavernBoard = Assert.Single(tavernProfile!.NoticeBoards);
            Assert.Equal("notice-board/tavern", tavernBoard.Id);
            Assert.Equal("Tavern Notice Board", tavernBoard.Label);
            Assert.DoesNotContain(tavernProfile.Decorations, d => d.Id.StartsWith("prop/notice-board/", StringComparison.Ordinal));
            Assert.InRange(tavernBoard.SightHeight, DaggerfallNoticeBoardMeshData.BackboardMinY, DaggerfallNoticeBoardMeshData.BackboardMaxY);
        }

        // 6. Untouched town notice board writes zero save bytes (no spurious conditions, dispatches, or notebook notes).
        DaggerfallSavePayload raw = DaggerfallSavePayload.Read(session.CaptureSave());
        Assert.Empty(raw.WorldConditions.Tokens);
        Assert.Empty(raw.CourierDispatches.Dispatches);
        Assert.Empty(raw.Notebook.Notes);
    }

    [Fact]
    public void Activating_in_world_notice_board_opens_projected_modal_picks_up_courier_and_bounty_leads_and_survives_save_load()
    {
        using var fixture = SourceBackedGuildBankSessionFixture.Create();
        using DaggerfallSession session = fixture.Start(fixture.ExteriorProfile);
        DaggerfallSiteRecord charing = session.Site.ActiveSite!;
        DaggerfallSiteRecord destination = TestPayload.Definitions.Locations.Records
            .First(r => r.Id != charing.Id && r.Id.Region == DaggerfallRegion && r.Exterior is { Buildings.Count: > 0 });

        // Stage a regional MonsterSurge bounty AND a sealed courier dispatch from Charing!
        int today = checked((int)session.Calendar.DayNumber);
        session.WorldConditions.Set(new DaggerfallWorldConditionToken(
            TokenId: "cond:surge:undead:17",
            Kind: DaggerfallWorldConditionKind.MonsterSurge,
            Region: DaggerfallRegion,
            SubjectTag: "undead",
            Severity: 2,
            StartedDay: today,
            ExpiresDay: today + 14));
        DaggerfallCourierDispatchSave dispatch = session.CourierDispatches.StageDispatch(
            dispatchId: "disp-notice-board-1",
            seatId: "daggerfall",
            actionKind: "warning",
            summary: "Urgent seal for the regional court",
            origin: charing,
            destination: destination,
            currentMinute: session.Calendar.MinuteNumber,
            currentDay: checked((int)session.Calendar.DayNumber),
            deliveryRewardGold: 175,
            stageOverlandBand: false);

        // 1. Info mode examines the board without opening the modal.
        DaggerfallActivationTarget boardTarget = Assert.Single(
            session.Activation.Module!.InspectTargets(),
            t => t.Kind == DaggerfallActivationTargetKind.NoticeBoard);
        DaggerfallNoticeBoardActivationOwner directOwner = new(
            session.Sites.Projection.NoticeBoards,
            label => session.NoticeBoardPanel.Open(label));
        DaggerfallActivationOutcome examined = directOwner.ActivateNoticeBoard(
            new DaggerfallActivationSelection(DaggerfallActivationMode.Info, boardTarget));
        Assert.True(examined.Applied, examined.Message);
        Assert.False(session.NoticeBoardPanel.IsOpen);
        Assert.Null(session.NoticeBoardPanel.Read());

        // 2. Grab mode activates the physical board -> opens NoticeBoardPanel and projects live entries + leads!
        DaggerfallActivationOutcome opened = directOwner.ActivateNoticeBoard(
            new DaggerfallActivationSelection(DaggerfallActivationMode.Grab, boardTarget));
        Assert.True(opened.Applied, opened.Message);
        Assert.True(session.NoticeBoardPanel.IsOpen);

        DaggerfallNoticeBoardUiView view = session.NoticeBoardPanel.Read()!;
        Assert.NotNull(view);
        Assert.Equal(charing.Name, view.TownName);

        DaggerfallNoticeBoardEntryUiView courierEntry = Assert.Single(
            view.Entries,
            e => e.Lead is { Kind: "courier-delivery" });
        Assert.False(courierEntry.Lead!.PickedUp);
        Assert.Equal(175, courierEntry.Lead.RewardGold);
        Assert.Contains(destination.Name, courierEntry.Heading, StringComparison.Ordinal);

        DaggerfallNoticeBoardEntryUiView bountyEntry = Assert.Single(
            view.Entries,
            e => e.Lead is { Kind: "regional-bounty" } && e.Id == "notice:cond:cond:surge:undead:17");
        Assert.False(bountyEntry.Lead!.PickedUp);
        Assert.NotNull(bountyEntry.Lead.TargetSummary);

        // 3. Wire UI action grammar parses notice-board-take & notice-board-close cleanly!
        DaggerfallPlayerUiAction? parsedTake = DaggerfallUiAction.Parse(
            Encoding.UTF8.GetBytes($"{{\"action\":\"notice-board-take\",\"revision\":\"{view.Revision}\",\"key\":\"{courierEntry.Id}\"}}"));
        Assert.NotNull(parsedTake);
        Assert.Equal(DaggerfallUiActionKind.NoticeBoardTake, parsedTake!.Kind);

        DaggerfallPlayerUiAction? parsedClose = DaggerfallUiAction.Parse(
            Encoding.UTF8.GetBytes($"{{\"action\":\"notice-board-close\",\"revision\":\"{view.Revision}\"}}"));
        Assert.NotNull(parsedClose);
        Assert.Equal(DaggerfallUiActionKind.NoticeBoardClose, parsedClose!.Kind);

        // 4. Take courier delivery lead directly from the board -> player now carries the sealed dispatch!
        DaggerfallActivationOutcome tookCourier = session.NoticeBoardPanel.TakeLead(view.Revision, courierEntry.Id);
        Assert.True(tookCourier.Applied, tookCourier.Message);
        Assert.Contains(session.CourierDispatches.PlayerCarriedDispatches(), d => d.DispatchId == dispatch.DispatchId);

        DaggerfallNoticeBoardUiView afterCourier = session.NoticeBoardPanel.Read()!;
        Assert.NotEqual(view.Revision, afterCourier.Revision);
        Assert.True(afterCourier.Entries.Single(e => e.Id == courierEntry.Id).Lead!.PickedUp);

        // Stale revision is rejected cleanly without mutating state.
        DaggerfallActivationOutcome stale = session.NoticeBoardPanel.TakeLead(view.Revision, bountyEntry.Id);
        Assert.False(stale.Applied);

        // 5. Take regional bounty lead -> recorded idempotently in player's Notebook!
        DaggerfallActivationOutcome tookBounty = session.NoticeBoardPanel.TakeLead(afterCourier.Revision, bountyEntry.Id);
        Assert.True(tookBounty.Applied, tookBounty.Message);

        DaggerfallNoticeBoardUiView afterBounty = session.NoticeBoardPanel.Read()!;
        Assert.True(afterBounty.Entries.Single(e => e.Id == bountyEntry.Id).Lead!.PickedUp);

        // Re-taking the same bounty lead is refused idempotently (no duplicate notebook entries).
        DaggerfallActivationOutcome duplicate = session.NoticeBoardPanel.TakeLead(afterBounty.Revision, bountyEntry.Id);
        Assert.False(duplicate.Applied);

        // 6. Close the modal -> NoticeBoardPanel.Read() returns null again.
        session.NoticeBoardPanel.Close(afterBounty.Revision);
        Assert.False(session.NoticeBoardPanel.IsOpen);
        Assert.Null(session.NoticeBoardPanel.Read());

        // 7. Save & reload preserves carried courier dispatch and journaled bounty lead, and shows both as PickedUp!
        RulesetSavePayload saved = session.CaptureSave();
        DaggerfallSavePayload decoded = DaggerfallSavePayload.Read(saved);
        Assert.Contains(decoded.Notebook.Notes, n => n.Id.StartsWith($"lead:{bountyEntry.Id}@", StringComparison.Ordinal) && n.Text.Contains(charing.Name, StringComparison.Ordinal));
        Assert.Contains(decoded.CourierDispatches.Dispatches, d => d.DispatchId == dispatch.DispatchId && d.Status == DaggerfallDispatchStatus.CarriedByPlayer);

        using DaggerfallSession restored = fixture.Restore(saved);
        Assert.False(restored.NoticeBoardPanel.IsOpen);
        Assert.True(restored.NoticeBoardPanel.Open("Charing Notice Board").Applied);
        DaggerfallNoticeBoardUiView restoredView = restored.NoticeBoardPanel.Read()!;
        Assert.True(restoredView.Entries.Single(e => e.Id == courierEntry.Id).Lead!.PickedUp);
        Assert.True(restoredView.Entries.Single(e => e.Id == bountyEntry.Id).Lead!.PickedUp);
    }
}
