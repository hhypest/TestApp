using System.Diagnostics.Metrics;
using TestApp.Infrastructure.Observability;
using Xunit;

namespace TestApp.IntegrationTests;

public sealed class OperationalMetricsTests
{
    [Fact]
    public void Operational_meter_exposes_stable_low_cardinality_contract()
    {
        var measurements = new List<Measurement>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, current) =>
            {
                if (instrument.Meter.Name == OperationalMetrics.MeterName)
                    current.EnableMeasurementEvents(instrument);
            }
        };

        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, tags.ToArray())));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) =>
            measurements.Add(new Measurement(instrument.Name, value, tags.ToArray())));
        listener.Start();

        using var metrics = new OperationalMetrics();
        metrics.RecordApiException("concurrency_conflict");
        metrics.RecordOutboxPublish(success: true, deadLettered: false, TimeSpan.FromSeconds(3));
        metrics.RecordOutboxPublish(success: false, deadLettered: true);
        metrics.RecordDeadLetterAction("requeue");
        metrics.RecordAttemptExpiration(success: true);
        metrics.RecordRetentionDeleted("audit", 2);
        listener.RecordObservableInstruments();

        Assert.Contains(measurements, x => x.Name == "testapp.api.exception" && x.Value == 1 && x.HasTag("kind", "concurrency_conflict"));
        Assert.Contains(measurements, x => x.Name == "testapp.outbox.publish" && x.Value == 1 && x.HasTag("outcome", "success"));
        Assert.Contains(measurements, x => x.Name == "testapp.outbox.publish" && x.Value == 1 && x.HasTag("outcome", "dead_letter"));
        Assert.Contains(measurements, x => x.Name == "testapp.outbox.delivery_lag" && x.Value == 3);
        Assert.Contains(measurements, x => x.Name == "testapp.outbox.dead_letter_action" && x.HasTag("action", "requeue"));
        Assert.Contains(measurements, x => x.Name == "testapp.attempt.expiration" && x.HasTag("outcome", "success"));
        Assert.Contains(measurements, x => x.Name == "testapp.retention.deleted" && x.Value == 2 && x.HasTag("kind", "audit"));

        Assert.Contains(measurements, x => x.Name == "testapp.outbox.pending");
        Assert.Contains(measurements, x => x.Name == "testapp.outbox.dead_letters");
        Assert.Contains(measurements, x => x.Name == "testapp.outbox.oldest_pending_age");
        Assert.Contains(measurements, x => x.Name == "testapp.attempt.overdue");
        Assert.Contains(measurements, x => x.Name == "testapp.attempt.oldest_overdue_lag");
    }

    private sealed record Measurement(
        string Name,
        double Value,
        KeyValuePair<string, object?>[] Tags)
    {
        public bool HasTag(string key, string value) =>
            Tags.Any(tag => tag.Key == key && string.Equals(tag.Value?.ToString(), value, StringComparison.Ordinal));
    }
}
