using Mkopa.OutboundSms.Contracts.Events;

namespace Mkopa.OutboundSms.Application.Models
{
    /// <summary>
    /// Represents the result of attempting to acquire a command.
    /// </summary>
    /// <param name="Acquired">true if the command was acquired; otherwise, false.</param>
    /// <param name="Outcome">The SMS submission outcome, if available.</param>
    public sealed record CommandClaim(
         bool Acquired,
         SmsSubmissionOutcome? Outcome = null);
}
