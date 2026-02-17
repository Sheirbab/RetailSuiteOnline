using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using RetailSuite.Api.MultiTenancy;
using RetailSuite.Infrastructure;
using RetailSuite.Modules.Accounting.Services;
using RetailSuite.Modules.Catalog;
using RetailSuite.Modules.Identity;
using RetailSuite.Infrastructure.Modules.Inventory;
using RetailSuite.Infrastructure.Modules.Inventory.Services;
using RetailSuite.Modules.Orders;
using RetailSuite.Infrastructure.Modules.Orders.Services;
using RetailSuite.Modules.Tenant;
using RetailSuite.Shared;
using System.Text;
using RetailSuite.Infrastructure.Modules.Tenant;
using RetailSuite.Infrastructure.Modules.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TenantDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddDbContext<RetailDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();
builder.Services.AddScoped<InventoryService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<AccountingService>();

var jwtSettings = builder.Configuration.GetSection("Jwt");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidAudience = jwtSettings["Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(jwtSettings["Key"] ?? ""))
    };
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen();

var app = builder.Build();

// 🔥 Remove environment condition for now
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => "RetailSuite API Running 🚀");

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
