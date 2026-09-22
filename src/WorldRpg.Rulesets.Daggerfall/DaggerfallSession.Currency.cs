using WorldRpg.Rulesets.Daggerfall.Presentation;

namespace WorldRpg.Rulesets.Daggerfall;

internal sealed partial class DaggerfallSession
{
    private void ChangeCurrency(DaggerfallPlayerUiAction action)
    {
        bool changed = action.Action switch
        {
            "currency-deposit-gold" => State.Currency.DepositGold(action.Amount.GetValueOrDefault()),
            "currency-withdraw-gold" => State.Currency.WithdrawGold(action.Amount.GetValueOrDefault()),
            "currency-deposit-letters" => State.Currency.DepositLetters(),
            "currency-withdraw-letter" => State.Currency.WithdrawLetter(action.Amount.GetValueOrDefault()),
            _ => throw new ArgumentException($"'{action.Action}' is not a currency operation.", nameof(action)),
        };
        Presentation.SetOutcome(changed ? "Currency updated." : "Currency operation was not accepted.");
    }
}
