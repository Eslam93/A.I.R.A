using System.Text.Json;
using Aira.Application.Models;
using Microsoft.AspNetCore.Mvc;

namespace Aira.Api.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(RequestDelegate next, ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception while processing {Method} {Path}", context.Request.Method, context.Request.Path);
            if (context.Response.HasStarted)
            {
                throw;
            }

            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            context.Response.ContentType = "application/problem+json";

            var problem = ProblemDetailsFactoryExtensions.BuildProblemDetails(
                context,
                new ProblemDescriptor(
                    500,
                    "/problems/internal-error",
                    "Internal error.",
                    "The server encountered an unexpected error.",
                    "InternalError"));

            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
    }
}
