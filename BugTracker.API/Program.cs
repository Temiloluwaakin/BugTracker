using Asp.Versioning;
using Asp.Versioning.ApiExplorer;
using BugTracker.API.Hubs;
using BugTracker.Data;
using BugTracker.Data.Context;
using BugTracker.Services.Helpers;
using BugTracker.Services.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using MongoDB.Driver;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using Serilog.Exceptions.EntityFrameworkCore.Destructurers;
using System.Reflection;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

//loging
Log.Logger = new LoggerConfiguration()
.MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
.MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
.MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Warning)
.Enrich.WithExceptionDetails(new DestructuringOptionsBuilder()
.WithDefaultDestructurers()
.WithDestructurers(new[] { new DbUpdateExceptionDestructurer() }))
.WriteTo.File(builder.Configuration["LogFilePath:TXT"], outputTemplate: "{NewLine}{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception} ", rollingInterval: RollingInterval.Day)
.CreateLogger();


// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("AppConfig"));


//mongodb
builder.Services.Configure<MongoDbSettings>(builder.Configuration.GetSection("MongoDbSettings"));

builder.Services.AddSingleton<IMongoClient>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    return new MongoClient(settings.ConnectionString);
});

builder.Services.AddSingleton<IMongoDatabase>(sp =>
{
    var settings = sp.GetRequiredService<IOptions<MongoDbSettings>>().Value;
    var client = sp.GetRequiredService<IMongoClient>();
    return client.GetDatabase(settings.DatabaseName);
});

builder.Services.Configure<AppConfig>(builder.Configuration.GetSection("AppConfig"));
builder.Services.Configure<Gemini>(builder.Configuration.GetSection("Gemini"));

builder.Services.AddScoped<DatabaseContext>();

//services
builder.Services.AddScoped<IAuthService, AuthServices>();
builder.Services.AddScoped<IAuthHelpers, AuthHelpers>();
builder.Services.AddScoped<IProjectService, ProjectServices>();
builder.Services.AddScoped<IBugService, BugService>();
builder.Services.AddScoped<IUserService, UserServices>();
builder.Services.AddScoped<ITestCaseService, TestCaseService>();
builder.Services.AddScoped<IResponseHelper, ResponseHelper>();
builder.Services.AddScoped<IChatService, ChatServices>();
builder.Services.AddScoped<IProfileService, ProfileService>();
builder.Services.AddScoped<IAIHelper, AIHelper>();
builder.Services.AddScoped<IEmailHelper, EmailHelper>();
builder.Services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();

builder.Services.AddHttpClient<AIHelper>();

//api versioning
builder.Services.AddApiVersioning(
  options =>
  {
      options.ReportApiVersions = true;
      options.DefaultApiVersion = new ApiVersion(1, 0);
      options.AssumeDefaultVersionWhenUnspecified = true;
  })
  .AddApiExplorer(
  options =>
  {
      options.GroupNameFormat = "'v'VVV";
      options.SubstituteApiVersionInUrl = true;
  });
builder.Services.AddSwaggerGen(options =>
{
    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    options.IncludeXmlComments(xmlPath);
});
builder.Services.AddSignalR();// Register SignalR

builder.Services.AddHttpClient("OpenAI", client =>
{
    client.BaseAddress = new Uri("https://api.openai.com/v1/");
    client.DefaultRequestHeaders.Add("Authorization", $"Bearer {builder.Configuration["OpenAI:ApiKey"]}");
});

//port for render
var port = Environment.GetEnvironmentVariable("PORT") ?? "8080";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

//CORS
var _allowSpecificOrigins = "_myAllowSpecificOrigins";
var allPermittedOrigins = builder.Configuration.GetSection("CORSConfig:AllowedDomains").Get<List<string>>() ?? new List<string>();
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: _allowSpecificOrigins,
                      policy =>
                      {
                          //policy.WithOrigins(allPermittedOrigins.ToArray());
                          policy.SetIsOriginAllowed(origin =>
                            allPermittedOrigins.Contains(origin) || origin == "null" || string.IsNullOrEmpty(origin)
                          );
                          policy.AllowAnyMethod();
                          policy.AllowCredentials();
                          policy.AllowAnyHeader();
                      });
});
// auth jwt
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var jwtKey = builder.Configuration.GetSection("Jwt:key").Value;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ClockSkew = TimeSpan.Zero,
            RequireExpirationTime = true,
            RequireSignedTokens = true
        };
        // event handlers for debugging
        options.Events = new JwtBearerEvents
        {
            OnAuthenticationFailed = context =>
            {
                Log.Error($"JWT Authentication failed: {context.Exception.Message}");
                if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
                {
                    context.Response.Headers.Add("Token-Expired", "true");
                }
                return Task.CompletedTask;
            },
            OnChallenge = context =>
            {
                Log.Warning($"JWT Challenge: {context.Error}, {context.ErrorDescription}");
                return Task.CompletedTask;
            },
            OnTokenValidated = context =>
            {
                Log.Information("JWT Token validated successfully");
                return Task.CompletedTask;
            },
            // websocket signalr reads the token from query string for SignalR WebSocket connections
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;

                // Only apply to SignalR hub requests
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs/chat"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });
builder.Services.AddAuthorization();


var app = builder.Build();


//Resolve IApiVersionDescriptionProvider
var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();
// Configure Swagger UI with version info
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    foreach (var description in provider.ApiVersionDescriptions)
    {
        options.SwaggerEndpoint($"{description.GroupName}/swagger.json",
            $"Bug Tracker API {description.GroupName.ToUpperInvariant()}");
    }
});
// Register Swagger docs for each version
app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions>>().Value.SwaggerGeneratorOptions.SwaggerDocs.Clear();
foreach (var description in provider.ApiVersionDescriptions)
{
    app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Swashbuckle.AspNetCore.SwaggerGen.SwaggerGenOptions>>().Value.SwaggerGeneratorOptions.SwaggerDocs
        .Add(description.GroupName, CreateInfoForApiVersion(description));
}



app.UseHttpsRedirection();
app.UseCors("_myAllowSpecificOrigins");
app.UseAuthentication(); // Must come before UseAuthorization
app.UseAuthorization();
app.MapHub<ChatHub>("/hubs/chat");   // This is the WebSocket endpoint the client connects to
app.MapControllers();
app.Run();


//Helper method for Swagger version info
static OpenApiInfo CreateInfoForApiVersion(ApiVersionDescription description)
{
    return new OpenApiInfo
    {
        Title = $"Bug Tracker V{description.ApiVersion}",
        Version = description.ApiVersion.ToString(),
        Description = "An ASP.NET Core Web API for Bug Tracker App by Temmy"
    };
}
