using Mfop.Access.Contracts;
using System.Text.Json.Nodes;
using Mfop.Access.Domain;
using Mfop.Assets.Domain;

namespace Mfop.Domain.Tests;

public class AssetsDomainTests
{
    private static StreetLightAttributes Valid(string power = PowerSources.Grid) =>
        new(33.5138m, 36.2765m, power, ResponsibleParties.Municipality, InstallDatePrecisions.Year, OperationalStatuses.Operational,
            InstalledOn: new DateOnly(2019, 1, 1), PoleHeightM: 8.50m);

    [Fact]
    public void Valid_attributes_pass()
    {
        Assert.True(Valid().Validate().IsSuccess);
        Assert.True((Valid(PowerSources.Solar) with { SolarPanelW = 100, SolarBatteryAh = 120 }).Validate().IsSuccess);
    }

    [Theory]
    [InlineData("lat")]
    [InlineData("precision")]
    [InlineData("yearNotJan1")]
    [InlineData("unknownWithDate")]
    [InlineData("solarOnGrid")]
    [InlineData("power")]
    [InlineData("responsible")]
    [InlineData("height")]
    public void Invalid_attributes_fail(string kind)
    {
        var a = kind switch
        {
            "lat" => Valid() with { Latitude = 91m },
            "precision" => Valid() with { Longitude = 36.1234567m },
            "yearNotJan1" => Valid() with { InstalledOn = new DateOnly(2019, 5, 3) },
            "unknownWithDate" => Valid() with { InstalledOnPrecision = InstallDatePrecisions.Unknown },
            "solarOnGrid" => Valid() with { SolarPanelW = 100 },
            "power" => Valid() with { PowerSource = "WIND" },
            "responsible" => Valid() with { ResponsibleParty = "NOBODY" },
            "height" => Valid() with { PoleHeightM = 8.55m },
            _ => throw new ArgumentException(kind),
        };
        var result = a.Validate();
        Assert.False(result.IsSuccess);
        Assert.Equal("INVALID_ATTRIBUTES", result.Error!.Code);
    }

    [Fact]
    public void Registration_enforces_registrar_only_and_maker_checker()
    {
        var registrar = Guid.NewGuid();
        var approver = Guid.NewGuid();
        var registration = new Registration(Guid.NewGuid(), Guid.NewGuid(), registrar, RegistrationReasons.BaselineSurvey, null, "WEB", DateTimeOffset.UtcNow);

        Assert.Equal("NOT_REGISTRAR", registration.Transition(AssetCommands.SubmitRegistration, approver, false, DateTimeOffset.UtcNow).Error!.Code);
        Assert.True(registration.Transition(AssetCommands.SubmitRegistration, registrar, false, DateTimeOffset.UtcNow).IsSuccess);
        Assert.Equal("SOD_VIOLATION", registration.Transition(AssetCommands.ApproveRegistration, registrar, true, DateTimeOffset.UtcNow).Error!.Code);
        Assert.Equal("FORBIDDEN", registration.Transition(AssetCommands.ApproveRegistration, approver, false, DateTimeOffset.UtcNow).Error!.Code);
        Assert.True(registration.Transition(AssetCommands.ApproveRegistration, approver, true, DateTimeOffset.UtcNow, "ok").IsSuccess);
        Assert.Equal(RegistrationStates.Approved, registration.State);
        Assert.Equal(approver, registration.DecidedBy);
        Assert.Equal("INVALID_TRANSITION", registration.Transition(AssetCommands.SubmitRegistration, registrar, false, DateTimeOffset.UtcNow).Error!.Code);
    }

    [Fact]
    public void New_installation_requires_a_project_reference()
    {
        Assert.Equal("PROJECT_REF_REQUIRED", Registration.ValidateReason(RegistrationReasons.NewInstallation, null).Error!.Code);
        Assert.True(Registration.ValidateReason(RegistrationReasons.NewInstallation, "P-2026-01").IsSuccess);
        Assert.Equal("INVALID_REASON", Registration.ValidateReason("WHATEVER", null).Error!.Code);
    }

    [Fact]
    public void Activation_follows_lifecycle_and_observed_status()
    {
        var light = new StreetLight(Guid.NewGuid(), Valid() with { ObservedStatus = OperationalStatuses.Faulty }, "WEB", DateTimeOffset.UtcNow);
        Assert.Equal(LifecycleStates.Pending, light.LifecycleState);
        Assert.Null(light.OperationalStatus);
        light.Activate("SL-000001", Guid.NewGuid(), 1);
        Assert.Equal(LifecycleStates.Active, light.LifecycleState);
        Assert.Equal(OperationalStatuses.Faulty, light.OperationalStatus);
        Assert.Throws<InvalidOperationException>(() => light.Activate("SL-000002", Guid.NewGuid(), 2));
    }

    [Fact]
    public void Snapshot_renders_values_canonically()
    {
        var light = new StreetLight(Guid.NewGuid(), Valid(), "WEB", DateTimeOffset.UtcNow);
        light.Activate("SL-000009", Guid.NewGuid(), 1);
        var snapshot = AssetSnapshot.Build(light, "7K3M92QX");
        Assert.Equal("8.5", snapshot["pole_height_m"]!.GetValue<string>());      // 8.50 → 8.5
        Assert.Equal("2019-01-01", snapshot["installed_on"]!.GetValue<string>());
        Assert.Null(snapshot["solar_panel_w"]);
        Assert.Equal(AssetSnapshot.Hash(snapshot), AssetSnapshot.Hash(JsonNode.Parse(AssetSnapshot.Serialize(snapshot))!.AsObject()));
        Assert.DoesNotContain("operational_status", snapshot.Select(p => p.Key));
    }

    [Fact]
    public void Tag_codes_have_a_keyed_check_code_and_parse_leniently()
    {
        var code = TagCodes.NewCode();
        Assert.Matches("^[0-9A-HJKMNP-TV-Z]{8}$", code);
        var check = TagCodes.CheckCode(code, "key-1");
        Assert.Equal(check, TagCodes.CheckCode(code, "key-1"));
        Assert.Equal(2, check.Length);
        var parsed = TagCodes.Parse($" {code[..4].ToLowerInvariant()}-{code[4..]} .{check} ");
        Assert.Equal((code, check), parsed);
        Assert.Null(TagCodes.Parse("TOO-SHORT"));
        Assert.Null(TagCodes.Parse("IIIIIIII.OO")); // I and O are not in the alphabet
    }

    [Fact]
    public void Granting_roles_enforces_static_separation_of_duties_and_no_self_grant()
    {
        var user = new User(Guid.NewGuid(), "sub", "u", "U", DateTimeOffset.UtcNow);
        var admin = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        Assert.NotNull(user.GrantRole(RoleCodes.Registrar, admin, "test", now).Value);
        Assert.Null(user.GrantRole(RoleCodes.Registrar, admin, "test", now).Value); // idempotent
        Assert.Equal("SOD_ROLE_CONFLICT", user.GrantRole(RoleCodes.Approver, admin, "test", now).Error!.Code);
        Assert.Equal("SOD_VIOLATION", user.GrantRole(RoleCodes.Supervisor, user.Id, "test", now).Error!.Code);
    }
}
