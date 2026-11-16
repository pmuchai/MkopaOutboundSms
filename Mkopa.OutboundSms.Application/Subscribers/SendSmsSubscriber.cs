using Microsoft.Extensions.Logging;
using Mkopa.OutboundSms.Contracts.Commands;
using Mkopa.OutboundSms.Application.Constants;
using Mkopa.OutboundSms.Application.Abstractions;
using Mkopa.OutboundSms.Application.Diagnostics;
using Mkopa.OutboundSms.Application.Enums;
using Mkopa.OutboundSms.Contracts.Events;
using Mkopa.OutboundSms.Application.Models;
using System.Diagnostics;

namespace Mkopa.OutboundSms.Application.Subscribers
{
    public sealed class SendSmsSubscriber : ICommandSubscriber<SendSms>
    {
        private readonly ISmsProvider smsProvider;
        private readonly ICommandStore commandStore;
        private readonly IEventPublisher eventPublisher;
        private readonly ICommandValidator validator;
        private readonly ILogger<SendSmsSubscriber> logger;

        public SendSmsSubscriber(
            ISmsProvider smsProvider,
            ICommandStore commandStore,
            IEventPublisher eventPublisher,
            ICommandValidator validator,
            ILogger<SendSmsSubscriber> logger)
        {
            this.smsProvider = smsProvider;
            this.commandStore = commandStore;
            this.eventPublisher = eventPublisher;
            this.validator = validator;
            this.logger = logger;
        }

