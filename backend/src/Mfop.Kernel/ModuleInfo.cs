namespace Mfop.Kernel;

/// <summary>
/// Identifies a backend module and the PostgreSQL schema it owns.
/// Every module exposes one static instance so hosts and architecture tests can enumerate modules.
/// </summary>
public sealed record ModuleInfo(string Name, string Schema);
