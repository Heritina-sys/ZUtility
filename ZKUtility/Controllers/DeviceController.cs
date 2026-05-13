using Microsoft.AspNetCore.Mvc;
using System.Text;
using System.Text.Json;
using ZKUtility.Services;
using static System.Net.WebRequestMethods;

namespace ZKUtility.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class DeviceController : ControllerBase
    {
        private readonly AttendanceService _attendanceService;

        private readonly HttpClient _http;
        private const string BACK_URL = "http://localhost:5159/api/attendance";

        public DeviceController(AttendanceService attendanceService)
        {
            _attendanceService = attendanceService;
        }

        [HttpGet("c")]
        public IActionResult GetAttendance()
        {
            var data = _attendanceService.GetAll();
            return Ok(data);
        }

        [HttpPost("sync")]
        public async Task<IActionResult> Sync()
        {
            var data = _attendanceService.GetAll();

            var json = JsonSerializer.Serialize(data);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _http.PostAsync(BACK_URL, content);

            if (!response.IsSuccessStatusCode)
                return StatusCode((int)response.StatusCode, "Erreur push vers le back");

            return Ok($"✅ {data.Count} enregistrements envoyés vers la DB");
        }
    }
}