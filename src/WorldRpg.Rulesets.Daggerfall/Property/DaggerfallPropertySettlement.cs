using WorldRpg.Rulesets.Daggerfall.Banking;
using WorldRpg.Kit.Inventory;

namespace WorldRpg.Rulesets.Daggerfall.Property;

/// <summary>
/// Settles property transactions through the existing regional bank and currency owners. Property
/// state never keeps a second wallet or account balance of its own.
/// </summary>
internal sealed class DaggerfallPropertyBankSettlementAdapter : IDaggerfallPropertyBankSettlement
{
    private readonly DaggerfallRegionalBankState _bank;
    private readonly DaggerfallCurrencyService _currency;

    internal DaggerfallPropertyBankSettlementAdapter(DaggerfallRegionalBankState bank,
        DaggerfallCurrencyService currency)
    {
        _bank = bank ?? throw new ArgumentNullException(nameof(bank));
        _currency = currency ?? throw new ArgumentNullException(nameof(currency));
    }

    public DaggerfallPropertyPaymentResult Apply(DaggerfallPropertyBankRequest request)
    {
        try
        {
            request.Validate();
        }
        catch (ArgumentOutOfRangeException exception)
        {
            return DaggerfallPropertyPaymentResult.Refused(
                exception.ParamName == nameof(DaggerfallPropertyBankRequest.Amount)
                    ? DaggerfallPropertyPaymentDenial.InvalidAmount
                    : DaggerfallPropertyPaymentDenial.InvalidRegion);
        }
        catch (ArgumentException)
        {
            return DaggerfallPropertyPaymentResult.Refused(DaggerfallPropertyPaymentDenial.Rejected);
        }

        if (request.Operation == DaggerfallPropertyBankOperation.Sale)
        {
            return _bank.TryCreditAccount(request.Region, request.Amount)
                ? DaggerfallPropertyPaymentResult.Accepted()
                : DaggerfallPropertyPaymentResult.Refused(DaggerfallPropertyPaymentDenial.Rejected);
        }

        // The donor spends carried gold first and uses the current regional account for the shortfall.
        // Reserve the account leg before consuming inventory so a failed carried-gold publication can
        // be rolled back without inventing a reverse wallet operation.
        ulong carriedDebit = Math.Min(_currency.Read().Gold, request.Amount);
        ulong accountDebit = request.Amount - carriedDebit;
        if (_bank.BalanceForRegion(request.Region) < accountDebit)
            return DaggerfallPropertyPaymentResult.Refused(DaggerfallPropertyPaymentDenial.InsufficientFunds);

        if (accountDebit != 0 && !_bank.TryDebitAccount(request.Region, accountDebit))
            return DaggerfallPropertyPaymentResult.Refused(DaggerfallPropertyPaymentDenial.Rejected);

        if (carriedDebit == 0 || _currency.TrySpendGold(carriedDebit, Array.Empty<InventoryAtomicGrant>()))
            return DaggerfallPropertyPaymentResult.Accepted();

        if (accountDebit != 0 && !_bank.TryCreditAccount(request.Region, accountDebit))
            throw new InvalidOperationException("Property purchase payment could not restore its regional account debit.");
        return DaggerfallPropertyPaymentResult.Refused(DaggerfallPropertyPaymentDenial.Rejected);
    }
}
