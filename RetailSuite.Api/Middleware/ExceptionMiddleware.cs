namespace RetailSuite.Api.Middleware
{
    using RetailSuite.Infrastructure.Exceptions;
    using RetailSuite.Shared;
    using System.Net;
    using System.Text.Json;

    public class ExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ExceptionMiddleware> _logger;

        public ExceptionMiddleware(
            RequestDelegate next,
            ILogger<ExceptionMiddleware> logger)
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
                _logger.LogError(ex, "Unhandled exception: {Message}", ex.Message);

                context.Response.ContentType = "application/json";

                context.Response.StatusCode = ex switch
                {
                    NotFoundException             => (int)HttpStatusCode.NotFound,
                    ConflictException             => (int)HttpStatusCode.Conflict,
                    BusinessRuleException         => 422, // Unprocessable Entity
                    UnauthorizedAccessException   => (int)HttpStatusCode.Unauthorized,
                    InvalidOperationException     => (int)HttpStatusCode.BadRequest,
                    ArgumentException             => (int)HttpStatusCode.BadRequest,
                    _                             => (int)HttpStatusCode.InternalServerError
                };

                var response = new ApiResponse<object>
                {
                    Success = false,
                    Message = ex.Message
                };

                var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, jsonOptions));
            }
        }
    }
}
