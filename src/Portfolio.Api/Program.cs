using Portfolio.Api.Common;
using Portfolio.Api.Features.Music;

var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddHealthChecks();
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<CoalescingCache>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddApiKey(builder.Configuration, builder.Environment);
builder.Services.AddMusic(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

app.MapHealthChecks("/healthz");
app.MapGroup("/api").RequireApiKey().MapMusic();

app.Run();
