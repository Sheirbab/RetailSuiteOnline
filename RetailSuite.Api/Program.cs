using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using RetailSuite.Api.MultiTenancy;
using RetailSuite.Modules.Tenant;
using RetailSuite.Shared;
using RetailSuite.Modules.Identity;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<TenantDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddDbContext<IdentityDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Default")));

builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ITenantContext, TenantContext>();


builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// 🔥 Remove environment condition for now
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => "RetailSuite API Running 🚀");

app.MapControllers();

app.Run();
