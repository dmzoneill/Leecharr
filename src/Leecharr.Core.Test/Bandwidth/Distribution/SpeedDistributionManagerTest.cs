using System;
using System.Collections.Generic;
using System.Linq;
using NSubstitute;
using NUnit.Framework;

using NzbDrone.Core.Bandwidth.Distribution;
using NzbDrone.Core.Configuration;

namespace Leecharr.Core.Test.Bandwidth.Distribution;

[TestFixture]
public class SpeedDistributionManagerTest
{
    private IConfigService _configService;
    private ISpeedDistributor _equalDistributor;
    private ISpeedDistributor _paretoDistributor;
    private DateTime _utcNow;
    private SpeedDistributionManager _manager;

    [SetUp]
    public void Setup()
    {
        _configService = Substitute.For<IConfigService>();
        _equalDistributor = Substitute.For<ISpeedDistributor>();
        _paretoDistributor = Substitute.For<ISpeedDistributor>();
        _utcNow = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);

        _equalDistributor.Name.Returns("Equal");
        _paretoDistributor.Name.Returns("Pareto");

        _configService.UploadDistributionAlgorithm.Returns("Equal");
        _configService.UploadDistributionSpreadPercentage.Returns(100);
        _configService.UploadRedistributionMode.Returns("tick");
        _configService.UploadCustomIntervalMinutes.Returns(5);
        _configService.DownloadDistributionAlgorithm.Returns("Equal");
        _configService.DownloadDistributionSpreadPercentage.Returns(100);
        _configService.DownloadRedistributionMode.Returns("tick");
        _configService.DownloadCustomIntervalMinutes.Returns(5);

        _equalDistributor.Distribute(Arg.Any<long>(), Arg.Any<int>())
            .Returns(callInfo =>
            {
                var total = callInfo.ArgAt<long>(0);
                var count = callInfo.ArgAt<int>(1);
                if (count == 0)
                {
                    return Array.Empty<long>();
                }

                var share = total / count;
                return Enumerable.Repeat(share, count).ToArray();
            });

        _paretoDistributor.Distribute(Arg.Any<long>(), Arg.Any<int>())
            .Returns(callInfo =>
            {
                var total = callInfo.ArgAt<long>(0);
                var count = callInfo.ArgAt<int>(1);
                if (count == 0)
                {
                    return Array.Empty<long>();
                }

                var result = new long[count];
                for (var i = 0; i < count; i++)
                {
                    result[i] = total / count;
                }

                result[0] += total - result.Sum();
                return result;
            });

