using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Nms.Api.Alerting;
using Nms.Api.Auth;
using Nms.Api.Data;
using Nms.Api.Endpoints;
using Nms.Api.Hubs;
using Nms.Api.Monitoring;
using Nms.Api.Reporting;

var builder = WebApplication.CreateBuilder(args);
var config = builder.Configuration;

builder.Services.AddDbContext<NmsDbContext>(options =>
    options.UseNpgsql(config.GetConnectionString("Nms")
                      ?? throw new InvalidOperationException("ConnectionStrings:Nms is not configured")));

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSignalR()
    .AddJsonProtocol(options => options.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton<LiveTelemetryCache>();
builder.Services.AddSingleton<BreachTracker>();
builder.Services.AddSingleton<MonitoringBroadcaster>();
builder.Services.AddSingleton<NotificationDispatcher>();
builder.Services.AddSingleton<TokenIssuer>();
builder.Services.AddHttpClient("notifications", client => client.Timeout = TimeSpan.FromSeconds(5));

builder.Services.AddScoped<AlertEngine>();
builder.Services.AddScoped<TopologyService>();
builder.Services.AddScoped<DiagnosticRunner>();
builder.Services.AddScoped<SqlQuery>();
builder.Services.AddScoped<ReportQueries>();
builder.Services.AddScoped<DatabaseBootstrapper>();

// Synthetic mode keeps the whole stack runnable without a lab; live mode pings
// real addresses. Resource counters stay synthetic until an SNMP collector is
// wired into IResourceSampler.
builder.Services.AddSingleton<SyntheticTelemetry>();
if (config.GetValue("Monitoring:Mode", "synthetic").Equals("live", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddSingleton<IDeviceProbe, IcmpDeviceProbe>();
}
else
{
    builder.Services.AddSingleton<IDeviceProbe>(sp => sp.GetRequiredService<SyntheticTelemetry>());
}

builder.Services.AddSingleton<IResourceSampler>(sp => sp.GetRequiredService<SyntheticTelemetry>());
builder.Services.AddHostedService<PollingService>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = config["Auth:Issuer"] ?? "nms",
            ValidAudience = config["Auth:Audience"] ?? "nms-web",
            IssuerSigningKey = TokenIssuer.SigningKey(config),
            ValidateIssuerSigningKey = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };

        // Browsers cannot set headers on a WebSocket handshake, so the hub takes
        // its token from the query string instead.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs"))
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorizationBuilder()
    .AddPolicy("ManageInventory", policy => policy.RequireRole("admin"));

builder.Services.AddCors(options => options.AddPolicy("web", policy => policy
    .WithOrigins((config["Cors:Origins"] ?? "http://localhost:5173").Split(',', StringSplitOptions.RemoveEmptyEntries))
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "Network Management System API", Version = "v1" });
    options.AddSecurityDefinition("bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme { Reference = new OpenApiReference { Id = "bearer", Type = ReferenceType.SecurityScheme } }] = []
    });
});

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    await scope.ServiceProvider.GetRequiredService<DatabaseBootstrapper>().RunAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseCors("web");
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "NMS API"));
}

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapDashboardEndpoints();
app.MapDeviceEndpoints();
app.MapTopologyEndpoints();
app.MapAlertEndpoints();
app.MapReportEndpoints();
app.MapHub<MonitoringHub>("/hubs/monitoring");

app.Run();
