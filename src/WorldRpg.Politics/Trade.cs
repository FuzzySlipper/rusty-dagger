using System.Globalization;

namespace WorldRpg.Politics;

/// <summary>
/// One side's term in a trade agreement: a weekly grain ration from the giver's surplus region <see cref="Source"/>,
/// or <see cref="Amount"/> of a resource every <see cref="EveryWeeks"/> weeks. Written as <c>grain:region</c> or
/// <c>coin:1/2</c>; a card's menu offers grain as plain <c>grain</c>, and the region is chosen when the deal is offered.
/// </summary>
public sealed record TradeTerm(Resource? Resource, int Amount, int EveryWeeks, string? Source)
{
    public const string Grain = "grain";

    public bool IsGrain => Resource is null;

    /// <summary>The term written as text, or null if it is not one.</summary>
    public static TradeTerm? Parse(string text)
    {
        string[] parts = text.Split(':');
        if (parts[0] == Grain)
            return parts.Length == 1 ? new TradeTerm(null, 1, 1, null) : parts.Length == 2 && parts[1].Length > 0 ? new TradeTerm(null, 1, 1, parts[1]) : null;
        Resource? resource = Enum.GetValues<Resource>().Cast<Resource?>().FirstOrDefault(value => Name(value!.Value) == parts[0]);
        string[] rate = parts.Length == 2 ? parts[1].Split('/') : [];
        return resource is not null && rate.Length == 2
            && int.TryParse(rate[0], NumberStyles.None, CultureInfo.InvariantCulture, out int amount)
            && int.TryParse(rate[1], NumberStyles.None, CultureInfo.InvariantCulture, out int every)
            ? new TradeTerm(resource, amount, every, null)
            : null;
    }

    public override string ToString() => Resource is { } resource
        ? string.Create(CultureInfo.InvariantCulture, $"{Name(resource)}:{Amount}/{EveryWeeks}")
        : Source is null ? Grain : $"{Grain}:{Source}";

    private static string Name(Resource resource) => resource.ToString().ToLowerInvariant();
}

/// <summary>
/// A standing trade agreement: <see cref="Proposer"/> gives <see cref="Gives"/> and <see cref="Partner"/> gives
/// <see cref="Asks"/>, each Sunday a term falls due, from the turn after <see cref="Start"/> through <see cref="End"/>;
/// it lapses the Sunday after.
/// <see cref="ProposerPrice"/> and <see cref="PartnerPrice"/> are the bargain each side struck: what the resource terms
/// were worth to it when the deal was made (<see cref="Trade.Worth"/>).
/// </summary>
public sealed record Agreement(string Id, string Proposer, TradeTerm Gives, string Partner, TradeTerm Asks, int Start, int End,
    int ProposerPrice, int PartnerPrice)
{
    public int PriceTo(string seat) => seat == Proposer ? ProposerPrice : PartnerPrice;

    public bool Binds(string seat) => Proposer == seat || Partner == seat;

    public string Other(string seat) => seat == Proposer ? Partner : Proposer;

    public TradeTerm GivenBy(string seat) => seat == Proposer ? Gives : Asks;

    public TradeTerm ReceivedBy(string seat) => seat == Proposer ? Asks : Gives;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Id} {Gives} for {Asks} until {End}");
}

/// <summary>
/// Standing trade between seats. A seat offers terms with a deal card, and the partner accepts if the deal comes out
/// ahead for it over <see cref="Horizon"/> weeks, weighing the proposer's gain as it would weigh a rival's
/// (<see cref="Accepts"/>); an accepted deal warms their relation by 1 and runs <see cref="TermWeeks"/> weeks. A seat
/// keeps at most <see cref="MaxAgreements"/> open.
/// <para>
/// Grain is rations, not a resource: a region with a surplus (<see cref="Supply.Surplus"/>) can send one a week under one
/// agreement. It travels any distance overland, through regions neither blockaded nor held by a crown hostile to either
/// side, and relieves the receiver's most valuable region not fed that week. A ration not needed is lost, never stored.
/// </para>
/// <para>
/// A deal ends for both sides when it lapses after its last week, when the two seats turn hostile, when a ration's source region changes
/// hands, loses its surplus or can no longer reach the receiver, when a side cannot pay a term that falls due (a default,
/// which costs 1 relation), or when either seat withdraws (a weekly move, which costs 1 relation).
/// </para>
/// </summary>
public static class Trade
{
    public const int TermWeeks = 26;
    public const int MaxAgreements = 3;

