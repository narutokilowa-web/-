namespace Mfop.Evidence.Application;

public sealed class EvidenceOptions
{
    /// <summary>Largest accepted photo (06 §6.2: ≤ 15 MB).</summary>
    public long MaxBytes { get; set; } = 15 * 1024 * 1024;

    /// <summary>How often the in-process verifier looks for uploaded photos (DEMO 1: hosted service inside the API).</summary>
    public double VerifyIntervalSeconds { get; set; } = 2;

    /// <summary>Longest side of the derived preview (EXIF stripped) shown on the web.</summary>
    public int PreviewMaxPixels { get; set; } = 1280;

    public int ThumbnailMaxPixels { get; set; } = 320;

    /// <summary>Set false to run verification only on demand (the migrator's demo seed calls it directly).</summary>
    public bool RunBackgroundVerifier { get; set; } = true;
}
