using Mkopa.OutboundSms.Contracts.Commands;
using Mkopa.OutboundSms.Application.Enums;

namespace Mkopa.OutboundSms.Application.Abstractions
{
    public interface ICommandValidator
    {
        /// <summary>
        /// Validates the specified SMS command.
        /// </summary>
        /// <param name="command">The SMS command to validate.</param>
        /// <returns>The outcome of validating the command.</returns>
        ValidationOutcome Validate(SendSms command);
    }
}
