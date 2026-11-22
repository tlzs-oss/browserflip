using System.Text.Json;
using System.Text.Json.Serialization;

namespace BrowserFlip.UI.Diagnostics;

/// <summary>Outcome of one capability check, using the round's four-way vocabulary.</summary>
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
        lines.Add($" RESULT: {(fails == 0 ? "PASS" : $"FAIL ({fails} failing)")}   "
                  + $"pass={Checks.Count(c => c.Result == CheckResult.PASS)} "
                  + $"skipped={Checks.Count(c => c.Result == CheckResult.SKIPPED)} "
                  + $"unsupported={Checks.Count(c => c.Result == CheckResult.UNSUPPORTED)}");
        lines.Add(new('-', 100));
        return string.Join(Environment.NewLine, lines);
    }
}
