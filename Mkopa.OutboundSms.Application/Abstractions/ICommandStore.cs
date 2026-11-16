using Mkopa.OutboundSms.Contracts.Events;
using Mkopa.OutboundSms.Application.Models;

namespace Mkopa.OutboundSms.Application.Abstractions
{
    public interface ICommandStore
    {
        /// <summary>
        /// Atomically reserves the ID or returns its existing outcome.
        /// Reservations must survive failures and must not expire automatically.
        /// </summary>
        Task<CommandClaim> TryAcquireAsync(Guid commandId, CancellationToken cancellationToken);

        /// <summary>
        /// Durably records an immutable outcome before returning.
        /// A failed or ambiguous write must leave the reservation in place.
        /// Store implementations must impose their own operation timeout.
        /// </summary>
        Task SaveOutcomeAsync(SmsSubmissionOutcome outcome, CancellationToken cancellationToken);
    }
}
