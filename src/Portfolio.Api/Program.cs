using Portfolio.Api.Common;
using Portfolio.Api.Features.Articles;
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
builder.Services.AddArticles(builder.Configuration);

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

// Pas /healthz : Cloud Run réserve les chemins qui finissent par « z ».
app.MapHealthChecks("/health");
app.MapGroup("/api").RequireApiKey().MapMusic().MapArticles();
app.MapContentWebhook();

app.Run();
