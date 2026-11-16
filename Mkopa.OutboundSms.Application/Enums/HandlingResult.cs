namespace Mkopa.OutboundSms.Application.Enums
{
    /// <summary>
    /// Specifies the outcome of handling a command.
    /// </summary>
    /// <remarks>An unresolved result indicates that the command is reserved, but no saved outcome is
    /// available.</remarks>
    public enum HandlingResult
    {
        Accepted,
        Unresolved,
        Invalid,
        Duplicate,
        Rejected,
        NotAccepted,
        Unknown
    }
}
