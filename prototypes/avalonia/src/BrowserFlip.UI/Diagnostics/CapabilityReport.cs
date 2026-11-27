using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrowserFlip.UI.Diagnostics;

/// <summary>Outcome of one capability check, using the round's vocabulary.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<CheckResult>))]
public enum CheckResult
{
    /// <summary>Verified on this machine, at run time.</summary>
    PASS,

    /// <summary>Checked and did not work.</summary>
    FAIL,

    /// <summary>Deliberately not executed (for example, a real system change).</summary>
    SKIPPED,

    /// <summary>Not available on this OS or OS version.</summary>
    UNSUPPORTED,

    /// <summary>
    /// Started, not finished, and the outcome is genuinely unknown: the OS has not answered
    /// yet. Distinct from FAIL on purpose. A macOS consent alert belongs to
    /// <c>CoreServicesUIAgent</c>, stays open until the user answers it, outlives this process
    /// and the change lands afterwards - measured at 44 s and 474 s on this Mac. Grading that
    /// FAIL printed the opposite of what the system then did.
    /// </summary>
    PENDING,
}

/// <summary>One line of the self-test report.</summary>
public sealed record CapabilityCheck(int Id, string Name, CheckResult Result, string Evidence);

/// <summary>
/// The self-test report. The prototype writes it while actually running, so the evidence comes
/// from the real application on the real OS rather than from a build log.
/// </summary>
public sealed class CapabilityReport
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Platform { get; set; } = string.Empty;

    public string OsVersion { get; set; } = string.Empty;

    public string Architecture { get; set; } = string.Empty;

    public string AvaloniaVersion { get; set; } = string.Empty;

    public string Timestamp { get; set; } = string.Empty;

    public string? RenderedWindowPng { get; set; }

    public List<CapabilityCheck> Checks { get; } = [];

    public void Add(int id, string name, CheckResult result, string evidence)
        => Checks.Add(new CapabilityCheck(id, name, result, evidence));

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <summary>A compact table for the terminal, in the round's PASS/FAIL/SKIPPED/UNSUPPORTED form.</summary>
    public string ToTable()
    {
        var lines = new List<string>
        {
            new('-', 100),
            $" BrowserFlip Avalonia prototype — self-test on {Platform} {OsVersion} ({Architecture}), Avalonia {AvaloniaVersion}",
            new('-', 100),
        };
        foreach (var c in Checks)
        {
            lines.Add($" {c.Result,-12} {c.Id,2}. {c.Name,-44} {c.Evidence}");
        }

        lines.Add(new('-', 100));
        var fails = Checks.Count(c => c.Result == CheckResult.FAIL);
        var pending = Checks.Count(c => c.Result == CheckResult.PENDING);
        // A pending check is not a failing one, and it is not a passing one either: the overall
        // verdict says so rather than rounding it to whichever is convenient.
        lines.Add($" RESULT: {(fails > 0 ? $"FAIL ({fails} failing)" : pending > 0 ? $"PASS with {pending} PENDING" : "PASS")}   "
                  + $"pass={Checks.Count(c => c.Result == CheckResult.PASS)} "
                  + $"pending={pending} "
                  + $"skipped={Checks.Count(c => c.Result == CheckResult.SKIPPED)} "
                  + $"unsupported={Checks.Count(c => c.Result == CheckResult.UNSUPPORTED)}");
        lines.Add(new('-', 100));
        return string.Join(Environment.NewLine, lines);
    }
}
