using Microsoft.Extensions.Options;

namespace DATA_EXCHANGE_HUB.Services;

public sealed class SeshatReminderSchedulerService(
    IServiceScopeFactory scopeFactory,
    IOptionsMonitor<SeshatReminderOptions> options,
    ILogger<SeshatReminderSchedulerService> logger) : BackgroundService
{
    private DateTime? _lastRunDate;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var currentOptions = options.CurrentValue;
            try
            {
                var now = DateTime.Now;
                var sendHour = currentOptions.NormalizedSendHour;
                if (now.Hour >= sendHour && _lastRunDate != now.Date)
                {
                    using var scope = scopeFactory.CreateScope();
                    var seshatService = scope.ServiceProvider.GetRequiredService<ISeshatService>();
                    var sent = await seshatService.SendDueRemindersAsync(now.Date, stoppingToken);
                    _lastRunDate = now.Date;
                    logger.LogInformation(
                        "SESHAT reminder scheduler checked {Date} after {SendTime}; sent {Count} email reminder(s).",
                        now.Date,
                        currentOptions.SendTimeText,
                        sent);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "SESHAT reminder scheduler failed.");
            }

            await Task.Delay(TimeSpan.FromMinutes(currentOptions.NormalizedCheckEveryMinutes), stoppingToken);
        }
    }
}
