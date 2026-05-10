using System.Net.Mail;
using System.Text.Json;
using System.Text.RegularExpressions;
using Astronomy.MediaFactory.Contracts;
using Astronomy.MediaFactory.Core;
using Astronomy.MediaFactory.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Astronomy.MediaFactory.Infrastructure.Alerting;

public sealed class SkyAlertService : ISkyAlertService
{
    private static readonly Regex EmailRegex = new("^[^@\\s]+@[^@\\s]+\\.[^@\\s]+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> SupportedEventTypes = new(StringComparer.OrdinalIgnoreCase) { "MeteorShower", "FullMoon", "Eclipse", "DailySkyGuide", "SpecialEvent", "meteor_shower", "full_moon", "eclipse", "moon_phase" };
    private readonly MediaFactoryDbContext _db;
    private readonly IAstronomyEventDiscoveryService _events;
    private readonly SchedulerOptions _schedulerOptions;
    private readonly LocalizationOptions _localizationOptions;
    private readonly AlertsOptions _alertsOptions;
    private readonly TimeProvider _timeProvider;

    public SkyAlertService(MediaFactoryDbContext db, IAstronomyEventDiscoveryService events, IOptions<SchedulerOptions> schedulerOptions, IOptions<LocalizationOptions> localizationOptions, IOptions<AlertsOptions> alertsOptions, TimeProvider timeProvider)
    {
        _db = db;
        _events = events;
        _schedulerOptions = schedulerOptions.Value;
        _localizationOptions = localizationOptions.Value;
        _alertsOptions = alertsOptions.Value;
        _timeProvider = timeProvider;
    }

    public async Task<AlertSubscriberResponse> SubscribeAsync(AlertSubscribeRequest request, CancellationToken cancellationToken)
    {
        Validate(request.Email, request.RegionId, request.Language, request.EventTypes, request.PreferredAlertTimeLocal, request.MinimumEventScore, request.PreferredChannel);
        var email = request.Email.Trim().ToLowerInvariant();
        var exists = await _db.AlertSubscribers.AnyAsync(x => x.IsActive && x.Email == email && x.RegionId == request.RegionId, cancellationToken);
        if (exists) throw new InvalidOperationException("An active alert subscription already exists for this email and region.");

        var subscriber = new AlertSubscriber
        {
            Email = email,
            Phone = string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim(),
            PreferredChannel = request.PreferredChannel,
            RegionId = request.RegionId.Trim(),
            Language = request.Language.Trim(),
            IsActive = true,
            Preferences = new AlertPreferences
            {
                EventTypes = NormalizeEventTypes(request.EventTypes),
                PreferredAlertTimeLocal = request.PreferredAlertTimeLocal.Trim(),
                MinimumEventScore = request.MinimumEventScore ?? _alertsOptions.DefaultMinimumEventScore,
                DailySkyGuideReminderEnabled = request.DailySkyGuideReminderEnabled,
                SpecialEventAlertsEnabled = request.SpecialEventAlertsEnabled
            }
        };
        _db.AlertSubscribers.Add(subscriber);
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(subscriber, subscriber.Preferences!);
    }

    public async Task<AlertSubscriberResponse?> GetPreferencesAsync(Guid subscriberId, CancellationToken cancellationToken)
    {
        var subscriber = await _db.AlertSubscribers.Include(x => x.Preferences).FirstOrDefaultAsync(x => x.Id == subscriberId, cancellationToken);
        return subscriber?.Preferences is null ? null : ToResponse(subscriber, subscriber.Preferences);
    }

    public async Task<AlertSubscriberResponse?> UpdatePreferencesAsync(Guid subscriberId, AlertPreferenceUpdateRequest request, CancellationToken cancellationToken)
    {
        var subscriber = await _db.AlertSubscribers.Include(x => x.Preferences).FirstOrDefaultAsync(x => x.Id == subscriberId, cancellationToken);
        if (subscriber?.Preferences is null) return null;
        Validate(subscriber.Email, subscriber.RegionId, request.Language ?? subscriber.Language, request.EventTypes, request.PreferredAlertTimeLocal, request.MinimumEventScore, request.PreferredChannel ?? subscriber.PreferredChannel);
        subscriber.Language = request.Language?.Trim() ?? subscriber.Language;
        subscriber.Phone = request.Phone is null ? subscriber.Phone : (string.IsNullOrWhiteSpace(request.Phone) ? null : request.Phone.Trim());
        subscriber.PreferredChannel = request.PreferredChannel ?? subscriber.PreferredChannel;
        subscriber.Touch();
        subscriber.Preferences.EventTypes = NormalizeEventTypes(request.EventTypes);
        subscriber.Preferences.PreferredAlertTimeLocal = request.PreferredAlertTimeLocal.Trim();
        subscriber.Preferences.MinimumEventScore = request.MinimumEventScore ?? _alertsOptions.DefaultMinimumEventScore;
        subscriber.Preferences.DailySkyGuideReminderEnabled = request.DailySkyGuideReminderEnabled;
        subscriber.Preferences.SpecialEventAlertsEnabled = request.SpecialEventAlertsEnabled;
        await _db.SaveChangesAsync(cancellationToken);
        return ToResponse(subscriber, subscriber.Preferences);
    }

    public async Task<IReadOnlyCollection<AlertUpcomingEventResponse>> GetUpcomingAsync(string? regionId, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(regionId) && !IsValidRegion(regionId)) throw new ArgumentException("Invalid regionId.");
        var events = await _events.GetUpcomingAsync(30, cancellationToken);
        var region = string.IsNullOrWhiteSpace(regionId) ? _schedulerOptions.Regions.Items.FirstOrDefault()?.RegionId ?? "global" : regionId.Trim();
        return events.Where(e => IsVisibleInRegion(e, region)).Select(e => new AlertUpcomingEventResponse(e.EventId, ToPublicEventType(e.EventType), e.Title, e.Description, region, e.StartUtc, e.PeakUtc, e.EndUtc, e.ContentOpportunityScore, e.VisibilityRegions)).ToArray();
    }

