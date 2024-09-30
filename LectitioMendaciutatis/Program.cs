using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Text;
using Microsoft.EntityFrameworkCore;
using LectitioMendaciutatis.Data;
using LectitioMendaciutatis.Hubs;
using Blazored.SessionStorage;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSignalR();
builder.Services.AddHttpContextAccessor();
builder.Services.AddBlazoredSessionStorage();
builder.Services.AddHttpClient("MyApiClient", client => {
    var baseUrl = builder.Configuration["BaseUrl"] ?? "https://localhost:5001/";
    client.BaseAddress = new Uri(baseUrl);
});

var secretKey = builder.Configuration["JwtSettings:Secret"];
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey))
        };

        // Handle JWT in SignalR connections
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];

                if (!string.IsNullOrEmpty(accessToken) && context.HttpContext.Request.Path.StartsWithSegments("/chathub"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddDbContext<ChatContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("ChatDbConnection")));

builder.WebHost.ConfigureKestrel(options =>
{
    options.ListenAnyIP(5024); // HTTP
    options.ListenAnyIP(5001, listenOptions =>
    {
        listenOptions.UseHttps("certs/selfsigned.pfx", "securecert"); // HTTPS
    });
});


var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication(); // This ensures JWT authentication is handled.
app.UseAuthorization();  // This ensures that the [Authorize] attribute is respected.

app.MapBlazorHub();
app.MapFallbackToPage("/_Host");
app.MapHub<ChatHub>("/chathub");

app.MapControllers();

app.Run();
