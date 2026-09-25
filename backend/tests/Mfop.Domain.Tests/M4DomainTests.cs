using Mfop.Access.Domain;
using Mfop.Sync.Domain;

namespace Mfop.Domain.Tests;

public class M4DomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("0.4.0", "0.3.9", true)]
    [InlineData("0.10.0", "0.9.9", true)]
    [InlineData("1.0", "0.99.99", true)]
    [InlineData("0.4.0", "0.4.0", false)]
    [InlineData("0.4.0+12", "0.4.0", false)]
    public void App_versions_compare_numerically(string a, string b, bool greater)
    {
        Assert.True(AppVersion.TryParse(a, out var va));
        Assert.True(AppVersion.TryParse(b, out var vb));
        Assert.Equal(greater, va > vb);
    }

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    [InlineData("1.x")]
    [InlineData(null)]
    public void Malformed_versions_are_rejected(string? value) => Assert.False(AppVersion.TryParse(value, out _));

    [Theory]
    [InlineData("offline_grace_hours", "24", true)]
    [InlineData("offline_grace_hours", "1", true)]
    [InlineData("offline_grace_hours", "72", true)]
    [InlineData("offline_grace_hours", "0", false)]
    [InlineData("offline_grace_hours", "73", false)]
    [InlineData("offline_grace_hours", "12.5", false)]
    [InlineData("grant_window_tolerance_minutes", "0", true)]
    [InlineData("grant_window_tolerance_minutes", "61", false)]
    [InlineData("app_lock_minutes", "31", false)]
    [InlineData("min_app_version", "0.4.0", true)]
    [InlineData("min_app_version", "four", false)]
    public void Policy_values_are_validated_against_their_allowed_range(string key, string value, bool valid) =>
        Assert.Equal(valid, PolicyKeys.Find(key)!.Validate(value) is null);

    [Fact]
    public void Policy_defaults_are_the_documented_pilot_values()
    {
        Assert.Equal("24", PolicyKeys.Find(PolicyKeys.OfflineGraceHours)!.Default);
        Assert.Equal("10", PolicyKeys.Find(PolicyKeys.GrantWindowToleranceMinutes)!.Default);
        Assert.Equal("5", PolicyKeys.Find(PolicyKeys.AppLockMinutes)!.Default);
        Assert.All(PolicyKeys.All, p => Assert.Null(p.Validate(p.Default)));
    }

    [Fact]
    public void A_user_is_disabled_by_someone_else_with_a_reason_and_enabled_again_the_same_way()
    {
        var user = new User(Guid.NewGuid(), "sub", "tech9", "Tech Nine", Now);
        var admin = Guid.NewGuid();
        Assert.Equal("SOD_VIOLATION", user.Disable(user.Id, "x", Now).Error!.Code);
        Assert.Equal("REASON_REQUIRED", user.Disable(admin, " ", Now).Error!.Code);
        Assert.True(user.Disable(admin, "انتهاء العقد", Now).IsSuccess);
        Assert.Equal(UserStates.Disabled, user.StateName);
        Assert.Equal("INVALID_TRANSITION", user.Disable(admin, "again", Now).Error!.Code);
        Assert.Equal("SOD_VIOLATION", user.Enable(user.Id, "x").Error!.Code);
        Assert.True(user.Enable(admin, "عاد").IsSuccess);
        Assert.Equal(UserStates.Active, user.StateName);
        Assert.Null(user.DisableReason);
    }

    [Fact]
    public void Identity_provider_tasks_back_off_and_raise_an_alert_after_repeated_failures()
    {
        var task = new IdpTask(Guid.NewGuid(), IdpTaskKinds.LogoutAll, Guid.NewGuid(), "sub", Now);
        var previous = Now;
        for (var i = 1; i < IdpTask.AlertAfterAttempts; i++)
        {
            task.Failed("unreachable", Now);
            Assert.True(task.NextAttemptAt > previous);
            previous = task.NextAttemptAt;
            Assert.False(task.Alert);
        }

        task.Failed("unreachable", Now);
        Assert.True(task.Alert);
        task.Succeeded(Now.AddHours(1));
        Assert.NotNull(task.DoneAt);
    }

    [Fact]
    public void A_quarantined_command_is_decided_once_by_someone_other_than_its_author_with_a_reason()
    {
        var author = Guid.NewGuid();
        QuarantinedCommand Entry() => new(Guid.NewGuid(), Guid.NewGuid(), "ReportFault", "{}", author, ["SUPERVISOR"], Guid.NewGuid(), "GRANT_WINDOW_UNPROVEN",
            "x", null, Now, "LOW", Now, Guid.NewGuid(), null);
        var q = Entry();
        Assert.Equal("SOD_VIOLATION", q.Resolve(QuarantineCommands.DiscardQuarantined, author, "x", Now).Error!.Code);
        Assert.Equal("REASON_REQUIRED", q.Resolve(QuarantineCommands.DiscardQuarantined, Guid.NewGuid(), "", Now).Error!.Code);
        Assert.True(q.Resolve(QuarantineCommands.DiscardQuarantined, Guid.NewGuid(), "مكرر", Now).IsSuccess);
        Assert.Equal(QuarantineStates.Discarded, q.State);
        Assert.Equal("INVALID_TRANSITION", q.Resolve(QuarantineCommands.ApplyQuarantined, Guid.NewGuid(), "x", Now).Error!.Code);
    }
}

public class GrantSignerTests
{
    private static Mfop.Access.Application.GrantSigner Signer(string? key) => new(
        Microsoft.Extensions.Options.Options.Create(new Mfop.Access.Application.GrantSigningOptions { SigningKey = key, KeyId = "k-test" }),
        Microsoft.Extensions.Logging.Abstractions.NullLogger<Mfop.Access.Application.GrantSigner>.Instance);

    /// <summary>`make env` keys: PKCS#8 (documented) and SEC1 (earlier `openssl genpkey -outform DER` output) both load.</summary>
    [Theory]
    [InlineData("pkcs8")]
    [InlineData("sec1")]
    public void The_configured_grant_key_is_loaded_and_its_signatures_verify(string format)
    {
        using var original = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP256);
        var der = format == "pkcs8" ? original.ExportPkcs8PrivateKey() : original.ExportECPrivateKey();
        using var signer = Signer(Convert.ToBase64String(der));
        Assert.Equal("k-test", signer.KeyId);
        Assert.Equal(Convert.ToBase64String(original.ExportSubjectPublicKeyInfo()), signer.PublicKey);
        var payload = "{\"grant_id\":\"x\"}"u8.ToArray();
        Assert.True(original.VerifyData(payload, Convert.FromBase64String(signer.Sign(payload)),
            System.Security.Cryptography.HashAlgorithmName.SHA256, System.Security.Cryptography.DSASignatureFormat.Rfc3279DerSequence));
    }

    [Fact]
    public void A_key_on_another_curve_is_refused()
    {
        using var p384 = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        Assert.ThrowsAny<System.Security.Cryptography.CryptographicException>(() => Signer(Convert.ToBase64String(p384.ExportPkcs8PrivateKey())));
    }
}
