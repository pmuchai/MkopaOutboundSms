using Microsoft.Extensions.Logging;
using Mkopa.OutboundSms.Contracts.Commands;
using Mkopa.OutboundSms.Application.Abstractions;
using Mkopa.OutboundSms.Application.Enums;
using Mkopa.OutboundSms.Contracts.Events;
using Mkopa.OutboundSms.Application.Models;
using Mkopa.OutboundSms.Application.Subscribers;
using Mkopa.OutboundSms.Application.Tests.Doubles;
using Mkopa.OutboundSms.Application.Validation;
using NSubstitute;
using NSubstitute.Extensions;
using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace Mkopa.OutboundSms.Application.Tests.Subscribers
{
    /// <summary>
    /// Tests SMS submission, validation, idempotency, failure handling, cancellation, and observability.
    /// </summary>
    public sealed class SendSmsSubscriberTests
    {
        private const string PhoneNumber = "+254712345678";
        private const string MessageText = "private-message-content";
        private const string PrivateDetail = "private-provider-detail";

        private readonly InMemoryCommandStore store = new();
        private readonly ConcurrentQueue<SmsSubmissionOutcome> published = new();
        private readonly ICommandStore commandStore = Substitute.For<ICommandStore>();
        private readonly ISmsProvider smsProvider = Substitute.For<ISmsProvider>();
        private readonly IEventPublisher eventPublisher = Substitute.For<IEventPublisher>();
        private readonly ILogger<SendSmsSubscriber> logger = Substitute.For<ILogger<SendSmsSubscriber>>();
        private readonly SendSms command = CreateCommand(Guid.NewGuid());
        private readonly SendSmsSubscriber subscriber;

        public SendSmsSubscriberTests()
        {
            logger.IsEnabled(Arg.Any<LogLevel>()).Returns(true);

            // The substitute delegates to the in-memory store so calls can be both verified and faulted.
            commandStore.TryAcquireAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(call => store.TryAcquireAsync(call.ArgAt<Guid>(0), call.ArgAt<CancellationToken>(1)));
            commandStore.SaveOutcomeAsync(Arg.Any<SmsSubmissionOutcome>(), Arg.Any<CancellationToken>())
                .Returns(call => store.SaveOutcomeAsync(
                    call.ArgAt<SmsSubmissionOutcome>(0), call.ArgAt<CancellationToken>(1)));

            smsProvider.SendAsync(Arg.Any<SendSms>(), Arg.Any<CancellationToken>())
                .Returns(new ProviderResult(ProviderResultStatus.Accepted, "provider-ref"));

            // Every publication in every test also verifies the outcome was saved first.
            eventPublisher.PublishAsync(Arg.Any<SmsSubmissionOutcome>(), Arg.Any<CancellationToken>())
                .Returns(call => Publish(call.ArgAt<SmsSubmissionOutcome>(0), call.ArgAt<CancellationToken>(1)));

            subscriber = new SendSmsSubscriber(
                smsProvider, commandStore, eventPublisher, new SendSmsValidator(), logger);
        }

        public enum ProviderFailure
        {
            ThrowsSynchronously,
            ThrowsAsynchronously,
            TimesOut,
            ReturnsNull,
            ReturnsUnsupportedStatus
        }

        public enum FirstOutcome
        {
            Accepted,
            Invalid,
            Unknown
        }

        #region Submission

        [Fact]
        public async Task HandleAsync_ValidCommand_SubmitsOnceSavesThenPublishesAccepted()
        {
            var result = await subscriber.HandleAsync(command, default);

            Assert.Equal(HandlingResult.Accepted, result);
            await smsProvider.Received(1).SendAsync(command, Arg.Any<CancellationToken>());
            var outcome = Assert.Single(published);
            Assert.Equal(command.CommandId, outcome.CommandId);
            Assert.Equal(SmsSubmissionStatus.Accepted, outcome.Status);
            Assert.Equal("provider-ref", outcome.ProviderReference);
            Assert.Null(outcome.ErrorCode);
            Assert.NotEqual(Guid.Empty, outcome.EventId);
            Assert.Equal(outcome, store.GetOutcome(command.CommandId));
        }

        [Theory]
        [InlineData(ProviderResultStatus.Rejected, null, HandlingResult.Rejected, SmsSubmissionStatus.Rejected, "PROVIDER_REJECTED")]
        [InlineData(ProviderResultStatus.Rejected, "INVALID_DESTINATION", HandlingResult.Rejected, SmsSubmissionStatus.Rejected, "INVALID_DESTINATION")]
        [InlineData(ProviderResultStatus.NotAccepted, null, HandlingResult.NotAccepted, SmsSubmissionStatus.NotAccepted, "PROVIDER_NOT_ACCEPTED")]
        [InlineData(ProviderResultStatus.NotAccepted, "RETRY_EXHAUSTED", HandlingResult.NotAccepted, SmsSubmissionStatus.NotAccepted, "RETRY_EXHAUSTED")]
        [InlineData(ProviderResultStatus.Unknown, null, HandlingResult.Unknown, SmsSubmissionStatus.Unknown, "PROVIDER_OUTCOME_UNKNOWN")]
        [InlineData(ProviderResultStatus.Unknown, "GATEWAY_TIMEOUT", HandlingResult.Unknown, SmsSubmissionStatus.Unknown, "GATEWAY_TIMEOUT")]
        public async Task HandleAsync_ProviderDoesNotAccept_PublishesMatchingStatusAndErrorCode(
            ProviderResultStatus providerStatus,
            string? providerErrorCode,
            HandlingResult expectedResult,
            SmsSubmissionStatus expectedStatus,
            string expectedErrorCode)
        {
            ProviderReturns(new ProviderResult(providerStatus, "provider-ref", providerErrorCode));

            var result = await subscriber.HandleAsync(command, default);

            Assert.Equal(expectedResult, result);
            var outcome = Assert.Single(published);
            Assert.Equal(expectedStatus, outcome.Status);
            Assert.Equal(expectedErrorCode, outcome.ErrorCode);
            Assert.Equal("provider-ref", outcome.ProviderReference);
            Assert.Equal(outcome, store.GetOutcome(command.CommandId));
        }

        [Theory]
        [InlineData(ProviderFailure.ThrowsSynchronously, "PROVIDER_CALL_FAILED")]
        [InlineData(ProviderFailure.ThrowsAsynchronously, "PROVIDER_CALL_FAILED")]
        [InlineData(ProviderFailure.TimesOut, "PROVIDER_CALL_FAILED")]
        [InlineData(ProviderFailure.ReturnsNull, "PROVIDER_CONTRACT_VIOLATION")]
        [InlineData(ProviderFailure.ReturnsUnsupportedStatus, "PROVIDER_CONTRACT_VIOLATION")]
        public async Task HandleAsync_ProviderCallFails_PublishesUnknownAndLogsErrorWithoutPrivateData(
            ProviderFailure failure, string expectedErrorCode)
        {
            ArrangeProviderFailure(failure);

            var result = await subscriber.HandleAsync(command, default);

            // The request may have reached the provider, so the outcome is Unknown rather than retried.
            Assert.Equal(HandlingResult.Unknown, result);
            var outcome = Assert.Single(published);
            Assert.Equal(SmsSubmissionStatus.Unknown, outcome.Status);
            Assert.Equal(expectedErrorCode, outcome.ErrorCode);
            Assert.Equal(outcome, store.GetOutcome(command.CommandId));

            var logs = GetLogEntries();
            Assert.Single(logs, entry => entry.Level == LogLevel.Error);
            Assert.All(logs, entry =>
            {
                Assert.DoesNotContain(PhoneNumber, entry.Message);
                Assert.DoesNotContain(MessageText, entry.Message);
                Assert.DoesNotContain(PrivateDetail, entry.Message);
                Assert.Null(entry.Exception);
            });
        }

        #endregion

        #region Validation

        public static TheoryData<string?, string?, ValidationOutcome> InvalidCommands => new()
        {
            { null, MessageText, ValidationOutcome.PhoneNumberRequired },
            { "0712345678", MessageText, ValidationOutcome.InvalidPhoneNumber },
            { "+254 712 345 678", MessageText, ValidationOutcome.InvalidPhoneNumber },
            { "+2547123456789012", MessageText, ValidationOutcome.InvalidPhoneNumber },
            { PhoneNumber, null, ValidationOutcome.MessageRequired },
            { PhoneNumber, " ", ValidationOutcome.MessageRequired },
            { PhoneNumber, new string('a', 1601), ValidationOutcome.MessageTooLong }
        };

        [Theory]
        [MemberData(nameof(InvalidCommands))]
        public async Task HandleAsync_InvalidCommand_PublishesInvalidWithoutCallingProvider(
            string? phoneNumber, string? message, ValidationOutcome expectedReason)
        {
            var invalid = CreateCommand(command.CommandId, phoneNumber, message);

            var result = await subscriber.HandleAsync(invalid, default);

            Assert.Equal(HandlingResult.Invalid, result);
            Assert.Empty(smsProvider.ReceivedCalls());
            var outcome = Assert.Single(published);
            Assert.Equal(SmsSubmissionStatus.Invalid, outcome.Status);
            Assert.Equal("VALIDATION_FAILED", outcome.ErrorCode);
            Assert.Equal(expectedReason.ToString(), outcome.Reason);
            Assert.Equal(outcome, store.GetOutcome(command.CommandId));
        }

        [Fact]
        public async Task HandleAsync_BoundaryValidCommand_IsSubmitted()
        {
            var boundary = CreateCommand(command.CommandId, "+254712345678901", new string('a', 1600));

            var result = await subscriber.HandleAsync(boundary, default);

            Assert.Equal(HandlingResult.Accepted, result);
            await smsProvider.Received(1).SendAsync(boundary, Arg.Any<CancellationToken>());
        }

        [Fact]
        public async Task HandleAsync_EmptyCommandId_ReturnsInvalidWithoutReservingOrPublishing()
        {
            // Without a CommandId there is nothing to deduplicate on or correlate an event with.
            var result = await subscriber.HandleAsync(CreateCommand(Guid.Empty), default);

            Assert.Equal(HandlingResult.Invalid, result);
            AssertNoExternalCalls();
            Assert.Single(GetLogEntries(), entry => entry.Level == LogLevel.Warning);
        }

        [Fact]
        public async Task HandleAsync_NullCommand_ThrowsWithoutExternalCalls()
        {
            await Assert.ThrowsAsync<ArgumentNullException>(() => subscriber.HandleAsync(null!, default));

            AssertNoExternalCalls();
        }

        [Fact]
        public async Task HandleAsync_ValidatorThrows_LogsErrorAndPropagatesWithoutExternalCalls()
        {
            var failure = new InvalidOperationException("validator bug");
            var throwingValidator = Substitute.For<ICommandValidator>();
            throwingValidator.Validate(Arg.Any<SendSms>()).Returns(_ => throw failure);
            var failingSubscriber = new SendSmsSubscriber(
                smsProvider, commandStore, eventPublisher, throwingValidator, logger);

            var actual = await Record.ExceptionAsync(() => failingSubscriber.HandleAsync(command, default));

            Assert.Same(failure, actual);
            AssertNoExternalCalls();
            Assert.Single(GetLogEntries(), entry => entry.Level == LogLevel.Error);
        }

        #endregion

        #region Idempotency and replay

        [Theory]
        [InlineData(FirstOutcome.Accepted, false)]
        [InlineData(FirstOutcome.Accepted, true)]
        [InlineData(FirstOutcome.Invalid, false)]
        [InlineData(FirstOutcome.Invalid, true)]
        [InlineData(FirstOutcome.Unknown, false)]
        [InlineData(FirstOutcome.Unknown, true)]
        public async Task HandleAsync_Redelivered_ReplaysOriginalEventWithoutResubmitting(
            FirstOutcome firstOutcome, bool payloadChanged)
        {
            var original = firstOutcome == FirstOutcome.Invalid
                ? CreateCommand(command.CommandId, phoneNumber: "bad")
                : command;
            if (firstOutcome == FirstOutcome.Unknown)
            {
                ArrangeProviderFailure(ProviderFailure.ThrowsAsynchronously);
            }

            // Deduplication is by CommandId only; a changed body must not trigger a new submission.
            var redelivery = payloadChanged
                ? CreateCommand(command.CommandId, "+254798765432", "changed-message")
                : original;

            await subscriber.HandleAsync(original, default);
            var result = await subscriber.HandleAsync(redelivery, default);

            Assert.Equal(HandlingResult.Duplicate, result);
            Assert.Equal(firstOutcome == FirstOutcome.Invalid ? 0 : 1, smsProvider.ReceivedCalls().Count());
            await commandStore.Received(1).SaveOutcomeAsync(Arg.Any<SmsSubmissionOutcome>(), Arg.Any<CancellationToken>());
            Assert.Equal(2, published.Count);
            Assert.All(published, outcome => Assert.Equal(store.GetOutcome(command.CommandId), outcome));
        }

        [Fact]
        public async Task HandleAsync_ConcurrentDuplicates_SubmitOnceAndReturnUnresolvedWhileInFlight()
        {
            var providerResponse = new TaskCompletionSource<ProviderResult>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            smsProvider.Configure().SendAsync(Arg.Any<SendSms>(), Arg.Any<CancellationToken>())
                .Returns(providerResponse.Task);

            var owner = subscriber.HandleAsync(command, default);
            HandlingResult[] duplicates;
            try
            {
                // Bounded so a broken reservation fails the test instead of deadlocking on the provider.
                duplicates = await Task.WhenAll(Enumerable.Range(0, 8)
                        .Select(_ => Task.Run(() => subscriber.HandleAsync(command, default))))
                    .WaitAsync(TimeSpan.FromSeconds(5));
                Assert.Empty(published);
            }
            finally
            {
                providerResponse.TrySetResult(new ProviderResult(ProviderResultStatus.Accepted));
            }

            Assert.All(duplicates, result => Assert.Equal(HandlingResult.Unresolved, result));
            Assert.Equal(HandlingResult.Accepted, await owner);
            await smsProvider.Received(1).SendAsync(command, Arg.Any<CancellationToken>());
            Assert.Single(published);
        }

        #endregion

        #region Partial failures

        [Fact]
        public async Task HandleAsync_AcquisitionFails_PropagatesWithoutSubmittingOrPublishing()
        {
            var failure = new IOException("store unavailable");
            commandStore.Configure().TryAcquireAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException<CommandClaim>(failure));

            var actual = await Record.ExceptionAsync(() => subscriber.HandleAsync(command, default));

            // Nothing was reserved, so the broker's redelivery can safely process the command later.
            Assert.Same(failure, actual);
            Assert.Empty(smsProvider.ReceivedCalls());
            Assert.Empty(eventPublisher.ReceivedCalls());
            Assert.False(store.IsReserved(command.CommandId));
        }

        [Fact]
        public async Task HandleAsync_SaveFails_PublishesNothingAndRedeliveryDoesNotResubmit()
        {
            commandStore.Configure().SaveOutcomeAsync(Arg.Any<SmsSubmissionOutcome>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromException(new IOException("private-store-detail")));

            await Assert.ThrowsAsync<IOException>(() => subscriber.HandleAsync(command, default));
            var redelivery = await subscriber.HandleAsync(command, default);

            // The reservation is kept: resubmitting could send the SMS twice.
            Assert.Equal(HandlingResult.Unresolved, redelivery);
            await smsProvider.Received(1).SendAsync(command, Arg.Any<CancellationToken>());
            Assert.Empty(published);
            Assert.Contains(GetLogEntries(), entry => entry.Level == LogLevel.Error);
            Assert.DoesNotContain(GetLogEntries(), entry => entry.Message.Contains("private-store-detail"));
        }

        [Fact]
        public async Task HandleAsync_PublishFails_RedeliveryRepublishesSavedEventWithoutResubmitting()
        {
            var publishAttempts = 0;
            eventPublisher.Configure().PublishAsync(Arg.Any<SmsSubmissionOutcome>(), Arg.Any<CancellationToken>())
                .Returns(call => ++publishAttempts == 1
                    ? Task.FromException(new IOException("broker unavailable"))
                    : Publish(call.ArgAt<SmsSubmissionOutcome>(0), call.ArgAt<CancellationToken>(1)));

            await Assert.ThrowsAsync<IOException>(() => subscriber.HandleAsync(command, default));
            var saved = store.GetOutcome(command.CommandId);
            var redelivery = await subscriber.HandleAsync(command, default);

            Assert.NotNull(saved);
            Assert.Equal(HandlingResult.Duplicate, redelivery);
            await smsProvider.Received(1).SendAsync(command, Arg.Any<CancellationToken>());
            Assert.Equal(saved, Assert.Single(published));
        }

        #endregion

        #region Cancellation

        [Fact]
        public async Task HandleAsync_CancelledBeforeProcessing_ThrowsWithoutExternalCalls()
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => subscriber.HandleAsync(command, new CancellationToken(canceled: true)));

            AssertNoExternalCalls();
        }

        [Theory]
        [InlineData(true, SmsSubmissionStatus.Accepted, null)]
        [InlineData(false, SmsSubmissionStatus.Unknown, "PROVIDER_CALL_CANCELLED")]
        public async Task HandleAsync_CancelledDuringProviderCall_SavesOutcomeAndRedeliveryPublishesIt(
            bool providerCompletes, SmsSubmissionStatus expectedStatus, string? expectedErrorCode)
        {
            using var cts = new CancellationTokenSource();
            smsProvider.Configure().SendAsync(Arg.Any<SendSms>(), Arg.Any<CancellationToken>())
                .Returns(_ =>
                {
                    cts.Cancel();
                    return providerCompletes
                        ? Task.FromResult(new ProviderResult(ProviderResultStatus.Accepted))
                        : Task.FromException<ProviderResult>(new OperationCanceledException(cts.Token));
                });

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => subscriber.HandleAsync(command, cts.Token));

            // The save ignores cancellation, so a possibly-sent SMS is never forgotten.
            await commandStore.Received(1).SaveOutcomeAsync(Arg.Any<SmsSubmissionOutcome>(), CancellationToken.None);
            var saved = store.GetOutcome(command.CommandId);
            Assert.NotNull(saved);
            Assert.Equal(expectedStatus, saved.Status);
            Assert.Equal(expectedErrorCode, saved.ErrorCode);
            Assert.Empty(published);

            Assert.Equal(HandlingResult.Duplicate, await subscriber.HandleAsync(command, default));
            await smsProvider.Received(1).SendAsync(command, Arg.Any<CancellationToken>());
            Assert.Equal(saved, Assert.Single(published));
            // Cancellation interrupts processing, so it is logged like any other processing failure.
            var error = Assert.Single(GetLogEntries(), entry => entry.Level == LogLevel.Error);
            Assert.Contains(nameof(OperationCanceledException), error.Message);
        }

        #endregion

        #region Observability

        [Fact]
        public async Task HandleAsync_ValidCommand_RecordsProviderAndProcessingDurations()
        {
            var providerDurations = new List<double>();
            var processingDurations = new List<double>();
            using var providerListener = ListenForDurations("sms.provider.request.duration", providerDurations);
            using var processingListener = ListenForDurations("sms.command.processing.duration", processingDurations);

            await subscriber.HandleAsync(command, default);

            var providerDuration = Assert.Single(providerDurations);
            var processingDuration = Assert.Single(processingDurations);
            Assert.True(providerDuration >= 0);
            Assert.True(processingDuration >= providerDuration);
        }

        #endregion

        #region Helpers

        private static SendSms CreateCommand(
            Guid commandId, string? phoneNumber = PhoneNumber, string? message = MessageText) =>
            new() { CommandId = commandId, PhoneNumber = phoneNumber!, MessageText = message! };

        private void ProviderReturns(ProviderResult result) =>
            smsProvider.Configure().SendAsync(Arg.Any<SendSms>(), Arg.Any<CancellationToken>()).Returns(result);

        private void ArrangeProviderFailure(ProviderFailure failure)
        {
            var exception = new HttpRequestException(PrivateDetail);
            smsProvider.Configure().SendAsync(Arg.Any<SendSms>(), Arg.Any<CancellationToken>())
                .Returns(_ => failure switch
                {
                    ProviderFailure.ThrowsSynchronously => throw exception,
                    ProviderFailure.ThrowsAsynchronously => Task.FromException<ProviderResult>(exception),
                    ProviderFailure.TimesOut => Task.FromCanceled<ProviderResult>(new CancellationToken(canceled: true)),
                    ProviderFailure.ReturnsNull => Task.FromResult<ProviderResult>(null!),
                    _ => Task.FromResult(new ProviderResult((ProviderResultStatus)999))
                });
        }

        private Task Publish(SmsSubmissionOutcome outcome, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Assert.Equal(outcome, store.GetOutcome(outcome.CommandId));
            published.Enqueue(outcome);
            return Task.CompletedTask;
        }

        private void AssertNoExternalCalls()
        {
            Assert.Empty(commandStore.ReceivedCalls());
            Assert.Empty(smsProvider.ReceivedCalls());
            Assert.Empty(eventPublisher.ReceivedCalls());
        }

        private (LogLevel Level, string Message, Exception? Exception)[] GetLogEntries() =>
            logger.ReceivedCalls()
                .Where(call => call.GetMethodInfo().Name == nameof(ILogger.Log))
                .Select(call =>
                {
                    var arguments = call.GetArguments();
                    return ((LogLevel)arguments[0]!, arguments[2]?.ToString() ?? string.Empty, arguments[3] as Exception);
                })
                .ToArray();

        private static MeterListener ListenForDurations(string instrumentName, List<double> durations)
        {
            var listener = new MeterListener
            {
                InstrumentPublished = (instrument, meterListener) =>
                {
                    if (instrument.Meter.Name == "Mkopa.OutboundSms" && instrument.Name == instrumentName)
                    {
                        meterListener.EnableMeasurementEvents(instrument);
                    }
                }
            };
            listener.SetMeasurementEventCallback<double>((_, value, _, _) => durations.Add(value));
            listener.Start();
            return listener;
        }

        #endregion
    }
}
