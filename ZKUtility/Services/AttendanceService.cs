/*
using ZKUtility.Models;

namespace ZKUtility.Services
{
    public class AttendanceService
    {

        List<string> events = new List<string>();

        private static List<Attendance> _data = new();
        private static DateTime _lastSync = DateTime.MinValue;

        public void ProcessLogs(List<DeviceLog> logs)
        {
            var newLogs = logs
                .Where(x => x.LogTime > _lastSync)
                .OrderBy(x => x.LogTime)
                .ToList();

            foreach (var log in newLogs)
            {
                var date = log.LogTime.Date;
                var time = log.LogTime.TimeOfDay;

                var att = _data.FirstOrDefault(x =>
                    x.EnrollNumber == log.EnrollNumber &&
                    x.Date == date);

                // 🔥 ANTI DOUBLE SCAN (60 sec)
                if (att != null)
                {
                    var lastTime = att.CheckOut ?? att.CheckIn;

                    if (lastTime != null &&
                        (log.LogTime - lastTime.Value).TotalSeconds < 60)
                    {
                        continue;
                    }
                }

                // =========================
                // 🟢 GESTION CHECK-IN
                // =========================
                if (time >= new TimeSpan(7, 0, 0) && time <= new TimeSpan(8, 0, 0))
                {
                    // si déjà check-in → ignorer
                    if (att != null && att.CheckIn != null)
                        continue;

                    if (att == null)
                    {
                        att = new Attendance
                        {
                            EnrollNumber = (int)log.EnrollNumber,
                            Date = date
                        };
                        _data.Add(att);
                    }

                    att.CheckIn = log.LogTime;
                    att.CheckInMode = log.VerifyMode;
                    //                    Console.WriteLine($"🟢 IN {log.EnrollNumber} {log.LogTime} {log.VerifyMode}");
                    events.Add($"🟢 IN  | #{log.EnrollNumber,-6} | {log.LogTime:HH:mm:ss} | Mode: {log.VerifyMode}");
                }

                // =========================
                // 🔴 GESTION CHECK-OUT
                // =========================
                else if (time >= new TimeSpan(16, 0, 0) && time <= new TimeSpan(18, 0, 0))
                {
                    // si pas de check-in → ignorer
                    if (att == null || att.CheckIn == null)
                        continue;

                    // si déjà check-out → ignorer
                    if (att.CheckOut != null)
                        continue;

                    att.CheckOut = log.LogTime;
                    att.CheckOutMode = log.VerifyMode;
                    events.Add($"🔴 OUT | #{log.EnrollNumber,-6} | {log.LogTime:HH:mm:ss} | Mode: {log.VerifyMode}");
                    //                    Console.WriteLine($"🔴 OUT {log.EnrollNumber} {log.LogTime} {log.VerifyMode}");
                }

                // =========================
                // ❌ IGNORER LE RESTE
                // =========================
                else
                {
                    continue;
                }
            }

            if (newLogs.Any())
                _lastSync = newLogs.Max(x => x.LogTime);
        }

        public List<Attendance> GetAll() => _data;
    }
}


using ZKUtility.Models;

namespace ZKUtility.Services
{
    public class AttendanceService
    {
        private static List<Attendance> _data = new();

        private static Dictionary<string, DateTime> _lastSyncPerDevice = new();

        private const int MIN_INTERVAL_SECONDS = 300; // 5 minutes

        public List<string> ProcessLogs(List<DeviceLog> logs, string deviceIp)
        {
            var events = new List<string>();

            if (!_lastSyncPerDevice.ContainsKey(deviceIp))
                _lastSyncPerDevice[deviceIp] = DateTime.MinValue;

            var _lastSync = _lastSyncPerDevice[deviceIp];

            var newLogs = logs
                .Where(x => x.LogTime > _lastSync)
                .OrderBy(x => x.LogTime)
                .ToList();

            foreach (var log in newLogs)
            {
                var date = log.LogTime.Date;

                // Cherche l'entrée du jour EN COURS (pas encore fermée)
                // "pas encore fermée" = dernière session sans CheckOut
                // ❌ AVANT — cherche par date (rate le toggle)
                var att = _data
                    .Where(x => x.EnrollNumber == log.EnrollNumber)
                    .OrderByDescending(x => x.Date)
                    .FirstOrDefault();

                // ✅ Anti double scan AVANT le toggle
                var lastSession = _data
                    .Where(x => x.EnrollNumber == log.EnrollNumber)
                    .OrderByDescending(x => x.CheckIn)
                    .FirstOrDefault();

                // 🔥 ANTI DOUBLE SCAN (5 min) — vérifie le dernier pointage peu importe IN ou OUT
                if (lastSession != null)
                {
                    var lastTime = lastSession.CheckOut ?? lastSession.CheckIn;
                    if (lastTime != null)
                    {
                        // log anterieur
                        if (log.LogTime <= lastTime.Value)
                        {
                            continue;
                        }
                        if ((log.LogTime - lastTime.Value).TotalSeconds < 300)
                        {
                            events.Add($"⏭️  SKIP | #{log.EnrollNumber,-6} | {log.LogTime:dd/MM HH:mm:ss} | trop rapide");
                            continue;
                        }
                    }
                }
                if (lastSession == null || lastSession.CheckOut != null)
                {
                    // 🟢 CHECK-IN
                    var newAtt = new Attendance
                    {
                        EnrollNumber = (int)log.EnrollNumber,
                        Date = log.LogTime.Date,
                        CheckIn = log.LogTime,
                        CheckInMode = log.VerifyMode
                    };
                    _data.Add(newAtt);
                    events.Add($"🟢 IN  | #{log.EnrollNumber,-6} | {log.LogTime:dd/MM HH:mm:ss} | Mode: {log.VerifyMode}");
                }
                else if (lastSession.CheckIn != null && lastSession.CheckOut == null)
                {
                    // 🔴 CHECK-OUT
                    lastSession.CheckOut = log.LogTime;
                    lastSession.CheckOutMode = log.VerifyMode;

                    var duration = lastSession.CheckOut.Value - lastSession.CheckIn.Value;
                    events.Add($"🔴 OUT | #{log.EnrollNumber,-6} | {log.LogTime:dd/MM HH:mm:ss} | Mode: {log.VerifyMode} | ⏱ {duration:hh\\:mm}");
                }
            }

            if (newLogs.Any())
                _lastSync = newLogs.Max(x => x.LogTime);

            return events;
        }

        public List<Attendance> GetAll() => _data;
    }
}
*/
using System.Text;
using System.Text.Json;
using ZKUtility.Models;

