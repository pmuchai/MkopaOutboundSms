using System.Diagnostics.Metrics;

namespace Mkopa.OutboundSms.Application.Diagnostics
{
    /// <summary>
    /// Provides histograms for measuring SMS command-processing and provider-request durations.
    /// </summary>
    /// <remarks>Measurements are published through the Mkopa.OutboundSms meter in seconds.</remarks>
    internal static class SubscriberMetrics
    {
        private static readonly Meter Meter = new("Mkopa.OutboundSms");

        internal static readonly Histogram<double> ProcessingDuration =
            Meter.CreateHistogram<double>(
                "sms.command.processing.duration",
                unit: "s",
                description: "Duration of a command-subscriber invocation, including validation, storage, submission and publication.");

        internal static readonly Histogram<double> ProviderDuration =
            Meter.CreateHistogram<double>(
                "sms.provider.request.duration",
                unit: "s",
                description: "Duration of an SMS provider submission operation.");
    }
}
