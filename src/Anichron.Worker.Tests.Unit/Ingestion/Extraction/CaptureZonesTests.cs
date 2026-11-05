using Anichron.Core.Domain;
using Anichron.Worker.Ingestion.Extraction;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using NodaTime;

namespace Anichron.Worker.Tests.Unit.Ingestion.Extraction;

public sealed class CaptureZonesTests
{
    private readonly FakeLogger<CaptureZones> logger = new();

    private static MediaDescription At(double? latitude, double? longitude)
        => MediaDescription.Unknown with { Latitude = latitude, Longitude = longitude };

    private DateTimeZone? ZoneAt(MediaDescription description, IDateTimeZoneProvider? zones = null)
        => new CaptureZones(zones ?? DateTimeZoneProviders.Tzdb, DateTimeZone.Utc, logger).At(description);

    [Theory]
    [InlineData(-36.8485, 174.7633, "Pacific/Auckland")]
    [InlineData(52.52, 13.405, "Europe/Berlin")]
    [InlineData(35.68, 139.69, "Asia/Tokyo")]
    [InlineData(30.0, -40.0, "Etc/GMT+3")]
    public void At_Position_IsTheZoneThere(double latitude, double longitude, string zone)
        => ZoneAt(At(latitude, longitude))!.Id.Should().Be(zone);

    [Theory]
    [InlineData(null, 174.7633)]
    [InlineData(-36.8485, null)]
    [InlineData(null, null)]
    public void At_NoPosition_IsNoZone(double? latitude, double? longitude)
        => ZoneAt(At(latitude, longitude)).Should().BeNull();

    [Theory]
    [InlineData(-90.0, 0.0)]
    [InlineData(90.0, 0.0)]
    [InlineData(0.0, -180.0)]
    [InlineData(0.0, 180.0)]
    public void At_PositionOnTheEdgeOfTheGlobe_HasAZone(double latitude, double longitude)
        => ZoneAt(At(latitude, longitude)).Should().NotBeNull();

    [Theory]
    [InlineData(-90.001, 0.0)]
    [InlineData(90.001, 0.0)]
    [InlineData(0.0, -180.001)]
    [InlineData(0.0, 180.001)]
    [InlineData(double.NaN, 0.0)]
    [InlineData(0.0, double.NaN)]
    public void At_PositionOffTheGlobe_IsNoZone(double latitude, double longitude)
        => ZoneAt(At(latitude, longitude)).Should().BeNull();

    [Fact]
    public void At_ZoneNodaTimeDoesNotKnow_IsNoZoneAndLogged()
    {
        var zones = Substitute.For<IDateTimeZoneProvider>();
        zones.GetZoneOrNull(Arg.Any<string>()).Returns((DateTimeZone?)null);

        ZoneAt(At(-36.8485, 174.7633), zones).Should().BeNull();

        logger.Collector.GetSnapshot().Should().ContainSingle(r => r.Level == LogLevel.Debug)
            .Which.Message.Should().Be("No time zone for -36.8485, 174.7633: NodaTime does not know 'Pacific/Auckland'.");
    }

    [Fact]
    public void At_KnownZone_LogsNothing()
    {
        ZoneAt(At(-36.8485, 174.7633));

        logger.Collector.Count.Should().Be(0);
    }
}
