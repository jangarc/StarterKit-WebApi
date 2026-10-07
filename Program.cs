using Infrastructure.BackgroundServices;
using Infrastructure.Common;
using Microsoft.AspNetCore.Mvc;
using Serilog;
//using System.Text.Json.Serialization;

var builder = WebApplication.CreateBuilder(args);

builder.BuildBaseService();
builder.Host.UseInfrastructureHost();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .WriteTo.File("logs/app-log-.txt", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        // 移除預設的駝峰命名規則，改為維持 C# 屬性原樣 [2, 3]
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
    })
    .ConfigureApiBehaviorOptions(options =>
        {
            // 🔥 全局黑魔法：當模型驗證失敗 (400 BadRequest) 時，強制覆寫並自訂回傳的 JSON 格式
            options.InvalidModelStateResponseFactory = context =>
            {
                // 1. 為了防呆，我們只抓取第一個驗證失敗的錯誤訊息當作 Message
                var firstError = context.ModelState.Values
                    .SelectMany(v => v.Errors)
                    .Select(e => e.ErrorMessage)
                    .FirstOrDefault() ?? "Invalid request payload.";

                // 2. 🚀 強制格式化為與網域、中介軟體完全一致的扁平化 JSON
                var customPayload = new
                {
                    error = true,
                    message = firstError
                };

                return new BadRequestObjectResult(customPayload);
            };
        })
    //.AddJsonOptions(options =>
    //    {
    //        // 💡 關鍵設定：當遇到雙向導覽屬性的無限循環時，自動切斷並設為 null
    //        options.JsonSerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
    //    })
    ;

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.AddHostedService<SystemInitializationHostedService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
