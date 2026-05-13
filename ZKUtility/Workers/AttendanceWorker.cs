/*
using Microsoft.Extensions.Hosting;
using ZKUtility.Services;

namespace ZKUtility.Workers
{
    public class AttendanceWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;

        public AttendanceWorker(IServiceProvider sp)
        {
            _sp = sp;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                using var scope = _sp.CreateScope();

                var device = scope.ServiceProvider.GetRequiredService<DeviceService>();
                var attendance = scope.ServiceProvider.GetRequiredService<AttendanceService>();

                if (device.Connect("192.168.8.41", 4370))
                {
                    var logs = device.GetLogs(1);

                    attendance.ProcessLogs(logs);

                    device.Disconnect();
                }

                await Task.Delay(10000, stoppingToken);
            }
        }
    }
}
*/
using ZKUtility.Services;

namespace ZKUtility.Workers
{
    public class AttendanceWorker : BackgroundService
    {
        private readonly IServiceProvider _sp;
        private readonly ILogger<AttendanceWorker> _logger;

        // Les 8 appareils ZKTeco (192.168.8.35 → 8.42)
        private static readonly (string Ip, int Port)[] Devices = Enumerable
            .Range(35, 8)
            .Select(i => ($"192.168.8.{i}", 4370))
            .ToArray();

        public AttendanceWorker(IServiceProvider sp, ILogger<AttendanceWorker> logger)
        {
            _sp = sp;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                // Lance tous les appareils en parallèle
                var tasks = Devices.Select(d => PollDeviceAsync(d.Ip, d.Port, stoppingToken));
                await Task.WhenAll(tasks);

                await Task.Delay(1, stoppingToken);
            }
        }

        private async Task PollDeviceAsync(string ip, int port, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _sp.CreateScope();
                var device = scope.ServiceProvider.GetRequiredService<DeviceService>();
                var attendance = scope.ServiceProvider.GetRequiredService<AttendanceService>();

                if (!device.Connect(ip, port))
                {
                    _logger.LogWarning("⚠️  Appareil {Ip}:{Port} injoignable — skippé", ip, port);
                    return; // Skip proprement
                }

                _logger.LogInformation("✅ Connecté à {Ip}:{Port}", ip, port);
                try
                {
                    var logs = device.GetLogs(1);
                    var events = attendance.ProcessLogs(logs, ip); // ✅ maintenant ça retourne List<string>

                    _logger.LogInformation("📋 {Count} log(s) depuis {Ip}", logs?.Count ?? 0, ip);

                    if (events.Count > 0)
                        foreach (var e in events)
                            _logger.LogInformation("   {Event}", e);
                    else
                        _logger.LogInformation("   ℹ️  Aucun événement sur {Ip}", ip);
                }
                finally
                {
                    device.Disconnect();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Erreur inattendue sur {Ip}:{Port}", ip, port);
            }
        }
    }
}