using Mkopa.OutboundSms.Application.Enums;

namespace Mkopa.OutboundSms.Application.Models
{
    /// <summary>
    /// Represents the outcome of a provider operation and any associated reference or error details.
    /// </summary>
    /// <param name="Status">The status of the provider operation.</param>
    /// <param name="ProviderReference">The reference assigned by the provider, if available.</param>
    /// <param name="ErrorCode">The provider's error code, if the operation failed.</param>
    /// <param name="ErrorMessage">A description of the provider error, if available.</param>
    public sealed record ProviderResult(
        ProviderResultStatus Status,
        string? ProviderReference = null,
        string? ErrorCode = null,
        string? ErrorMessage = null
    );
}