        _manager = new SpeedDistributionManager(
            new List<ISpeedDistributor> { _equalDistributor, _paretoDistributor },
            _configService,
            () => _utcNow);
    }

    [Test]
    public void CurrentDistribution_should_return_configured_algorithm()
    {
        _configService.UploadDistributionAlgorithm.Returns("Pareto");

        Assert.That(_manager.CurrentDistribution, Is.EqualTo("Pareto"));
    }

    [Test]
    public void DistributeSpeeds_with_no_args_should_use_default_speed()
    {
        var speeds = _manager.DistributeSpeeds(3);

        Assert.That(speeds, Has.Length.EqualTo(3));
        _equalDistributor.Received(1).Distribute(1_048_576L, 3);
    }

    [Test]
    public void DistributeSpeeds_with_max_speed_should_use_provided_speed()
    {
        var speeds = _manager.DistributeSpeeds(3, 500_000L);

        Assert.That(speeds, Has.Length.EqualTo(3));
        _equalDistributor.Received(1).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeSpeeds_with_zero_max_speed_should_use_default()
    {
        _manager.DistributeSpeeds(3, 0);

        _equalDistributor.Received(1).Distribute(1_048_576L, 3);
    }

    [Test]
    public void DistributeSpeeds_should_select_configured_algorithm()
    {
        _configService.UploadDistributionAlgorithm.Returns("Pareto");

        _manager.DistributeSpeeds(3, 500_000L);

        _paretoDistributor.Received(1).Distribute(500_000L, 3);
        _equalDistributor.DidNotReceive().Distribute(Arg.Any<long>(), Arg.Any<int>());
    }

    [Test]
    public void DistributeSpeeds_should_fall_back_to_first_distributor_when_algorithm_not_found()
    {
        _configService.UploadDistributionAlgorithm.Returns("NonExistent");

        _manager.DistributeSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeSpeeds_should_apply_spread_percentage()
    {
        _configService.UploadDistributionSpreadPercentage.Returns(50);

        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 300_000, 200_000 });

        var speeds = _manager.DistributeSpeeds(2, 500_000);

        Assert.That(speeds, Has.Length.EqualTo(2));
        Assert.That(speeds[0], Is.Not.EqualTo(300_000));
    }

    [Test]
    public void DistributeUploadSpeeds_should_redistribute_on_tick_mode()
    {
        _configService.UploadRedistributionMode.Returns("tick");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(3, 500_000L);

        _equalDistributor.Received(2).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeUploadSpeeds_should_not_redistribute_on_fixed_mode()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeUploadSpeeds_fixed_mode_should_return_cached_array_when_inputs_unchanged()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeUploadSpeeds(3, 500_000L);
        var second = _manager.DistributeUploadSpeeds(3, 500_000L);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(second, Is.Not.SameAs(first));
        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeUploadSpeeds_fixed_mode_should_return_new_array_when_inputs_change()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeUploadSpeeds(3, 500_000L);
        var second = _manager.DistributeUploadSpeeds(4, 500_000L);

        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void DistributeUploadSpeeds_should_redistribute_when_count_changes()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(4, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 4);
    }

    [Test]
    public void DistributeUploadSpeeds_should_redistribute_when_max_speed_changes()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(3, 600_000L);

        _equalDistributor.Received(2).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeUploadSpeeds_with_priority_weights_should_apply_weights()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 3)
            .Returns(new long[] { 100_000, 100_000, 100_000 });

        var weights = new double[] { 2.0, 1.0, 0.5 };
        var speeds = _manager.DistributeUploadSpeeds(3, 300_000L, weights);

        Assert.That(speeds, Has.Length.EqualTo(3));
        Assert.That(speeds[0], Is.GreaterThan(speeds[2]));
    }

    [Test]
    public void DistributeDownloadSpeeds_should_use_download_config()
    {
        _configService.DownloadDistributionAlgorithm.Returns("Equal");
        _configService.DownloadRedistributionMode.Returns("tick");

        _manager.DistributeDownloadSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeDownloadSpeeds_should_not_redistribute_on_fixed_mode()
    {
        _configService.DownloadRedistributionMode.Returns("fixed");

        _manager.DistributeDownloadSpeeds(3, 500_000L);
        _manager.DistributeDownloadSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeDownloadSpeeds_fixed_mode_should_return_cached_array_when_inputs_unchanged()
    {
        _configService.DownloadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeDownloadSpeeds(3, 500_000L);
        var second = _manager.DistributeDownloadSpeeds(3, 500_000L);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(second, Is.Not.SameAs(first));
        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeDownloadSpeeds_fixed_mode_should_return_new_array_when_inputs_change()
    {
        _configService.DownloadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeDownloadSpeeds(3, 500_000L);
        var second = _manager.DistributeDownloadSpeeds(3, 600_000L);

        Assert.That(second, Is.Not.SameAs(first));
    }

    [Test]
    public void DistributeDownloadSpeeds_with_priority_weights_should_apply_weights()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 200_000, 200_000 });

        var weights = new double[] { 1.0, 2.0 };
        var speeds = _manager.DistributeDownloadSpeeds(2, 400_000L, weights);

        Assert.That(speeds, Has.Length.EqualTo(2));
        Assert.That(speeds[1], Is.GreaterThan(speeds[0]));
    }

    [Test]
    public void ApplyPriorityWeights_should_return_original_when_weights_null()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 250_000, 250_000 });

        var speeds = _manager.DistributeUploadSpeeds(2, 500_000L, null);

        Assert.That(speeds[0], Is.EqualTo(250_000));
        Assert.That(speeds[1], Is.EqualTo(250_000));
    }

    [Test]
    public void ApplyPriorityWeights_should_return_original_when_length_mismatch()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 250_000, 250_000 });

        var speeds = _manager.DistributeUploadSpeeds(2, 500_000L, new double[] { 1.0 });

        Assert.That(speeds[0], Is.EqualTo(250_000));
    }

    [Test]
    public void ApplyPriorityWeights_negative_weights_are_sanitized_and_do_not_produce_negative_speeds()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 100_000, 100_000 });

        var speeds = _manager.DistributeUploadSpeeds(2, 200_000L, new double[] { -5.0, 3.0 });

        Assert.That(speeds[0], Is.GreaterThanOrEqualTo(0L));
        Assert.That(speeds[1], Is.GreaterThanOrEqualTo(0L));
        Assert.That(speeds[0], Is.EqualTo(52_560L));
        Assert.That(speeds[1], Is.EqualTo(147_440L));
        Assert.That(speeds[0] + speeds[1], Is.EqualTo(200_000L));
    }

    [Test]
    public void ApplyPriorityWeights_nan_and_infinity_weights_are_sanitized_and_do_not_collapse_bandwidth()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 3)
            .Returns(new long[] { 100_000, 100_000, 100_000 });

        var speeds = _manager.DistributeUploadSpeeds(3, 300_000L, new double[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity });

        Assert.That(speeds[0], Is.GreaterThan(0L));
        Assert.That(speeds[1], Is.GreaterThan(0L));
        Assert.That(speeds[2], Is.GreaterThan(0L));
        Assert.That(speeds.Sum(), Is.EqualTo(300_000L));
    }

    [Test]
    public void ApplyPriorityWeights_zero_weights_are_safely_handled()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 200_000, 200_000 });

        var speeds = _manager.DistributeUploadSpeeds(2, 400_000L, new double[] { 0.0, 0.0 });

        Assert.That(speeds[0], Is.EqualTo(200_000L));
        Assert.That(speeds[1], Is.EqualTo(200_000L));
    }

    [Test]
    public void GetAvailableDistributions_should_return_all_distributor_names()
    {
        var names = _manager.GetAvailableDistributions();

        Assert.That(names, Has.Count.EqualTo(2));
        Assert.That(names, Does.Contain("Equal"));
        Assert.That(names, Does.Contain("Pareto"));
    }

    [Test]
    public void DistributeSpeeds_should_match_algorithm_case_insensitively()
    {
        _configService.UploadDistributionAlgorithm.Returns("pareto");

        _manager.DistributeSpeeds(3, 500_000L);

        _paretoDistributor.Received(1).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeUploadSpeeds_interval_mode_should_not_redistribute_before_interval()
    {
        _configService.UploadRedistributionMode.Returns("interval");
        _configService.UploadCustomIntervalMinutes.Returns(60);

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeUploadSpeeds(3, 500_000L);

        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeUploadSpeeds_interval_mode_should_redistribute_after_interval()
    {
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _utcNow = baseTime;
        _configService.UploadRedistributionMode.Returns("interval");
        _configService.UploadCustomIntervalMinutes.Returns(5);

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);

        // Advance clock past the interval without sleeps
        _utcNow = baseTime.AddMinutes(6);

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _equalDistributor.Received(2).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeDownloadSpeeds_interval_mode_should_redistribute_after_interval()
    {
        var baseTime = new DateTime(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
        _utcNow = baseTime;
        _configService.DownloadDistributionAlgorithm.Returns("Equal");
        _configService.DownloadRedistributionMode.Returns("interval");
        _configService.DownloadCustomIntervalMinutes.Returns(5);

        _manager.DistributeDownloadSpeeds(3, 500_000L);
        _equalDistributor.Received(1).Distribute(Arg.Any<long>(), 3);

        // Advance clock past the interval without sleeps
        _utcNow = baseTime.AddMinutes(6);

        _manager.DistributeDownloadSpeeds(3, 500_000L);
        _equalDistributor.Received(2).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void DistributeUploadSpeeds_should_redistribute_when_algorithm_changes_in_fixed_mode()
    {
        _configService.UploadRedistributionMode.Returns("fixed");
        _configService.UploadDistributionAlgorithm.Returns("Equal");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _equalDistributor.Received(1).Distribute(500_000L, 3);

        _configService.UploadDistributionAlgorithm.Returns("Pareto");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _paretoDistributor.Received(1).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeDownloadSpeeds_should_redistribute_when_algorithm_changes_in_fixed_mode()
    {
        _configService.DownloadRedistributionMode.Returns("fixed");
        _configService.DownloadDistributionAlgorithm.Returns("Equal");

        _manager.DistributeDownloadSpeeds(3, 500_000L);
        _equalDistributor.Received(1).Distribute(500_000L, 3);

        _configService.DownloadDistributionAlgorithm.Returns("Pareto");

        _manager.DistributeDownloadSpeeds(3, 500_000L);
        _paretoDistributor.Received(1).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeUploadSpeeds_should_redistribute_when_spread_percentage_changes_in_fixed_mode()
    {
        _configService.UploadRedistributionMode.Returns("fixed");
        _configService.UploadDistributionSpreadPercentage.Returns(100);

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _equalDistributor.Received(1).Distribute(500_000L, 3);

        _configService.UploadDistributionSpreadPercentage.Returns(50);

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _equalDistributor.Received(2).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeDownloadSpeeds_should_redistribute_when_spread_percentage_changes_in_fixed_mode()
    {
        _configService.DownloadRedistributionMode.Returns("fixed");
        _configService.DownloadDistributionSpreadPercentage.Returns(100);

        _manager.DistributeDownloadSpeeds(3, 500_000L);
        _equalDistributor.Received(1).Distribute(500_000L, 3);

        _configService.DownloadDistributionSpreadPercentage.Returns(50);

        _manager.DistributeDownloadSpeeds(3, 500_000L);
        _equalDistributor.Received(2).Distribute(500_000L, 3);
    }

    [Test]
    public void DistributeUploadSpeeds_mutating_returned_array_should_not_affect_subsequent_calls()
    {
        _configService.UploadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeUploadSpeeds(3, 300_000L);
        var originalFirstValue = first[0];

        first[0] = 999_999L;

        var second = _manager.DistributeUploadSpeeds(3, 300_000L);

        Assert.That(second[0], Is.EqualTo(originalFirstValue));
        Assert.That(second[0], Is.Not.EqualTo(999_999L));
    }

    [Test]
    public void DistributeDownloadSpeeds_mutating_returned_array_should_not_affect_subsequent_calls()
    {
        _configService.DownloadRedistributionMode.Returns("fixed");

        var first = _manager.DistributeDownloadSpeeds(3, 300_000L);
        var originalFirstValue = first[0];

        first[0] = 999_999L;

        var second = _manager.DistributeDownloadSpeeds(3, 300_000L);

        Assert.That(second[0], Is.EqualTo(originalFirstValue));
        Assert.That(second[0], Is.Not.EqualTo(999_999L));
    }

    [Test]
    public void InvalidateCache_should_force_redistribution_on_next_call()
    {
        _configService.UploadRedistributionMode.Returns("fixed");
        _configService.DownloadRedistributionMode.Returns("fixed");

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeDownloadSpeeds(3, 500_000L);

        _equalDistributor.Received(2).Distribute(Arg.Any<long>(), 3);

        _manager.InvalidateCache();

        _manager.DistributeUploadSpeeds(3, 500_000L);
        _manager.DistributeDownloadSpeeds(3, 500_000L);

        _equalDistributor.Received(4).Distribute(Arg.Any<long>(), 3);
    }

    [Test]
    public void ApplyPriorityWeights_low_priority_torrents_receive_at_least_guaranteed_minimum_floor()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 3)
            .Returns(new long[] { 10_000, 10_000, 10_000 });

        // Total bandwidth = 30,000 bytes/s across 3 torrents.
        // Torrent 0 has massive weight (100.0), Torrent 1 and 2 have minimal weights (0.01, 0.01).
        var weights = new double[] { 100.0, 0.01, 0.01 };
        var speeds = _manager.DistributeUploadSpeeds(3, 30_000L, weights);

        Assert.That(speeds, Has.Length.EqualTo(3));
        Assert.That(speeds[0], Is.GreaterThan(speeds[1]));
        Assert.That(speeds[1], Is.GreaterThanOrEqualTo(SpeedDistributionManager.MinimumFloorBytesPerSec));
        Assert.That(speeds[2], Is.GreaterThanOrEqualTo(SpeedDistributionManager.MinimumFloorBytesPerSec));
        Assert.That(speeds.Sum(), Is.EqualTo(30_000L));
    }

    [Test]
    public void ApplyPriorityWeights_high_priority_torrents_receive_proportional_remainder_above_floor()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 50_000, 50_000 });

        var total = 100_000L;
        var floor = SpeedDistributionManager.MinimumFloorBytesPerSec;
        var totalFloor = 2 * floor;
        var remainder = total - totalFloor;

        // Weights ratio 3:1 (sum = 4.0)
        var weights = new double[] { 3.0, 1.0 };
        var speeds = _manager.DistributeUploadSpeeds(2, total, weights);

        var expectedTorrent0Remainder = (long)(remainder * (3.0 / 4.0));
        var expectedTorrent1Remainder = (long)(remainder * (1.0 / 4.0));

        Assert.That(speeds[0] - floor, Is.EqualTo(expectedTorrent0Remainder));
        Assert.That(speeds[1] - floor, Is.EqualTo(expectedTorrent1Remainder));
        Assert.That(speeds[0] + speeds[1], Is.EqualTo(total));
    }

    [Test]
    public void ApplyPriorityWeights_surplus_bandwidth_reallocated_when_high_priority_hits_quota_cap()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 50_000, 50_000 });

        var total = 100_000L;
        // High priority torrent (weight 4.0) has a cap of 20,000 bytes/s.
        // Its uncapped share would be floor (5,120) + (100,000 - 10,240) * (4/5) = 5,120 + 71,808 = 76,928 B/s.
        // Because it is capped at 20,000 B/s, its surplus (~56,928 B/s) must cascade to Torrent 1.
        var weights = new double[] { 4.0, 1.0 };
        var caps = new long[] { 20_000L, 0L };

        var speeds = _manager.DistributeUploadSpeeds(2, total, weights, caps);

        Assert.That(speeds[0], Is.EqualTo(20_000L));
        Assert.That(speeds[1], Is.EqualTo(80_000L));
        Assert.That(speeds.Sum(), Is.EqualTo(total));
    }

    [Test]
    public void ApplyPriorityWeights_when_total_less_than_total_floor_divides_equally()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 3)
            .Returns(new long[] { 3_000, 3_000, 3_000 });

        // Total 9,000 is less than 3 * 5,120 = 15,360
        var weights = new double[] { 10.0, 2.0, 1.0 };
        var speeds = _manager.DistributeUploadSpeeds(3, 9_000L, weights);

        Assert.That(speeds[0], Is.EqualTo(3_000L));
        Assert.That(speeds[1], Is.EqualTo(3_000L));
        Assert.That(speeds[2], Is.EqualTo(3_000L));
        Assert.That(speeds.Sum(), Is.EqualTo(9_000L));
    }

    [Test]
    public void ApplyPriorityWeights_cascading_surplus_reallocates_across_multiple_capped_tiers()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 3)
            .Returns(new long[] { 50_000, 50_000, 50_000 });

        var total = 150_000L;
        // Torrent 0 (highest weight 10.0) capped at 25,000
        // Torrent 1 (weight 2.0) capped at 50,000
        // Torrent 2 (lowest weight 1.0) uncapped (0)
        var weights = new double[] { 10.0, 2.0, 1.0 };
        var caps = new long[] { 25_000L, 50_000L, 0L };

        var speeds = _manager.DistributeUploadSpeeds(3, total, weights, caps);

        Assert.That(speeds[0], Is.EqualTo(25_000L));
        Assert.That(speeds[1], Is.EqualTo(50_000L));
        Assert.That(speeds[2], Is.EqualTo(75_000L));
        Assert.That(speeds.Sum(), Is.EqualTo(total));
    }

    [Test]
    public void DistributeDownloadSpeeds_with_priority_weights_and_caps_reallocates_surplus()
    {
        _equalDistributor.Distribute(Arg.Any<long>(), 2)
            .Returns(new long[] { 50_000, 50_000 });

        var weights = new double[] { 2.0, 1.0 };
        var caps = new long[] { 20_000L, 0L };
        var speeds = _manager.DistributeDownloadSpeeds(2, 100_000L, weights, caps);

        Assert.That(speeds[0], Is.EqualTo(20_000L));
        Assert.That(speeds[1], Is.EqualTo(80_000L));
    }

    [Test]
    public void ApplyPriorityWeights_low_bandwidth_across_many_items_distributes_full_quota_without_discarding_remainder()
    {
        const int count = 50;
        const long totalBandwidth = 3_333L; // Non-divisible number across 50 items
        var speeds = new long[count];
        Array.Fill(speeds, totalBandwidth / count);
        speeds[0] += totalBandwidth - speeds.Sum();

        var weights = new double[count];
        weights[0] = 50.0;
        for (var i = 1; i < count; i++)
        {
            weights[i] = 0.05 * ((i % 5) + 1);
        }

        var result = SpeedDistributionManager.ApplyPriorityWeights(speeds, weights, null, floorBytesPerSec: 0);

        Assert.That(result, Has.Length.EqualTo(count));
        Assert.That(result.Sum(), Is.EqualTo(totalBandwidth));
        for (var i = 0; i < count; i++)
        {
            Assert.That(result[i], Is.GreaterThan(0L), $"Item {i} starved with 0 bytes");
        }
    }

    [Test]
    public void ApplyPriorityWeights_active_low_priority_items_receive_non_zero_quantum_and_do_not_starve()
    {
        const int count = 50;
        const long totalBandwidth = 51_200L; // 50 KB/s across 50 torrents
        var speeds = new long[count];
        Array.Fill(speeds, 1_024L);

        // One super high-priority item, 49 very low-priority items
        var weights = new double[count];
        weights[0] = 100.0;
        for (var i = 1; i < count; i++)
        {
            weights[i] = 0.01;
        }

        var result = SpeedDistributionManager.ApplyPriorityWeights(speeds, weights, null, floorBytesPerSec: 0);

        Assert.That(result, Has.Length.EqualTo(count));
        Assert.That(result.Sum(), Is.EqualTo(totalBandwidth));
        for (var i = 1; i < count; i++)
        {
            Assert.That(result[i], Is.GreaterThanOrEqualTo(SpeedDistributionManager.MinimumTransmissionQuantum), $"Low priority item {i} starved below transmission quantum");
        }
    }

    [Test]
    public void ApplyPriorityWeights_largest_remainder_redistributes_fractional_tokens_accurately()
    {
        var speeds = new long[] { 34, 33, 33 };
        var total = 100L;
        var weights = new double[] { 1.0, 1.0, 1.0 }; // 100 / 3 = 33.333 each -> remainder 1

        var result = SpeedDistributionManager.ApplyPriorityWeights(speeds, weights, null, floorBytesPerSec: 0);

        Assert.That(result.Sum(), Is.EqualTo(total));
        Assert.That(result[0] + result[1] + result[2], Is.EqualTo(100L));
    }
}
