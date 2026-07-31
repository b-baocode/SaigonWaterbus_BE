using NUnit.Framework;
using SaigonWaterbus.Application.Tracking;
using Shouldly;

namespace SaigonWaterbus.Integration.Tests;

public sealed class TrackingLatestMergePolicyTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-07-31T07:30:00Z");
    private static readonly TimeSpan AuthorityWindow = TimeSpan.FromSeconds(90);

    [Test]
    public void DecideUpdatesWhenLatestDoesNotExist()
    {
        var decision = TrackingLatestMergePolicy.Decide(
            lastRecordedAt: null,
            lastSequence: null,
            recordedAt: Now,
            sequence: 1,
            source: null,
            now: Now,
            liveAuthorityWindow: AuthorityWindow);

        decision.LatestUpdated.ShouldBeTrue();
        decision.Rejected.ShouldBeNull();
    }

    [Test]
    public void DecideUsesRecordedAtBeforeSequence()
    {
        var decision = TrackingLatestMergePolicy.Decide(
            lastRecordedAt: Now,
            lastSequence: 100,
            recordedAt: Now.AddSeconds(1),
            sequence: 1,
            source: null,
            now: Now.AddSeconds(1),
            liveAuthorityWindow: AuthorityWindow);

        decision.LatestUpdated.ShouldBeTrue();
        decision.Rejected.ShouldBeNull();
    }

    [Test]
    public void DecideUpdatesWhenRecordedAtTiesAndSequenceIncreases()
    {
        var decision = TrackingLatestMergePolicy.Decide(
            lastRecordedAt: Now,
            lastSequence: 100,
            recordedAt: Now,
            sequence: 101,
            source: null,
            now: Now,
            liveAuthorityWindow: AuthorityWindow);

        decision.LatestUpdated.ShouldBeTrue();
        decision.Rejected.ShouldBeNull();
    }

    [Test]
    public void DecideRejectsOlderNonLivePacket()
    {
        var decision = TrackingLatestMergePolicy.Decide(
            lastRecordedAt: Now,
            lastSequence: 100,
            recordedAt: Now.AddSeconds(-1),
            sequence: 101,
            source: null,
            now: Now,
            liveAuthorityWindow: AuthorityWindow);

        decision.LatestUpdated.ShouldBeFalse();
        decision.Rejected.ShouldBe(TrackingLatestMergePolicy.OlderRejectedReason);
    }

    [Test]
    public void DecideLetsOlderLivePacketOverrideInsideAuthorityWindow()
    {
        var decision = TrackingLatestMergePolicy.Decide(
            lastRecordedAt: Now,
            lastSequence: 100,
            recordedAt: Now.AddSeconds(-10),
            sequence: 1,
            source: "live",
            now: Now,
            liveAuthorityWindow: AuthorityWindow);

        decision.LatestUpdated.ShouldBeTrue();
        decision.ReplacedBy.ShouldBe(TrackingLatestMergePolicy.LiveAuthorityReplacement);
        decision.Rejected.ShouldBeNull();
    }

    [Test]
    public void DecideRejectsOlderLivePacketOutsideAuthorityWindow()
    {
        var decision = TrackingLatestMergePolicy.Decide(
            lastRecordedAt: Now,
            lastSequence: 100,
            recordedAt: Now.AddSeconds(-91),
            sequence: 1,
            source: "live",
            now: Now,
            liveAuthorityWindow: AuthorityWindow);

        decision.LatestUpdated.ShouldBeFalse();
        decision.Rejected.ShouldBe(TrackingLatestMergePolicy.LiveStaleRejectedReason);
    }

    [Test]
    public void DecideRejectsNonLivePacketDuringActiveLiveAuthority()
    {
        var decision = TrackingLatestMergePolicy.Decide(
            lastRecordedAt: Now,
            lastSequence: 100,
            recordedAt: Now.AddSeconds(1),
            sequence: 101,
            source: null,
            now: Now,
            liveAuthorityWindow: AuthorityWindow,
            lastSource: "live",
            lastLiveAuthorityUntil: Now.AddSeconds(90),
            incomingBreaksLiveAuthority: false);

        decision.LatestUpdated.ShouldBeFalse();
        decision.Rejected.ShouldBe(TrackingLatestMergePolicy.OlderRejectedReason);
    }

    [Test]
    public void DecideLetsTrustedNonLivePacketBreakActiveLiveAuthority()
    {
        var decision = TrackingLatestMergePolicy.Decide(
            lastRecordedAt: Now,
            lastSequence: 100,
            recordedAt: Now.AddSeconds(1),
            sequence: 1,
            source: null,
            now: Now,
            liveAuthorityWindow: AuthorityWindow,
            lastSource: "live",
            lastLiveAuthorityUntil: Now.AddSeconds(90),
            incomingBreaksLiveAuthority: true);

        decision.LatestUpdated.ShouldBeTrue();
        decision.Rejected.ShouldBeNull();
    }
}
