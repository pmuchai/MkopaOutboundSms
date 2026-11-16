using Mkopa.OutboundSms.Contracts.Commands;
using Mkopa.OutboundSms.Application.Abstractions;
using Mkopa.OutboundSms.Application.Enums;
using System.Text.RegularExpressions;

namespace Mkopa.OutboundSms.Application.Validation
{
    public sealed class SendSmsValidator : ICommandValidator
    {
        // E.164 format: +[country code][subscriber number including area code]
        private static readonly Regex PhoneNumberRegex = new(@"\A\+[1-9][0-9]{1,14}\z", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        // Assumption, the provider may support
        // longer multipart messages, depending on its API.
        private const int MaxMessageLength = 1600;

        public ValidationOutcome Validate(SendSms command)
        {
            ArgumentNullException.ThrowIfNull(command);

            if (command.CommandId == Guid.Empty)
            {
                return ValidationOutcome.InvalidCommandId;
            }

            if (string.IsNullOrWhiteSpace(command.PhoneNumber))
            {
                return ValidationOutcome.PhoneNumberRequired;
            }

            if (command.PhoneNumber.Length > 16 || !PhoneNumberRegex.IsMatch(command.PhoneNumber))
            {
                return ValidationOutcome.InvalidPhoneNumber;
            }

            if (string.IsNullOrWhiteSpace(command.MessageText))
            {
                return ValidationOutcome.MessageRequired;
            }

            if (command.MessageText.Length > MaxMessageLength)
            {
                return ValidationOutcome.MessageTooLong;
            }

            return ValidationOutcome.None;
        }
    }
}
