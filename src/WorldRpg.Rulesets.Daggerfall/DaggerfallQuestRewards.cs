using WorldRpg.Rulesets.Daggerfall.Content;
using WorldRpg.Rulesets.Daggerfall.World;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed record DaggerfallQuestRewardState(bool WaitingForTown, double DelaySeconds, long? GroundContainer = null,
    ulong? DeliveryId = null, bool LootOpened = false);

internal static partial class DaggerfallQuestTaskCompiler
{
    private static readonly System.Text.RegularExpressions.Regex GivePc = Header(@"^give\s+pc\s+(?<item>[a-zA-Z0-9_.-]+)(?:\s+notify\s+(?<message>[a-zA-Z0-9_.-]+)|\s+(?<silent>silently))?$");
    private static DaggerfallQuestTaskOperation? CompileReward(string line, int sourceLine)
    {
        if (GivePc.Match(line) is not { Success: true } match) return null;
        string item = Canonical(match.Groups["item"].Value);
        string? message = match.Groups["message"].Success ? match.Groups["message"].Value : null;
        int? messageId = int.TryParse(message, out int number) ? number : null;
        int mode = match.Groups["silent"].Success ? 2 : message is not null && messageId != 0 ? 1 : 0;
        if (item == "nothing" && mode != 0) throw new ArgumentException("Give pc nothing does not accept notification modifiers.");
        return new(DaggerfallQuestTaskOperationKind.GivePc, sourceLine, line, [item], [], messageId,
            Step: mode, MessageAlias: messageId is null ? message : null);
    }
}

internal sealed partial class DaggerfallQuestInstances
{
    private Func<DaggerfallQuestRuntimeInstance, string, long?>? _offerQuestReward;
    private Func<long?, bool>? _presentQuestReward;
    private DaggerfallQuestRewardTuning? _rewardTuning;
    internal void BindRewards(Func<DaggerfallQuestRuntimeInstance, string, long?> offer, Func<long?, bool> present, DaggerfallQuestRewardTuning tuning)
    { _offerQuestReward = offer; _presentQuestReward = present; _rewardTuning = tuning.Validate(); }

    bool IDaggerfallQuestTaskLifecycle.GivePc(DaggerfallQuestRuntimeInstance instance, DaggerfallQuestTaskOperation operation,
        DaggerfallQuestTaskRuntimeState task, int index, DaggerfallCalendar calendar, double elapsedSeconds)
    {
        string item = operation.Targets[0];
        if (item != "nothing" && !instance.Resources.Any(resource => resource.Symbol == item && resource.SelectedItem is not null))
            throw new NotSupportedException($"Reward '{item}' is not an admitted selected Item.");
        int? message = null;
        if (operation.Step == 1)
        {
            if (!Messages.TryResolveMessage(instance, operation.MessageId, operation.MessageAlias, out int resolved, out var diagnostic))
                throw new NotSupportedException(diagnostic);
            message = resolved;
        }
        var state = task.OperationState[index].Reward ?? new(false, 0);
        if (operation.Step is 1 or 2)
        {
            var tuning = _rewardTuning ?? throw new InvalidOperationException("No quest delivery tuning is composed.");
            var location = (_worldRead ?? throw new InvalidOperationException("Quest delivery requires the admitted location."))();
            bool eligible = location.Profile.ProfileKind == DaggerfallWorldProfileKind.Exterior && calendar.Hour >= tuning.MinimumDeliveryHour && calendar.Hour <= tuning.MaximumDeliveryHour
                && location.ExteriorLocation?.Kind is DaggerfallSiteKind.TownCity or DaggerfallSiteKind.TownHamlet or DaggerfallSiteKind.TownVillage
                    or DaggerfallSiteKind.HomeFarms or DaggerfallSiteKind.HomeWealthy or DaggerfallSiteKind.Tavern or DaggerfallSiteKind.ReligionTemple;
            if (!eligible)
            { task.OperationState[index] = task.OperationState[index] with { Reward = state with { WaitingForTown = true, DelaySeconds = 0 } }; return false; }
            if (state.WaitingForTown)
            {
                // GivePc uses 40..500 ten-Hz quest ticks; consume the same duration in admitted
                // simulation time, without adding a quest clock or advancing it during rest.
                double delay = _random.DrawKeyed(new(0, "daggerfall.quest.delivery", instance.InstanceId + "/" + task.Symbol + "/" + index, tuning.MinimumDelaySeconds * 10, tuning.MaximumDelaySeconds * 10)).Value / 10d;
                state = state with { WaitingForTown = false, DelaySeconds = delay };
            }
            if (state.DelaySeconds > 0)
            {
                state = state with { DelaySeconds = Math.Max(0, state.DelaySeconds - elapsedSeconds) };
                task.OperationState[index] = task.OperationState[index] with { Reward = state };
                if (state.DelaySeconds > 0) return false;
            }
            var result = Items.Get(instance, item);
            if (result == DaggerfallQuestItemResult.Unavailable) throw new NotSupportedException($"Quest delivery item '{item}' is unavailable.");
            // The receiving #8031/#8133 contract retains accepted notifications as letters.
            if (message is { } notify) Messages.Letter(instance, notify);
        }
        else
        {
            long? ground = item == "nothing" ? null : (_offerQuestReward ?? throw new InvalidOperationException("No canonical quest reward inventory owner is composed."))(instance, item);
            if (item != "nothing" && ground is null) return false;
            // Success follows actual reward admission. The quest remains active for final source
            // tasks and explicit end; notification-only deliveries do not declare success.
            instance.Succeeded = true;
            bool popup = Messages.TryPopup(instance, 1004);
            ulong? delivery = popup ? Messages.Deliveries.Last().Id : null;
            state = state with { GroundContainer = ground, DeliveryId = delivery, LootOpened = ground is null || !popup };
            (_presentQuestReward ?? throw new InvalidOperationException("No reward presentation owner is composed."))(popup ? null : ground);
        }
        task.OperationState[index] = task.OperationState[index] with { Reward = state, UnavailableReason = null };
        return true;
    }

    internal bool DismissRewardMessage(string instanceId, string entryId)
    {
        var delivery = Messages.Deliveries.FirstOrDefault(value => value.InstanceId == instanceId && value.EntryId == entryId);
        if (delivery is null) return false;
        if (_instances.TryGetValue(instanceId, out var instance))
            foreach (var task in instance.Tasks)
                for (int index = 0; index < task.OperationState.Length; index++)
                    if (task.OperationState[index].Reward is { LootOpened: false, GroundContainer: { } ground } reward && reward.DeliveryId == delivery.Id)
                    {
                        // A site transition can unload the actual container while its HUD message
                        // remains visible. Keep the message until its real loot owner can open.
                        if (!(_presentQuestReward ?? throw new InvalidOperationException("No reward presentation owner is composed."))(ground)) return false;
                        task.OperationState[index] = task.OperationState[index] with { Reward = reward with { LootOpened = true } };
                    }
        return Messages.Dismiss(instanceId, entryId);
    }
}
