namespace Mkopa.OutboundSms.Application.Constants
{
    /// <summary>
    /// Provides error codes and reason messages for SMS submission outcomes.
    /// </summary>
    internal static class SmsSubmissionOutcomeConstants
    {
        public const string ValidationFailedErrorCode = "VALIDATION_FAILED";
        public const string ProviderRejectedErrorCode = "PROVIDER_REJECTED";
        public const string ProviderNotAcceptedErrorCode = "PROVIDER_NOT_ACCEPTED";
        public const string ProviderOutcomeUnknownErrorCode = "PROVIDER_OUTCOME_UNKNOWN";
        public const string ProviderCallFailedErrorCode = "PROVIDER_CALL_FAILED";
        public const string ProviderCallCancelledErrorCode = "PROVIDER_CALL_CANCELLED";
        public const string ProviderContractViolationErrorCode = "PROVIDER_CONTRACT_VIOLATION";
        public const string ProviderRejectedReason = "Provider rejected request.";
        public const string ProviderNotAcceptedReason = "Provider did not accept request.";
        public const string ProviderOutcomeUnknownReason = "Provider acceptance could not be established.";
    }
}
