using Microsoft.Extensions.Logging;
using Mkopa.OutboundSms.Contracts.Events;

namespace Mkopa.OutboundSms.Application.Diagnostics
{
    /// <summary>
    /// Provides structured log messages for SMS command processing and provider outcomes.
    /// </summary>
    internal static partial class SubscriberLog
    {
        [LoggerMessage(EventId = 1001, Level = LogLevel.Warning, Message = "Discarded SMS command with an empty CommandId; no event published.")]
        internal static partial void EmptyCommandIdDiscarded(ILogger logger);

        [LoggerMessage(EventId = 1002, Level = LogLevel.Warning, Message = "Command {CommandId} is already being processed.")]
        internal static partial void CommandAlreadyBeingProcessed(ILogger logger, Guid commandId);

        [LoggerMessage(EventId = 1003, Level = LogLevel.Information, Message = "Recorded SMS outcome {Status} for command {CommandId} with event {EventId}.")]
        internal static partial void RecordedOutcome(ILogger logger, SmsSubmissionStatus status, Guid commandId, Guid eventId);

        [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "Processing SMS command {CommandId} failed; exception type {ExceptionType}.")]
        internal static partial void ProcessingFailed(ILogger logger, Guid commandId, string exceptionType);

        [LoggerMessage(EventId = 1005, Level = LogLevel.Error, Message = "SMS provider call for command {CommandId} failed; recording unknown outcome. Exception type {ExceptionType}.")]
        internal static partial void ProviderCallFailed(ILogger logger, Guid commandId, string exceptionType);

        [LoggerMessage(EventId = 1006, Level = LogLevel.Error, Message = "SMS provider returned a null or unsupported result for command {CommandId}; recording unknown outcome.")]
        internal static partial void ProviderContractViolation(ILogger logger, Guid commandId);
    }
}
