namespace Mkopa.OutboundSms.Contracts.Commands
{
    /// <summary>
    /// Represents a command to send an SMS message to a phone number.
    /// </summary>
    public sealed class SendSms
    {
        public Guid CommandId { get; init; }
        public string PhoneNumber { get; init; }
        public string MessageText { get; init; }
    }
}
