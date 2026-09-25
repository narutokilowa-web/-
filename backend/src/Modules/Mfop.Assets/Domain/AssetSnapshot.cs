using System.Globalization;
using System.Text.Json.Nodes;
using Mfop.Kernel.Json;

namespace Mfop.Assets.Domain;

/// <summary>
/// The versioned (official) attributes of a street light, as a flat object of strings/nulls.
/// Rendering rules (tools/audit-verify implements the same rules independently; a test compares them):
/// decimals without trailing zeros (invariant), integers as decimal strings, dates yyyy-MM-dd.
/// Operational status is NOT part of the snapshot (it changes through work orders, not approvals).
/// </summary>
public static class AssetSnapshot
{
    public static JsonObject Build(StreetLight a, string tagCode) => new()
    {
        ["asset_code"] = a.AssetCode,
        ["lifecycle_state"] = a.LifecycleState,
        ["latitude"] = Dec(a.Latitude),
        ["longitude"] = Dec(a.Longitude),
        ["address_text"] = a.AddressText,
        ["circuit_ref"] = a.CircuitRef,
        ["power_source"] = a.PowerSource,
        ["pole_material"] = a.PoleMaterial,
        ["pole_height_m"] = a.PoleHeightM is { } h ? Dec(h) : null,
        ["luminaire_make"] = a.LuminaireMake,
        ["luminaire_model"] = a.LuminaireModel,
        ["luminaire_wattage_w"] = Int(a.LuminaireWattageW),
        ["luminaire_serial"] = a.LuminaireSerial,
        ["solar_panel_w"] = Int(a.SolarPanelW),
        ["solar_battery_type"] = a.SolarBatteryType,
        ["solar_battery_ah"] = Int(a.SolarBatteryAh),
        ["solar_controller_model"] = a.SolarControllerModel,
        ["installed_on"] = Date(a.InstalledOn),
        ["installed_on_precision"] = a.InstalledOnPrecision,
        ["warranty_until"] = Date(a.WarrantyUntil),
        ["supplier"] = a.Supplier,
        ["responsible_party"] = a.ResponsibleParty,
        ["responsible_entity_name"] = a.ResponsibleEntityName,
        ["tag_code"] = tagCode,
    };

    public static string Hash(JsonObject snapshot) => CanonicalJson.Sha256Hex(snapshot);

    public static string Serialize(JsonObject snapshot) => System.Text.Encoding.UTF8.GetString(CanonicalJson.Serialize(snapshot));

    private static string Dec(decimal d) => d.ToString("0.############################", CultureInfo.InvariantCulture);

    private static string? Int(int? i) => i?.ToString(CultureInfo.InvariantCulture);

    private static string? Date(DateOnly? d) => d?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
