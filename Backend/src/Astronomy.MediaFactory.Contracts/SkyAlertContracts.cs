using System.Text.Json.Serialization;

namespace Astronomy.MediaFactory.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlertPreferredChannel { Email = 1, Push = 2, WhatsAppLater = 3 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlertNotificationChannel { Email = 1, Push = 2, WhatsAppLater = 3 }

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AlertNotificationStatus { Pending = 1, Sent = 2, Failed = 3, Skipped = 4 }

public sealed record AlertSubscribeRequest(
    string Email,
    string RegionId,
    string Language,
    IReadOnlyCollection<string> EventTypes,
    string PreferredAlertTimeLocal,
    double? MinimumEventScore,
    AlertPreferredChannel PreferredChannel = AlertPreferredChannel.Email,
    string? Phone = null,
    bool DailySkyGuideReminderEnabled = true,
    bool SpecialEventAlertsEnabled = true);

public sealed record AlertPreferenceUpdateRequest(
    IReadOnlyCollection<string> EventTypes,
    string PreferredAlertTimeLocal,
    double? MinimumEventScore,
    bool DailySkyGuideReminderEnabled,
    bool SpecialEventAlertsEnabled,
    AlertPreferredChannel? PreferredChannel = null,
    string? Phone = null,
    string? Language = null);

public sealed record AlertSubscriberResponse(
    Guid SubscriberId,
    string Email,
    string? Phone,
    AlertPreferredChannel PreferredChannel,
    string RegionId,
    string Language,
    bool IsActive,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    AlertPreferencesResponse Preferences);

public sealed record AlertPreferencesResponse(
    Guid SubscriberId,
    IReadOnlyCollection<string> EventTypes,
    string PreferredAlertTimeLocal,
    double MinimumEventScore,
    bool DailySkyGuideReminderEnabled,
    bool SpecialEventAlertsEnabled);

public sealed record AlertUpcomingEventResponse(
    string EventId,
    string EventType,
    string Title,
    string Description,
    string RegionId,
    DateTimeOffset StartUtc,
    DateTimeOffset? PeakUtc,
    DateTimeOffset EndUtc,
    double Score,
    IReadOnlyCollection<string> VisibilityRegions);

public sealed record AlertTestRequest(Guid SubscriberId, string? EventId = null);

public sealed record AlertTestResponse(Guid NotificationId, AlertNotificationStatus Status, string Message);
