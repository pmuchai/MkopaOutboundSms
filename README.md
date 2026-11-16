# M-KOPA Outbound SMS Command Subscriber

## Overview

This solution implements `SendSmsSubscriber`, the Command Subscriber for the `SendSms` command. It validates the command, submits it to the SMS provider at most once per `CommandId`, and publishes an `SmsSubmissionOutcome` event describing the result. Duplicate deliveries never reach the provider again; they republish the original event.

All infrastructure sits behind interfaces in `Abstractions/`: `ICommandStore` (key-value store), `ISmsProvider` (external HTTP API) and `IEventPublisher` (pub-sub channel). The subscriber atomically reserves the `CommandId` before calling the provider, and saves the outcome before publishing it. The store holds only the outcome, never the message body. Tests replace every dependency with substitutes or an in-memory store, so no real queue, store or HTTP call is involved. `Mkopa.OutboundSms.Contracts` contains only the plain command and event classes that client services would reference.

The event's `Status` tells the caller what to do next. `Accepted`: the provider took the request. `Invalid`: validation failed. `Rejected`: the provider permanently refused. `NotAccepted`: the provider definitely did not take it, so the subscriber could safely retry it internally (see Known limitations). `Unknown`: acceptance is uncertain (for example a timeout or provider error), so do not resend blindly. `ErrorCode` and `Reason` carry the detail.

## Assumptions

- Phone numbers must be E.164 (`+` and up to 15 digits); messages are non-blank and at most 1,600 characters.
- Calling services send identical bodies for the same `CommandId`, as the archetype requires.

## Known limitations

- **No retries:** `NotAccepted` is safe to retry internally but isn't yet; next step is a retry queue with a bounded counter that releases the reservation between attempts.
- **Unresolved reservations:** a crash or failed save after reserving leaves no outcome, so redeliveries return `Unresolved`. Reservations never expire, to avoid double sends; callbacks or reconciliation against the provider would resolve them.
- **Duplicate events:** replays republish the original event, so consumers must deduplicate by `EventId`.
- **Empty `CommandId`:** rejected without an event, since there is nothing to correlate it with.

## Build and test (.NET 10 SDK or newer)

There is no runnable host; the subscriber is exercised through its unit tests.

```powershell
dotnet build
dotnet test
```

## AI assistance

After reviewing the requirements, I used GitHub Copilot in VS Code to discuss design tradeoffs, draft structured code tests, and refine documentation. I experimented by simulating concurrent duplicates, redeliveries and failures, and running coverage to find untested paths. I simplified its suggestions (validation, idempotency, provider error handling) and kept only what the unit tests proved.