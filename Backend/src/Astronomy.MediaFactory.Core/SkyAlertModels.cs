using Astronomy.MediaFactory.Contracts;

namespace Astronomy.MediaFactory.Core;

public interface ISkyAlertService
{
    Task<AlertSubscriberResponse> SubscribeAsync(AlertSubscribeRequest request, CancellationToken cancellationToken);
    Task<AlertSubscriberResponse?> GetPreferencesAsync(Guid subscriberId, CancellationToken cancellationToken);
    Task<AlertSubscriberResponse?> UpdatePreferencesAsync(Guid subscriberId, AlertPreferenceUpdateRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyCollection<AlertUpcomingEventResponse>> GetUpcomingAsync(string? regionId, CancellationToken cancellationToken);
    Task<AlertTestResponse> CreateTestAlertAsync(AlertTestRequest request, CancellationToken cancellationToken);
    Task<bool> UnsubscribeAsync(Guid subscriberId, CancellationToken cancellationToken);
}

public interface ISkyAlertGenerationService
{
    Task<SkyAlertGenerationResult> GenerateAsync(CancellationToken cancellationToken);
    Task<SkyAlertSendResult> SendPendingAsync(CancellationToken cancellationToken);
}

public interface IEmailSender
{
    Task<EmailSendResult> SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken);
}

public sealed record EmailSendResult(bool Sent, string? Error = null, bool ConfigurationMissing = false);
public sealed record SkyAlertGenerationResult(int SubscribersChecked, int EventsChecked, int NotificationsCreated, int DuplicatesSkipped);
public sealed record SkyAlertSendResult(int PendingChecked, int Sent, int Failed, int KeptPending);