namespace ZKUtility.Services
{
    public class AttendanceService
    {
        private static List<Attendance> _data = new();
        private static Dictionary<string, DateTime> _lastSyncPerDevice = new();
        private const int MIN_INTERVAL_SECONDS = 300;
        private const string BACK_URL = "http://localhost:5159/api/Attendance";

        private readonly HttpClient _http;

        public AttendanceService(HttpClient http)
        {
            _http = http;
        }

        public List<string> ProcessLogs(List<DeviceLog> logs, string deviceIp)
        {
            var events = new List<string>();

            if (!_lastSyncPerDevice.ContainsKey(deviceIp))
                _lastSyncPerDevice[deviceIp] = DateTime.MinValue;

            var lastSync = _lastSyncPerDevice[deviceIp];

            var newLogs = logs
                .Where(x => x.LogTime > lastSync)
                .OrderBy(x => x.LogTime)
                .ToList();

            foreach (var log in newLogs)
            {
                var lastSession = _data
                    .Where(x => x.EnrollNumber == log.EnrollNumber)
                    .OrderByDescending(x => x.CheckIn)
                    .FirstOrDefault();

                // 🔥 ANTI DOUBLE SCAN
                if (lastSession != null)
                {
                    var lastTime = lastSession.CheckOut ?? lastSession.CheckIn;
                    if (lastTime != null)
                    {
                        if (log.LogTime <= lastTime.Value)
                            continue;

                        if ((log.LogTime - lastTime.Value).TotalSeconds < MIN_INTERVAL_SECONDS)
                        {
 //                           events.Add($"⏭️  SKIP | #{log.EnrollNumber,-6} | {log.LogTime:dd/MM HH:mm:ss} | trop rapide");
                            continue;
                        }
                    }
                }

                // 🟢 CHECK-IN
                if (lastSession == null || lastSession.CheckOut != null)
                {
                    var newAtt = new Attendance
                    {
                        EnrollNumber = (int)log.EnrollNumber,
                        Date = log.LogTime.Date,
                        CheckIn = log.LogTime,
                        CheckInMode = log.VerifyMode
                    };
                    _data.Add(newAtt);
 //                   events.Add($"🟢 IN  | #{log.EnrollNumber,-6} | {log.LogTime:dd/MM HH:mm:ss} | Mode: {log.VerifyMode}");

                    // 👇 POST automatique vers le back
                    _ = PushAsync(new
                    {
                        userId = log.EnrollNumber,
                        date = log.LogTime.Date,
                        checkIn = (DateTime?)log.LogTime,
                        checkOut = (DateTime?)null,
                        checkInMode = log.VerifyMode,
                        checkOutMode = (string?)null
                    });
                }

                // 🔴 CHECK-OUT
                else if (lastSession.CheckIn != null && lastSession.CheckOut == null)
                {
                    lastSession.CheckOut = log.LogTime;
                    lastSession.CheckOutMode = log.VerifyMode;

                    var duration = lastSession.CheckOut.Value - lastSession.CheckIn.Value;
 //                   events.Add($"🔴 OUT | #{log.EnrollNumber,-6} | {log.LogTime:dd/MM HH:mm:ss} | Mode: {log.VerifyMode} | ⏱ {duration:hh\\:mm}");

                    // 👇 POST automatique vers le back
                    _ = PushAsync(new
                    {
                        userId = lastSession.EnrollNumber,
                        date = lastSession.Date,
                        checkIn = lastSession.CheckIn,
                        checkOut = lastSession.CheckOut,
                        checkInMode = lastSession.CheckInMode,
                        checkOutMode = lastSession.CheckOutMode
                    });
                }
            }

            if (newLogs.Any())
                _lastSyncPerDevice[deviceIp] = newLogs.Max(x => x.LogTime);

            return events;
        }

        private async Task PushAsync(object payload)
        {
            try
            {
                var json = JsonSerializer.Serialize(payload);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await _http.PostAsync(BACK_URL, content);

                if (response.IsSuccessStatusCode)
                    Console.WriteLine($"✅ Push OK → {BACK_URL}");
                else
                    Console.WriteLine($"❌ Push failed: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"❌ Push error: {ex.Message}");
            }
        }

        public List<Attendance> GetAll() => _data;
    }
}
