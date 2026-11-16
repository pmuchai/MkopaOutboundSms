namespace Mkopa.OutboundSms.Contracts.Events
{
    /// <summary>
    /// The result of submitting an SMS to the provider, expressed as the action a caller should take.
    /// </summary>
    public enum SmsSubmissionStatus
    {
        /// <summary>The provider accepted the request; delivery is reported later by callback.</summary>
        Accepted,

        /// <summary>The command failed validation and was not submitted; fix the request.</summary>
        Invalid,

        /// <summary>The provider permanently refused the request; resending will not help.</summary>
        Rejected,

        /// <summary>The provider definitely did not accept the request; if transient, it could be retried.</summary>
        NotAccepted,

        /// <summary>Acceptance could not be established; do not resend blindly.</summary>
        Unknown
    }
}
