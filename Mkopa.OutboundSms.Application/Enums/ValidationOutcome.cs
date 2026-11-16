namespace Mkopa.OutboundSms.Application.Enums
{
    /// <summary>
    /// Identifies the outcome of command validation.
    /// </summary>
    public enum ValidationOutcome
    {
        None,
        InvalidCommandId,
        PhoneNumberRequired,
        InvalidPhoneNumber,
        MessageRequired,
        MessageTooLong
    }
}
