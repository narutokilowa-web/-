using System.Text.RegularExpressions;

namespace Mfop.Kernel;

/// <summary>
/// Money is always amount + ISO 4217 currency + the date the amount refers to.
/// No implicit conversion exists anywhere (docs/spec/17 §17.6-d). The default operational currency is configuration (SYP).
/// </summary>
public readonly partial record struct Money
{
    public Money(decimal amount, string currency, DateOnly asOf)
    {
        if (!CurrencyPattern().IsMatch(currency ?? string.Empty))
        {
            throw new ArgumentException($"'{currency}' is not an ISO 4217 alphabetic code.", nameof(currency));
        }

        Amount = amount;
        Currency = currency!;
        AsOf = asOf;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public DateOnly AsOf { get; }

    public Money Add(Money other)
    {
        if (other.Currency != Currency)
        {
            throw new InvalidOperationException($"Cannot add {other.Currency} to {Currency}: no implicit conversion.");
        }

        return new Money(Amount + other.Amount, Currency, AsOf > other.AsOf ? AsOf : other.AsOf);
    }

    [GeneratedRegex("^[A-Z]{3}$")]
    private static partial Regex CurrencyPattern();
}
