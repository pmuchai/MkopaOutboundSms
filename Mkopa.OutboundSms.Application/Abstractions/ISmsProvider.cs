using Mkopa.OutboundSms.Contracts.Commands;
using Mkopa.OutboundSms.Application.Models;

namespace Mkopa.OutboundSms.Application.Abstractions
{
    public interface ISmsProvider
    {
        /// <summary>
        /// Submits an SMS and returns a non-null result with a supported status.
        /// </summary>
        /// <remarks>
        /// Adapters classify expected transport failures as results: NotAccepted when the provider definitely did not
        /// take the request, Unknown when acceptance is uncertain. Exceptions and invalid results are treated as Unknown.
        /// Implementations may perform bounded internal retries only when resubmission is safe.
        /// </remarks>
        Task<ProviderResult> SendAsync(SendSms command, CancellationToken cancellationToken);
    }
}
