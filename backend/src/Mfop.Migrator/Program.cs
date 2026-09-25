namespace Mfop.Migrator;

// Usage:
//   Mfop.Migrator migrate                 apply migrations + grants (connects as the owner role)
//   Mfop.Migrator migrate --seed-dev FILE also load dev bootstrap users/roles from FILE (dev profile only)
//   Mfop.Migrator migrate --reference-data FILE   load diagnosis codes and checklist templates (audited, idempotent)
//   Mfop.Migrator seed-demo FILE          DEV ONLY: synthetic DEMO 1 data through the application's commands (make seed)
// Connection string: env ConnectionStrings__Owner (owner role, e.g. mfop_owner).
internal static class MigratorProgram
{
    public static async Task<int> Main(string[] args)
    {


        var ownerConnection = Environment.GetEnvironmentVariable("ConnectionStrings__Owner");
        if (string.IsNullOrWhiteSpace(ownerConnection))
        {
            Console.Error.WriteLine("ConnectionStrings__Owner is required.");
            return 2;
        }

        if (args.Length >= 2 && args[0] == "seed-demo")
        {
            var configuration = Microsoft.Extensions.Configuration.EnvironmentVariablesExtensions.AddEnvironmentVariables(new Microsoft.Extensions.Configuration.ConfigurationBuilder()).Build();
            await new DemoSeeder(ownerConnection, configuration, Console.Out).SeedAsync(args[1], CancellationToken.None);
            Console.WriteLine("Done.");
            return 0;
        }

        if (args.Length == 0 || args[0] != "migrate")
        {
            Console.Error.WriteLine("Usage: migrate [--seed-dev <dev-users.json>] [--reference-data <reference-data.json>] | seed-demo <demo-data.json>");
            return 2;
        }

        string? seedFile = null, referenceFile = null;
        for (var i = 1; i < args.Length; i++)
        {
            if (args[i] == "--seed-dev" && i + 1 < args.Length)
            {
                seedFile = args[++i];
            }
            else if (args[i] == "--reference-data" && i + 1 < args.Length)
            {
                referenceFile = args[++i];
            }
        }

        var runner = new MigrationRunner(ownerConnection, Console.Out);
        await runner.MigrateAsync(CancellationToken.None);

        if (referenceFile is not null)
        {
            await runner.LoadReferenceDataAsync(referenceFile, CancellationToken.None);
        }

        if (seedFile is not null)
        {
            await runner.SeedDevUsersAsync(seedFile, CancellationToken.None);
        }

        Console.WriteLine("Done.");
        return 0;
    }
}
