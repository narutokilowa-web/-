using Mfop.Kernel.Results;

namespace Mfop.Assets.Domain;

public static class PowerSources
{
    public const string Grid = "GRID", Solar = "SOLAR", Hybrid = "HYBRID";
    public static readonly string[] All = [Grid, Solar, Hybrid];
}

public static class InstallDatePrecisions
{
    public const string Exact = "EXACT", Year = "YEAR", Unknown = "UNKNOWN";
    public static readonly string[] All = [Exact, Year, Unknown];
}

/// <summary>Who is responsible for the asset. Configurable data, not hard-coded ownership (docs/spec/17).</summary>
public static class ResponsibleParties
{
    public const string Municipality = "MUNICIPALITY", ElectricityAuthority = "ELECTRICITY_AUTHORITY", OtherPublicEntity = "OTHER_PUBLIC_ENTITY", Shared = "SHARED";
    public static readonly string[] All = [Municipality, ElectricityAuthority, OtherPublicEntity, Shared];
}

public static class RegistrationReasons
{
    public const string BaselineSurvey = "BASELINE_SURVEY", NewInstallation = "NEW_INSTALLATION", MissedInSurvey = "MISSED_IN_SURVEY";
    public static readonly string[] All = [BaselineSurvey, NewInstallation, MissedInSurvey];
}

public static class ObservedStatuses
{
    public static readonly string[] All = [OperationalStatuses.Operational, OperationalStatuses.Faulty, OperationalStatuses.Unknown];
}

/// <summary>Street-light attributes entered by the registrar (web form in DEMO 1; mobile capture in M7).</summary>
public sealed record StreetLightAttributes(
    decimal Latitude,
    decimal Longitude,
    string PowerSource,
    string ResponsibleParty,
    string InstalledOnPrecision,
    string ObservedStatus,
    string? AddressText = null,
    string? CircuitRef = null,
    string? PoleMaterial = null,
    decimal? PoleHeightM = null,
    string? LuminaireMake = null,
    string? LuminaireModel = null,
    int? LuminaireWattageW = null,
    string? LuminaireSerial = null,
    int? SolarPanelW = null,
    string? SolarBatteryType = null,
    int? SolarBatteryAh = null,
    string? SolarControllerModel = null,
    DateOnly? InstalledOn = null,
    DateOnly? WarrantyUntil = null,
    string? Supplier = null,
    string? ResponsibleEntityName = null)
{
    public Result<StreetLightAttributes> Validate()
    {
        var errors = new Dictionary<string, string>();

        if (Latitude is < -90 or > 90) errors["latitude"] = "must be between -90 and 90";
        if (Longitude is < -180 or > 180) errors["longitude"] = "must be between -180 and 180";
        if (!HasAtMostDecimals(Latitude, 6)) errors["latitude"] = "at most 6 decimal places";
        if (!HasAtMostDecimals(Longitude, 6)) errors["longitude"] = "at most 6 decimal places";
        if (!PowerSources.All.Contains(PowerSource)) errors["powerSource"] = "GRID, SOLAR or HYBRID";
        if (!ResponsibleParties.All.Contains(ResponsibleParty)) errors["responsibleParty"] = string.Join(", ", ResponsibleParties.All);
        if (!InstallDatePrecisions.All.Contains(InstalledOnPrecision)) errors["installedOnPrecision"] = "EXACT, YEAR or UNKNOWN";
        if (!ObservedStatuses.All.Contains(ObservedStatus)) errors["observedStatus"] = "OPERATIONAL, FAULTY or UNKNOWN";
        if (InstalledOnPrecision == InstallDatePrecisions.Unknown && InstalledOn is not null) errors["installedOn"] = "must be empty when precision is UNKNOWN";
        if (InstalledOnPrecision != InstallDatePrecisions.Unknown && InstalledOn is null) errors["installedOn"] = "required unless precision is UNKNOWN";
        if (InstalledOnPrecision == InstallDatePrecisions.Year && InstalledOn is { } d && (d.Month != 1 || d.Day != 1)) errors["installedOn"] = "use January 1st of the year when precision is YEAR";
        if (PoleHeightM is { } h && (h <= 0 || h > 30 || !HasAtMostDecimals(h, 1))) errors["poleHeightM"] = "between 0 and 30 m, one decimal";
        if (LuminaireWattageW is { } w && (w <= 0 || w > 2000)) errors["luminaireWattageW"] = "between 1 and 2000";
        var isSolar = PowerSource is PowerSources.Solar or PowerSources.Hybrid;
        if (!isSolar && (SolarPanelW is not null || SolarBatteryType is not null || SolarBatteryAh is not null || SolarControllerModel is not null))
        {
            errors["solar"] = "solar fields only for SOLAR or HYBRID";
        }

        if (SolarPanelW is { } p && (p <= 0 || p > 5000)) errors["solarPanelW"] = "between 1 and 5000";
        if (SolarBatteryAh is { } ah && (ah <= 0 || ah > 5000)) errors["solarBatteryAh"] = "between 1 and 5000";

        foreach (var (name, value, max) in new[]
        {
            ("addressText", AddressText, 300), ("circuitRef", CircuitRef, 50), ("poleMaterial", PoleMaterial, 50),
            ("luminaireMake", LuminaireMake, 100), ("luminaireModel", LuminaireModel, 100), ("luminaireSerial", LuminaireSerial, 100),
            ("solarBatteryType", SolarBatteryType, 50), ("solarControllerModel", SolarControllerModel, 100), ("supplier", Supplier, 200),
            ("responsibleEntityName", ResponsibleEntityName, 200),
        })
        {
            if (value is not null && value.Length > max) errors[name] = $"at most {max} characters";
        }

        return errors.Count == 0 ? this : Error.Validation("INVALID_ATTRIBUTES", "Some attributes are invalid.", errors);
    }

    /// <summary>By value, not by representation: 8.50 has one significant decimal.</summary>
    private static bool HasAtMostDecimals(decimal value, int decimals)
    {
        var factor = (decimal)Math.Pow(10, decimals);
        var scaled = value * factor;
        return scaled == decimal.Truncate(scaled);
    }
}
