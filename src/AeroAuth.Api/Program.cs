using System.Text;
using AeroAuth.Api.Auth;
using AeroAuth.Core.Exceptions;
using AeroAuth.Core.Interfaces;
using AeroAuth.Infrastructure;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);
var cfg = builder.Configuration;

var connectionString = cfg.GetConnectionString("AeroAuth") ?? throw new InvalidOperationException("ConnectionStrings:AeroAuth is not configured.");
var jwtKey = cfg["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is not configured. Use 'dotnet user-secrets set Jwt:Key <value>'.");

builder.Services.AddAeroAuthInfrastructure(connectionString);
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, HttpTenantContext>();
builder.Services.AddSingleton<TokenService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "AeroAuth API", Version = "v1" });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", In = ParameterLocation.Header, Name = "Authorization" });
    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        { new OpenApiSecurityScheme { Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" } }, Array.Empty<string>() }
    });
});

// In production replace the token issuer with your organization's identity provider and keep the sub / role / org claims.
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(o =>
{
    o.MapInboundClaims = false;
    o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = cfg["Jwt:Issuer"], ValidateAudience = true, ValidAudience = cfg["Jwt:Audience"],
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromMinutes(1), NameClaimType = "name", RoleClaimType = "role"
    };
});
builder.Services.AddAuthorization();

var app = builder.Build();

// One place that turns domain and database errors into clean API responses.
app.UseExceptionHandler(errorApp => errorApp.Run(async ctx =>
{
    var ex = ctx.Features.Get<IExceptionHandlerFeature>()?.Error;
    var (status, title) = ex switch
    {
        NotFoundException => (StatusCodes.Status404NotFound, ex.Message),
        ConflictException => (StatusCodes.Status409Conflict, ex.Message),
        TenantViolationException => (StatusCodes.Status403Forbidden, ex.Message),
        ValidationFailedException => (StatusCodes.Status400BadRequest, ex.Message),
        SqlException { Number: 2627 or 2601 } => (StatusCodes.Status409Conflict, "A record with the same unique value already exists."),
        SqlException { Number: 547 } => (StatusCodes.Status400BadRequest, "A rule was violated: a related record is missing, belongs to another organization, or a value is not allowed."),
        SqlException { Number: 51000 or 51001 } => (StatusCodes.Status403Forbidden, "Audit records are append-only."),
        _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
    };
    if (status == 500) app.Logger.LogError(ex, "Unhandled exception");
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title });
}));

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.Run();
