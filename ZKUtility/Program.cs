/*using ZKUtility.Services;
using ZKUtility.Workers;


var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddScoped<AttendanceService>();

// 🔥 LE WORKER
builder.Services.AddHostedService<AttendanceWorker>();
// 🔥 AJOUT IMPORTANT
builder.Services.AddScoped<DeviceService>();



var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();

app.MapControllers();

app.Run();
*/
using ZKUtility.Services;
using ZKUtility.Workers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ✅ Singleton — partagé entre Worker ET API
builder.Services.AddSingleton<AttendanceService>();

// ✅ Scoped — ok pour DeviceService (connexion physique)
builder.Services.AddScoped<DeviceService>();

// ✅ Worker background
builder.Services.AddHostedService<AttendanceWorker>();

// 👇 Ajoute HttpClient pour les push
builder.Services.AddHttpClient<AttendanceService>();
builder.Services.AddSingleton<AttendanceService>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.Run();