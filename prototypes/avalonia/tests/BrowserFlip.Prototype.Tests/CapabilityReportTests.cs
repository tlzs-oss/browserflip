using BrowserFlip.UI.Diagnostics;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// How the self-test report grades a run. The point of these is the three-way distinction: a
/// check that has not finished is neither a pass nor a failure, and an earlier version that had
/// only PASS/FAIL printed "FAIL" over a change macOS went on to apply.
/// </summary>
public class CapabilityReportTests
{
    private static CapabilityReport Report(params (int Id, CheckResult Result)[] checks)
    {
        var report = new CapabilityReport { Platform = "test", OsVersion = "test", Architecture = "arm64" };
        foreach (var (id, result) in checks)
        {
            report.Add(id, $"check {id}", result, "evidence");
        }

        return report;
    }

    [Fact]
    public void A_pending_check_is_not_counted_as_a_failure()
    {
        var table = Report((1, CheckResult.PASS), (2, CheckResult.PENDING)).ToTable();

        Assert.Contains("PENDING", table, StringComparison.Ordinal);
        Assert.Contains("pending=1", table, StringComparison.Ordinal);
        Assert.Contains("PASS with 1 PENDING", table, StringComparison.Ordinal);
        Assert.DoesNotContain("FAIL", table, StringComparison.Ordinal);
    }

    [Fact]
    public void A_run_with_nothing_outstanding_still_reads_as_a_plain_pass()
    {
        var table = Report((1, CheckResult.PASS), (2, CheckResult.SKIPPED)).ToTable();

        Assert.Contains("RESULT: PASS   ", table, StringComparison.Ordinal);
        Assert.Contains("pending=0", table, StringComparison.Ordinal);
    }

    [Fact]
    public void A_failing_check_still_fails_the_run_even_alongside_a_pending_one()
    {
        var table = Report((1, CheckResult.FAIL), (2, CheckResult.PENDING)).ToTable();

        Assert.Contains("FAIL (1 failing)", table, StringComparison.Ordinal);
    }

    [Fact]
    public void The_pending_state_is_written_to_the_json_report_by_name()
    {
        var json = Report((6, CheckResult.PENDING)).ToJson();

        Assert.Contains("\"PENDING\"", json, StringComparison.Ordinal);
    }
}