    /// <summary>How many weeks ahead a seat weighs a deal; a term falls due at most this far apart.</summary>
    public const int Horizon = 8;

    /// <summary>The move that withdraws from an agreement, its one target the agreement's id.</summary>
    public const string WithdrawId = "withdraw";

    public static int Open(Board board, string seat) => board.Agreements.Count(deal => deal.Binds(seat));

    /// <summary>
    /// The term as <paramref name="giver"/> would offer it to <paramref name="receiver"/> now: grain from the first of the
    /// giver's surplus regions that is not yet promised and reaches a region the receiver holds, or null if none does.
    /// </summary>
    public static TradeTerm? Resolve(Board board, TradeTerm term, Seat giver, Seat receiver)
    {
        if (!term.IsGrain) return term;
        Region? source = board.Regions.FirstOrDefault(region => Spare(board, region, giver) && Reaches(board, region, giver, receiver));
        return source is null ? null : term with { Source = source.Id };
    }

    /// <summary>Whether <paramref name="proposer"/> can offer these terms to <paramref name="partner"/> now.</summary>
    public static bool Fits(Board board, Seat proposer, Seat partner, TradeTerm gives, TradeTerm asks) =>
        proposer != partner && gives.Resource != asks.Resource
        && board.Relation(proposer.Id, partner.Id) > Supply.Hostile
        && Open(board, proposer.Id) < MaxAgreements && Open(board, partner.Id) < MaxAgreements
        && TermFits(board, gives, proposer, partner) && TermFits(board, asks, partner, proposer);

    /// <summary>Offers the deal: the partner accepts or refuses it at once.</summary>
    public static void Propose(Board board, Seat proposer, Seat partner, TradeTerm gives, TradeTerm asks)
    {
        Agreement offer = Offer(board, proposer, partner, gives, asks);
        if (!Accepts(board, partner, offer))
        {
            board.RefuseAgreement(offer);
            return;
        }
        board.AddAgreement(offer);
        board.AdjustRelation(proposer, partner, 1);
    }

    /// <summary>Whether <paramref name="partner"/> takes <paramref name="offer"/>: its own gain outweighs the proposer's, weighed as the chooser weighs a rival's.</summary>
    public static bool Accepts(Board board, Seat partner, Agreement offer) =>
        MoveChooser.SelfWeight * Worth(board, partner, offer)
        - MoveChooser.RivalWeight(board.Relation(partner.Id, offer.Proposer)) * Worth(board, board.Seat(offer.Proposer), offer) > 0;

    /// <summary>The deal these terms make if struck now, at the prices each side sets on them now.</summary>
    public static Agreement Offer(Board board, Seat proposer, Seat partner, TradeTerm gives, TradeTerm asks) =>
        new("", proposer.Id, gives, partner.Id, asks, board.Turn, board.Turn + TermWeeks, Price(proposer, asks, gives), Price(partner, gives, asks));

    /// <summary>
    /// What <paramref name="deal"/> is worth to <paramref name="seat"/> over <see cref="Horizon"/> weeks: the price it
    /// struck on the resource terms, and whatever grain it receives as it needs it now. A ration is worth 3 a week
    /// while the seat has a region not fed that its other rations do not cover, 1 while one of its regions would not feed
    /// itself in winter, and costs its giver nothing, since grain not sent is lost. Need changes with the land; a price,
    /// once struck, does not.
    /// </summary>
    public static int Worth(Board board, Seat seat, Agreement deal) =>
        deal.PriceTo(seat.Id) + (deal.ReceivedBy(seat.Id).IsGrain ? Horizon * Hunger(board, seat, deal) : 0);

