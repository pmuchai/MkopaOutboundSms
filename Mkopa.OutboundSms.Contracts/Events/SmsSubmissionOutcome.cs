namespace Mkopa.OutboundSms.Contracts.Events
{
    /// <summary>
    /// Published once per command, and republished unchanged for duplicates, to report the provider submission outcome.
    /// </summary>
    public sealed class SmsSubmissionOutcome
    {
        public Guid EventId { get; init; }
        public Guid CommandId { get; init; }
        public SmsSubmissionStatus Status { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public string ProviderReference { get; init; }
        public string ErrorCode { get; init; }
        public string Reason { get; init; }
    }
}
