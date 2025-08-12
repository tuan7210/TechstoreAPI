using Microsoft.AspNetCore.Diagnostics;
using TechstoreBackend.Models.DTOs;
using System.Net;

namespace TechstoreBackend.Middleware
{
    public class GlobalExceptionHandler : IExceptionHandler
    {
        private readonly ILogger<GlobalExceptionHandler> _logger;

        public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
        {
            _logger = logger;
        }

        public async ValueTask<bool> TryHandleAsync(
            HttpContext httpContext,
            Exception exception,
            CancellationToken cancellationToken)
        {
            _logger.LogError(exception, "Exception occurred: {Message}", exception.Message);

            var errorResponse = new ErrorResponse
            {
                Message = "Đã xảy ra lỗi trong quá trình xử lý",
                StatusCode = (int)HttpStatusCode.InternalServerError,
                TraceId = httpContext.TraceIdentifier
            };

            switch (exception)
            {
                case ArgumentException:
                    errorResponse.StatusCode = (int)HttpStatusCode.BadRequest;
                    errorResponse.Message = "Dữ liệu đầu vào không hợp lệ";
                    break;
                case UnauthorizedAccessException:
                    errorResponse.StatusCode = (int)HttpStatusCode.Unauthorized;
                    errorResponse.Message = "Không có quyền truy cập";
                    break;
                case KeyNotFoundException:
                    errorResponse.StatusCode = (int)HttpStatusCode.NotFound;
                    errorResponse.Message = "Không tìm thấy tài nguyên";
                    break;
            }

            httpContext.Response.StatusCode = errorResponse.StatusCode;
            httpContext.Response.ContentType = "application/json";

            await httpContext.Response.WriteAsync(
                System.Text.Json.JsonSerializer.Serialize(errorResponse),
                cancellationToken);

            return true;
        }
    }
}