    /// <summary>
    /// What resource terms are worth to <paramref name="seat"/> now over <see cref="Horizon"/> weeks: what it receives less
    /// what it gives. A unit is worth 3 to a seat holding under 3 of it, 2 under 6 and 1 beyond, so a seat trades what it
    /// has plenty of for what it lacks.
    /// </summary>
    private static int Price(Seat seat, TradeTerm received, TradeTerm given) =>
        (received.Resource is { } gain ? Units(received) * UnitValue(seat, gain) : 0)
        - (given.Resource is { } loss ? Units(given) * UnitValue(seat, loss) : 0);

    /// <summary>
    /// Upkeep, before income: ends the deals that have broken, then sends the rations due and returns the regions they
    /// relieve. A region in <paramref name="fed"/> needs none.
    /// </summary>
    public static HashSet<Region> Relieve(Board board, HashSet<Region> fed)
    {
        HashSet<Region> relieved = [];
        foreach (Agreement deal in board.Agreements.ToArray())
        {
            Seat proposer = board.Seat(deal.Proposer);
            Seat partner = board.Seat(deal.Partner);
            if (Broken(board, deal, proposer, partner) is { } reason)
            {
                board.EndAgreement(deal, reason);
                continue;
            }
            Ration(board, deal.Gives, proposer, partner, fed, relieved);
            Ration(board, deal.Asks, partner, proposer, fed, relieved);
        }
        return relieved;
    }

    /// <summary>
    /// Upkeep, after income: pays the resource terms that fall due. A side that cannot pay defaults: the deal ends and
    /// the two seats think 1 worse of each other.
    /// </summary>
    public static void Settle(Board board)
    {
        foreach (Agreement deal in board.Agreements.ToArray())
        {
            Seat proposer = board.Seat(deal.Proposer);
            Seat partner = board.Seat(deal.Partner);
            Seat? defaulter = Short(board, deal.Gives, proposer, deal) ? proposer : Short(board, deal.Asks, partner, deal) ? partner : null;
            if (defaulter is not null)
            {
                board.EndAgreement(deal, $"default by {defaulter.Id}");
                board.AdjustRelation(proposer, partner, -1);
                continue;
            }
            Pay(board, deal.Gives, proposer, partner, deal);
            Pay(board, deal.Asks, partner, proposer, deal);
        }
    }

    /// <summary><paramref name="seat"/> withdraws from <paramref name="deal"/>, ending it for both, and the two think 1 worse of each other.</summary>
    public static void Withdraw(Board board, Seat seat, Agreement deal)
    {
        board.EndAgreement(deal, $"withdrawn by {seat.Id}");
        board.AdjustRelation(seat, board.Seat(deal.Other(seat.Id)), -1);
    }

    /// <summary>
    /// The regions a ration from <paramref name="source"/> reaches: every region joined to it by an overland path that
    /// runs, from end to end, only through regions neither blockaded nor held by a crown hostile to the giver or the
    /// receiver. A blockaded region takes in no grain, and a blockaded source sends none.
    /// </summary>
    public static HashSet<Region> Reached(Board board, Region source, Seat giver, Seat receiver)
    {
        if (!Passable(board, source, giver, receiver)) return [];
        HashSet<Region> reached = [source];
        Queue<Region> open = new([source]);
        while (open.TryDequeue(out Region? region))
        {
            foreach (Region border in region.Borders.Select(board.Region))
                if (Passable(board, border, giver, receiver) && reached.Add(border)) open.Enqueue(border);
        }
        return reached;
    }

