namespace Mkopa.OutboundSms.Application.Abstractions
{
    public interface IEventPublisher
    {
        /// <summary>
        /// Publishes an event asynchronously.
        /// </summary>
        /// <typeparam name="TEvent">The type of the event being published.</typeparam>
        /// <param name="message">The event to publish.</param>
        /// <param name="cancellationToken">A token to monitor for cancellation.</param>
        /// <returns>A task that represents the asynchronous publish operation.</returns>
        Task PublishAsync<TEvent>(TEvent message, CancellationToken cancellationToken);
    }
}
