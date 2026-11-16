using Mkopa.OutboundSms.Application.Enums;

namespace Mkopa.OutboundSms.Application.Abstractions
{
    public interface ICommandSubscriber<TCommand>
    {
        /// <summary>
        /// Processes the specified command asynchronously.
        /// </summary>
        /// <param name="command">The command to process.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation requests.</param>
        /// <returns>A task that represents the asynchronous operation and contains the command handling result.</returns>
        Task<HandlingResult> HandleAsync(TCommand command, CancellationToken cancellationToken);
    }
}