        /// <summary>
        /// Processes an SMS command and records its processing duration.
        /// </summary>
        /// <param name="command">The SMS command to process.</param>
        /// <param name="cancellationToken">A token to observe while processing the command.</param>
        /// <returns>A task that represents the asynchronous operation. The task result contains the handling result.</returns>
        public async Task<HandlingResult> HandleAsync(SendSms command, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(command);
            cancellationToken.ThrowIfCancellationRequested();

            var started = Stopwatch.GetTimestamp();
            try
            {
                return await ProcessCommandAsync(command, cancellationToken);
            }
            finally
            {
                SubscriberMetrics.ProcessingDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
        }

        private async Task<HandlingResult> ProcessCommandAsync(
            SendSms command, CancellationToken cancellationToken)
        {
            try
            {
                var validation = validator.Validate(command);

                if (validation == ValidationOutcome.InvalidCommandId)
                {
                    SubscriberLog.EmptyCommandIdDiscarded(logger);
                    return HandlingResult.Invalid;
                }

                // Acquire the command for processing
                var claim = await commandStore.TryAcquireAsync(command.CommandId, cancellationToken);
                if (!claim.Acquired)
                {
                    if (claim.Outcome is null)
                    {
                        SubscriberLog.CommandAlreadyBeingProcessed(logger, command.CommandId);
                        return HandlingResult.Unresolved;
                    }

                    await eventPublisher.PublishAsync(claim.Outcome, cancellationToken);

                    return HandlingResult.Duplicate;
                }

                var outcome = validation == ValidationOutcome.None
                    ? await SubmitToProviderAsync(command, cancellationToken)
                    : CreateOutcome(
                        command.CommandId,
                        SmsSubmissionStatus.Invalid,
                        SmsSubmissionOutcomeConstants.ValidationFailedErrorCode,
                        validation.ToString());

                // Preserve a possible submission result even if the caller cancelled.
                await commandStore.SaveOutcomeAsync(outcome, CancellationToken.None);
                SubscriberLog.RecordedOutcome(logger, outcome.Status, command.CommandId, outcome.EventId);
                await eventPublisher.PublishAsync(outcome, cancellationToken);

                return ToHandlingResult(outcome.Status);
            }
            catch (Exception ex)
            {
                SubscriberLog.ProcessingFailed(logger, command.CommandId, ex.GetType().Name);
                throw;
            }
        }

        /// <summary>
        /// Submits the SMS command to the provider and creates an outcome from the provider response.
        /// </summary>
        /// <remarks>Never throws once the provider has been called: a failed, cancelled or invalid provider call may
        /// still have reached the provider, so it is recorded as <see cref="SmsSubmissionStatus.Unknown"/> rather
        /// than left unresolved or retried.</remarks>
        /// <param name="command">The SMS command to submit.</param>
        /// <param name="cancellationToken">A token used to cancel the provider call.</param>
        /// <returns>The submission outcome.</returns>
        private async Task<SmsSubmissionOutcome> SubmitToProviderAsync(SendSms command, CancellationToken cancellationToken)
        {
            var started = Stopwatch.GetTimestamp();
            try
            {
                var response = await smsProvider.SendAsync(command, cancellationToken);
                return ToOutcome(command.CommandId, response);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Caller cancellation cannot establish that the provider did not accept the request.
                return CreateUnknownOutcome(command.CommandId, SmsSubmissionOutcomeConstants.ProviderCallCancelledErrorCode);
            }
            catch (Exception ex)
            {
                SubscriberLog.ProviderCallFailed(logger, command.CommandId, ex.GetType().Name);
                return CreateUnknownOutcome(command.CommandId, SmsSubmissionOutcomeConstants.ProviderCallFailedErrorCode);
            }
            finally
            {
                SubscriberMetrics.ProviderDuration.Record(Stopwatch.GetElapsedTime(started).TotalSeconds);
            }
        }

        private SmsSubmissionOutcome ToOutcome(Guid commandId, ProviderResult? response)
        {
            switch (response?.Status)
            {
                case ProviderResultStatus.Accepted:
                    return CreateOutcome(commandId, SmsSubmissionStatus.Accepted,
                        providerReference: response.ProviderReference);

                case ProviderResultStatus.Rejected:
                    return CreateOutcome(commandId, SmsSubmissionStatus.Rejected,
                        response.ErrorCode ?? SmsSubmissionOutcomeConstants.ProviderRejectedErrorCode,
                        SmsSubmissionOutcomeConstants.ProviderRejectedReason,
                        response.ProviderReference);

                case ProviderResultStatus.NotAccepted:
                    return CreateOutcome(commandId, SmsSubmissionStatus.NotAccepted,
                        response.ErrorCode ?? SmsSubmissionOutcomeConstants.ProviderNotAcceptedErrorCode,
                        SmsSubmissionOutcomeConstants.ProviderNotAcceptedReason,
                        response.ProviderReference);

                case ProviderResultStatus.Unknown:
                    return CreateUnknownOutcome(commandId,
                        response.ErrorCode ?? SmsSubmissionOutcomeConstants.ProviderOutcomeUnknownErrorCode,
                        response.ProviderReference);

                default:
                    SubscriberLog.ProviderContractViolation(logger, commandId);
                    return CreateUnknownOutcome(commandId, SmsSubmissionOutcomeConstants.ProviderContractViolationErrorCode);
            }
        }

        private static SmsSubmissionOutcome CreateUnknownOutcome(
            Guid commandId, string errorCode, string? providerReference = null) =>
            CreateOutcome(commandId, SmsSubmissionStatus.Unknown, errorCode,
                SmsSubmissionOutcomeConstants.ProviderOutcomeUnknownReason, providerReference);

        private static SmsSubmissionOutcome CreateOutcome(
            Guid commandId,
            SmsSubmissionStatus status,
            string? errorCode = null,
            string? reason = null,
            string? providerReference = null) =>
            new()
            {
                EventId = Guid.NewGuid(),
                CommandId = commandId,
                Status = status,
                OccurredAt = DateTimeOffset.UtcNow,
                ErrorCode = errorCode,
                Reason = reason,
                ProviderReference = providerReference
            };

        private static HandlingResult ToHandlingResult(SmsSubmissionStatus status) => status switch
        {
            SmsSubmissionStatus.Accepted => HandlingResult.Accepted,
            SmsSubmissionStatus.Rejected => HandlingResult.Rejected,
            SmsSubmissionStatus.NotAccepted => HandlingResult.NotAccepted,
            SmsSubmissionStatus.Invalid => HandlingResult.Invalid,
            _ => HandlingResult.Unknown
        };
    }
}
