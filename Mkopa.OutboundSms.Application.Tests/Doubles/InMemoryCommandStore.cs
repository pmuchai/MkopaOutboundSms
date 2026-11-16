using Mkopa.OutboundSms.Application.Abstractions;
using Mkopa.OutboundSms.Contracts.Events;
using Mkopa.OutboundSms.Application.Models;
using System.Collections.Concurrent;

namespace Mkopa.OutboundSms.Application.Tests.Doubles
{
    /// <summary>
    /// Stores command reservations and submission outcomes in memory.
    /// </summary>
    /// <remarks>State is not persisted and is lost when the store is discarded.</remarks>
    public sealed class InMemoryCommandStore : ICommandStore
    {
        private readonly ConcurrentDictionary<Guid, InMemoryCommandRecord> records = new();

        public Task<CommandClaim> TryAcquireAsync(
            Guid commandId,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var now = DateTimeOffset.UtcNow;
            var record = new InMemoryCommandRecord(commandId, now, now);

            if (records.TryAdd(commandId, record))
            {
                return Task.FromResult(new CommandClaim(true));
            }

            return Task.FromResult(
                new CommandClaim(false, records[commandId].Outcome));
        }

        public Task SaveOutcomeAsync(
            SmsSubmissionOutcome outcome,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            while (records.TryGetValue(outcome.CommandId, out var current))
            {
                if (current.Outcome is not null)
                {
                    if (current.Outcome == outcome)
                    {
                        return Task.CompletedTask;
                    }

                    throw new InvalidOperationException(
                        "The command already has a different outcome.");
                }

                var updated = current with
                {
                    Outcome = outcome,
                    UpdatedAt = DateTimeOffset.UtcNow
                };

                if (records.TryUpdate(outcome.CommandId, updated, current))
                {
                    return Task.CompletedTask;
                }
            }

            throw new InvalidOperationException("The command has not been reserved.");
        }

        // Test inspection helpers: read state without reserving the ID as TryAcquireAsync would.
        public bool IsReserved(Guid commandId) => records.ContainsKey(commandId);

        public SmsSubmissionOutcome? GetOutcome(Guid commandId) =>
            records.TryGetValue(commandId, out var record) ? record.Outcome : null;

        private sealed record InMemoryCommandRecord(
            Guid CommandId,
            DateTimeOffset CreatedAt,
            DateTimeOffset UpdatedAt,
            SmsSubmissionOutcome? Outcome = null);
    }
}
