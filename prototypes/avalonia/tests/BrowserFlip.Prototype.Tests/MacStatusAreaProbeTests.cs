using BrowserFlip.Platform.MacOS;
using BrowserFlip.UI.Platform;

namespace BrowserFlip.Prototype.Tests;

/// <summary>
/// Tests for the probe that reads BrowserFlip's own <c>NSStatusItem</c> back out of the live menu
/// bar. The test host has no status item of its own, which is exactly the case that has to report
/// "not found" rather than an optimistic PASS: the defect the Owner found (MAC-D6) survived
/// precisely because the report inferred success from BrowserFlip's own intent.
/// </summary>
[Collection(SerialAppKit.Name)]
[System.Runtime.Versioning.SupportedOSPlatform("macos")]
public class MacStatusAreaProbeTests
{
    [MacFact]
    public void An_item_that_is_not_there_is_reported_as_not_there()
    {
        var facts = new MacStatusAreaProbe().Inspect("no-such-browserflip-tooltip");

        Assert.False(facts.ItemFound);
        Assert.False(facts.ItemVisible);
        Assert.False(facts.HasImage);
        // The evidence line has to say what was looked for and what was scanned, or a FAIL in
        // the report cannot be told from a probe that never ran.
        Assert.Contains("no-such-browserflip-tooltip", facts.Detail, StringComparison.Ordinal);
        Assert.Contains("NSApp window", facts.Detail, StringComparison.Ordinal);
    }

    [MacFact]
    public void Clicking_an_item_that_is_not_there_fails_with_a_reason_instead_of_throwing()
    {
        var posted = new MacStatusAreaProbe().TryClick("no-such-browserflip-tooltip", out var detail);

        Assert.False(posted);
        Assert.Contains("no-such-browserflip-tooltip", detail, StringComparison.Ordinal);
    }

    [MacFact]
    public void Scanning_the_live_application_does_not_throw_whatever_is_on_screen()
    {
        // The walk goes through every NSApp window and every subview of each one. It must be
        // safe on an arbitrary view tree, since the test host's own tree is not BrowserFlip's.
        var probe = new MacStatusAreaProbe();

        for (var i = 0; i < 3; i++)
        {
            var facts = probe.Inspect("BrowserFlip");
            Assert.NotNull(facts.Detail);
        }
    }

}

/// <summary>The platform-neutral half: what a platform with no probe at all reports.</summary>
public class StatusAreaFactsTests
{
    [Fact]
    public void Not_probed_facts_carry_the_reason_they_could_not_be_measured()
    {
        var facts = StatusAreaFacts.NotProbed("no probe on this platform");

        Assert.False(facts.ItemFound);
        Assert.False(facts.ItemVisible);
        Assert.False(facts.HasImage);
        Assert.Null(facts.ToolTip);
        Assert.Equal("no probe on this platform", facts.Detail);
    }
}