    private static bool Passable(Board board, Region region, Seat giver, Seat receiver) =>
        !Supply.Blockaded(region)
        && (region.Allegiance is not { } holder
            || board.Relation(holder, giver.Id) > Supply.Hostile && board.Relation(holder, receiver.Id) > Supply.Hostile);

    private static bool Reaches(Board board, Region source, Seat giver, Seat receiver) =>
        Reached(board, source, giver, receiver).Any(region => region.Allegiance == receiver.Id);

    private static bool Spare(Board board, Region region, Seat giver) =>
        region.Allegiance == giver.Id && Supply.Surplus(region, board.Turn)
        && !board.Agreements.Any(deal => deal.Gives.Source == region.Id || deal.Asks.Source == region.Id);

    private static bool TermFits(Board board, TradeTerm term, Seat giver, Seat receiver) =>
        term.Source is not { } source || Spare(board, board.Region(source), giver) && Reaches(board, board.Region(source), giver, receiver);

    private static string? Broken(Board board, Agreement deal, Seat proposer, Seat partner) =>
        board.Turn > deal.End ? "lapsed"
        : board.Relation(proposer.Id, partner.Id) <= Supply.Hostile ? "hostile"
        : RationBroken(board, deal.Gives, proposer, partner) ?? RationBroken(board, deal.Asks, partner, proposer);

    private static string? RationBroken(Board board, TradeTerm term, Seat giver, Seat receiver)
    {
        if (term.Source is null) return null;
        Region source = board.Region(term.Source);
        return source.Allegiance != giver.Id ? $"{source.Id} lost"
            : !Supply.Surplus(source, board.Turn) ? $"no surplus in {source.Id}"
            : !Reaches(board, source, giver, receiver) ? $"route from {source.Id} cut"
            : null;
    }

    private static void Ration(Board board, TradeTerm term, Seat giver, Seat receiver, HashSet<Region> fed, HashSet<Region> relieved)
    {
        if (term.Source is null) return;
        HashSet<Region> reached = Reached(board, board.Region(term.Source), giver, receiver);
        Region? hungry = board.Regions
            .Where(region => region.Allegiance == receiver.Id && !fed.Contains(region) && !relieved.Contains(region) && reached.Contains(region))
            .OrderByDescending(region => Conditions.ProsperityIncome(region) + region.Control)
            .FirstOrDefault();
        if (hungry is not null) relieved.Add(hungry);
    }

    private static bool Due(Board board, TradeTerm term, Agreement deal) =>
        term.Resource is not null && board.Turn > deal.Start && (board.Turn - deal.Start) % term.EveryWeeks == 0;

    private static bool Short(Board board, TradeTerm term, Seat giver, Agreement deal) =>
        Due(board, term, deal) && giver.Amount(term.Resource!.Value) < term.Amount;

    private static void Pay(Board board, TradeTerm term, Seat giver, Seat receiver, Agreement deal)
    {
        if (!Due(board, term, deal)) return;
        board.AdjustResource(giver, term.Resource!.Value, -term.Amount);
        board.AdjustResource(receiver, term.Resource.Value, term.Amount);
    }

    private static int Units(TradeTerm term) => term.Amount * Horizon / term.EveryWeeks;

    private static int UnitValue(Seat seat, Resource resource) => seat.Amount(resource) switch { < 3 => 3, < 6 => 2, _ => 1 };

    /// <summary>How much <paramref name="seat"/> needs one more ration a week, besides those its other deals bring.</summary>
    private static int Hunger(Board board, Seat seat, Agreement deal)
    {
        Region[] held = [.. board.Regions.Where(region => region.Allegiance == seat.Id)];
        int rations = board.Agreements.Count(other => other != deal && other.Binds(seat.Id) && other.ReceivedBy(seat.Id).IsGrain);
        if (held.Count(region => region.Supply != RegionSupply.Fed) > rations) return 3;
        return held.Any(region => region.Prosperity < Supply.WinterHarvest) ? 1 : 0;
    }
}
