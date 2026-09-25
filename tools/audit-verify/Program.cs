using Mfop.AuditVerify;

// audit-verify — standalone tamper detection for the MFOP audit trail (ADR-0007).
// Usage: audit-verify --db "<connection string, ideally the read-only role mfop_readonly>"
//        (or environment variable MFOP_VERIFY_DB)
// Exit codes: 0 = verified, 1 = integrity findings, 2 = usage or connection error.

string? connection = null;
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--db" && i + 1 < args.Length)
    {
        connection = args[++i];
    }
}

connection ??= Environment.GetEnvironmentVariable("MFOP_VERIFY_DB");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("Usage: audit-verify --db \"Host=...;Database=mfop;Username=mfop_readonly;Password=...\"");
    return 2;
}

VerificationReport report;
try
{
    report = await Verifier.RunAsync(connection);
}
catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException or System.Net.Sockets.SocketException)
{
    Console.Error.WriteLine($"audit-verify: cannot read the database: {ex.Message}");
    return 2;
}

Console.WriteLine($"audit-verify: {report.EventsChecked} audit events, {report.VersionsChecked} asset versions, {report.AssetsChecked} active assets, " +
                  $"{report.WorkOrdersChecked} closed work-order seals, {report.MovementsChecked} stock movements checked (balances, in-transit documents, core bin).");
if (report.Ok)
{
    Console.WriteLine("RESULT: OK — no tampering detected.");
    return 0;
}

var first = report.Findings[0];
Console.WriteLine($"RESULT: INTEGRITY FAILURE — {report.Findings.Count} finding(s).");
Console.WriteLine($"FIRST: {first.Code} seq={first.Seq?.ToString() ?? "-"} entity={first.EntityType}/{first.EntityId}: {first.Message}");
foreach (var f in report.Findings.Skip(1).Take(50))
{
    Console.WriteLine($"  - {f.Code} seq={f.Seq?.ToString() ?? "-"} entity={f.EntityType}/{f.EntityId}: {f.Message}");
}

return 1;