    public async Task<AlertTestResponse> CreateTestAlertAsync(AlertTestRequest request, CancellationToken cancellationToken)
    {
        var subscriber = await _db.AlertSubscribers.FirstOrDefaultAsync(x => x.Id == request.SubscriberId && x.IsActive, cancellationToken);
        if (subscriber is null) throw new KeyNotFoundException("Active subscriber was not found.");
        var notification = new AlertNotification
        {
            SubscriberId = subscriber.Id,
            EventId = request.EventId,
            RegionId = subscriber.RegionId,
            Title = "AstroPulse test sky alert",
            Message = "This is a safe test alert for your AstroPulse sky-alert subscription.",
            Channel = AlertNotificationChannel.Email,
            Status = AlertNotificationStatus.Pending,
            ScheduledUtc = _timeProvider.GetUtcNow()
        };
        _db.AlertNotifications.Add(notification);
        await _db.SaveChangesAsync(cancellationToken);
        return new AlertTestResponse(notification.Id, notification.Status, "Test alert queued for the subscriber only.");
    }

    public async Task<bool> UnsubscribeAsync(Guid subscriberId, CancellationToken cancellationToken)
    {
        var subscriber = await _db.AlertSubscribers.FirstOrDefaultAsync(x => x.Id == subscriberId, cancellationToken);
        if (subscriber is null) return false;
        subscriber.IsActive = false;
        subscriber.Touch();
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private void Validate(string email, string regionId, string language, IReadOnlyCollection<string> eventTypes, string preferredTime, double? minimumScore, AlertPreferredChannel channel)
    {
        if (channel == AlertPreferredChannel.Email && (string.IsNullOrWhiteSpace(email) || !EmailRegex.IsMatch(email.Trim()))) throw new ArgumentException("A valid email is required for Email alerts.");
        if (!IsValidRegion(regionId)) throw new ArgumentException("Invalid regionId.");
        if (!_localizationOptions.SupportedLanguages.Contains(language, StringComparer.OrdinalIgnoreCase)) throw new ArgumentException("Unsupported language.");
        if (eventTypes.Count == 0 || eventTypes.Any(type => !SupportedEventTypes.Contains(type))) throw new ArgumentException("Unsupported event type.");
        if (!TimeOnly.TryParse(preferredTime, out _)) throw new ArgumentException("preferredAlertTimeLocal must use HH:mm format.");
        if (minimumScore is < 0 or > 1) throw new ArgumentException("minimumEventScore must be between 0 and 1.");
    }

    private bool IsValidRegion(string regionId) => _schedulerOptions.Regions.Items.Any(x => x.RegionId.Equals(regionId?.Trim(), StringComparison.OrdinalIgnoreCase));
    internal static string[] NormalizeEventTypes(IEnumerable<string> eventTypes) => eventTypes.Select(ToPublicEventType).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    internal static string ToPublicEventType(string type) => type.Trim().ToLowerInvariant() switch { "meteor_shower" or "meteorshower" or "meteor showers" => "MeteorShower", "full_moon" or "fullmoon" or "full moon / supermoon" => "FullMoon", "eclipse" or "eclipses" => "Eclipse", "dailyskyguide" or "daily sky guide reminders" => "DailySkyGuide", "specialevent" or "special event videos" => "SpecialEvent", _ => type.Trim() };
    internal static bool IsVisibleInRegion(AstronomyEvent e, string regionId) => e.GlobalVisibility || e.VisibilityRegions.Length == 0 || e.VisibilityRegions.Any(v => v.Contains("Global", StringComparison.OrdinalIgnoreCase) || v.Contains("Northern", StringComparison.OrdinalIgnoreCase) || v.Contains("Southern", StringComparison.OrdinalIgnoreCase));
    internal static AlertSubscriberResponse ToResponse(AlertSubscriber subscriber, AlertPreferences preferences) => new(subscriber.Id, subscriber.Email, subscriber.Phone, subscriber.PreferredChannel, subscriber.RegionId, subscriber.Language, subscriber.IsActive, subscriber.CreatedUtc, subscriber.UpdatedUtc ?? subscriber.CreatedUtc, new AlertPreferencesResponse(subscriber.Id, preferences.EventTypes, preferences.PreferredAlertTimeLocal, preferences.MinimumEventScore, preferences.DailySkyGuideReminderEnabled, preferences.SpecialEventAlertsEnabled));
}

public sealed class SkyAlertGenerationService : BackgroundService, ISkyAlertGenerationService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web) { WriteIndented = true };
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly AlertsOptions _options;
    private readonly MaintenanceOptions _maintenanceOptions;
    private readonly ILogger<SkyAlertGenerationService> _logger;

    public SkyAlertGenerationService(IServiceScopeFactory scopeFactory, IOptions<AlertsOptions> options, IOptions<MaintenanceOptions> maintenanceOptions, ILogger<SkyAlertGenerationService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _maintenanceOptions = maintenanceOptions.Value;
        _logger = logger;
    }

    public async Task<SkyAlertGenerationResult> GenerateAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaFactoryDbContext>();
        var eventService = scope.ServiceProvider.GetRequiredService<IAstronomyEventDiscoveryService>();
        var now = DateTimeOffset.UtcNow;
        var subscribers = await db.AlertSubscribers.Include(x => x.Preferences).Where(x => x.IsActive && x.Preferences != null).ToArrayAsync(cancellationToken);
        var events = await eventService.GetUpcomingAsync(30, cancellationToken);
        var created = 0; var duplicates = 0;
        foreach (var subscriber in subscribers)
        {
            foreach (var e in events.Where(e => SkyAlertService.IsVisibleInRegion(e, subscriber.RegionId)))
            {
                var prefs = subscriber.Preferences!;
                if (e.ContentOpportunityScore < prefs.MinimumEventScore || !prefs.EventTypes.Contains(SkyAlertService.ToPublicEventType(e.EventType), StringComparer.OrdinalIgnoreCase)) continue;
                var exists = await db.AlertNotifications.AnyAsync(x => x.SubscriberId == subscriber.Id && x.EventId == e.EventId && x.RegionId == subscriber.RegionId, cancellationToken);
                if (exists) { duplicates++; continue; }
                var perDay = await db.AlertNotifications.CountAsync(x => x.SubscriberId == subscriber.Id && x.ScheduledUtc >= now.Date && x.ScheduledUtc < now.Date.AddDays(1), cancellationToken);
                if (perDay >= _options.MaxAlertsPerSubscriberPerDay) break;
                db.AlertNotifications.Add(new AlertNotification { SubscriberId = subscriber.Id, EventId = e.EventId, RegionId = subscriber.RegionId, Title = e.Title, Message = $"{e.Title}: {e.Description}", Channel = AlertNotificationChannel.Email, Status = AlertNotificationStatus.Pending, ScheduledUtc = now });
                created++;
            }
        }
        await db.SaveChangesAsync(cancellationToken);
        var result = new SkyAlertGenerationResult(subscribers.Length, events.Count, created, duplicates);
        await WriteReportAsync("alert-generation-report.json", result, cancellationToken);
        return result;
    }

    public async Task<SkyAlertSendResult> SendPendingAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<MediaFactoryDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IEmailSender>();
        var pending = await db.AlertNotifications.Where(x => x.Status == AlertNotificationStatus.Pending && x.Channel == AlertNotificationChannel.Email && x.ScheduledUtc <= DateTimeOffset.UtcNow).OrderBy(x => x.ScheduledUtc).Take(50).ToArrayAsync(cancellationToken);
        var sent = 0; var failed = 0; var kept = 0;
        foreach (var notification in pending)
        {
            var subscriber = await db.AlertSubscribers.FirstOrDefaultAsync(x => x.Id == notification.SubscriberId && x.IsActive, cancellationToken);
            if (subscriber is null) { notification.Status = AlertNotificationStatus.Skipped; notification.Error = "Subscriber inactive or missing."; continue; }
            var result = await sender.SendAsync(subscriber.Email, notification.Title, notification.Message, cancellationToken);
            if (result.Sent) { notification.Status = AlertNotificationStatus.Sent; notification.SentUtc = DateTimeOffset.UtcNow; sent++; }
            else if (result.ConfigurationMissing) { notification.Status = AlertNotificationStatus.Pending; notification.Error = result.Error; kept++; _logger.LogWarning("Email service is disabled or incomplete; sky alert notification {NotificationId} remains Pending.", notification.Id); }
            else { notification.Status = AlertNotificationStatus.Failed; notification.Error = result.Error; failed++; }
        }
        await db.SaveChangesAsync(cancellationToken);
        var report = new SkyAlertSendResult(pending.Length, sent, failed, kept);
        await WriteReportAsync("alert-send-report.json", report, cancellationToken);
        return report;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled) return;
        while (!stoppingToken.IsCancellationRequested)
        {
            await GenerateAsync(stoppingToken);
            await SendPendingAsync(stoppingToken);
            await Task.Delay(TimeSpan.FromMinutes(Math.Max(1, Math.Min(_options.GenerateEveryMinutes, _options.SendEveryMinutes))), stoppingToken);
        }
    }

    private async Task WriteReportAsync<T>(string fileName, T payload, CancellationToken cancellationToken)
    {
        var dir = string.IsNullOrWhiteSpace(_maintenanceOptions.WorkingDirectory) ? "./media-output" : _maintenanceOptions.WorkingDirectory;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(Path.Combine(dir, fileName), JsonSerializer.Serialize(new { generatedUtc = DateTimeOffset.UtcNow, payload }, JsonOptions), cancellationToken);
    }
}

public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailOptions _options;
    public SmtpEmailSender(IOptions<EmailOptions> options) => _options = options.Value;
    public async Task<EmailSendResult> SendAsync(string toEmail, string subject, string body, CancellationToken cancellationToken)
    {
        if (!_options.Enabled || string.IsNullOrWhiteSpace(_options.SmtpHost) || string.IsNullOrWhiteSpace(_options.FromEmail)) return new(false, "Email service is disabled or SMTP settings are incomplete.", ConfigurationMissing: true);
        using var message = new MailMessage { From = new MailAddress(_options.FromEmail, _options.FromName), Subject = subject, Body = body };
        message.To.Add(toEmail);
        using var client = new SmtpClient(_options.SmtpHost, _options.SmtpPort) { EnableSsl = true };
        if (!string.IsNullOrWhiteSpace(_options.Username)) client.Credentials = new System.Net.NetworkCredential(_options.Username, _options.Password);
        await client.SendMailAsync(message, cancellationToken);
        return new(true);
    }
}
