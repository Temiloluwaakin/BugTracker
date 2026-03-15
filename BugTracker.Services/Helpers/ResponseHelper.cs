using BugTracker.Data;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BugTracker.Services.Helpers
{
    public interface IResponseHelper
    {
        ApiResponse<T> Ok<T>(T? data, string message = "Success");
        ApiResponse<T> Fail<T>(string code, string message);
        ApiResponse<T> SystemError<T>();
    }
    public class ResponseHelper : IResponseHelper
    {
        private readonly ILogger<ResponseHelper> _logger;

        public ResponseHelper(
            ILogger<ResponseHelper> logger
        )
        {
            _logger = logger;
        }

        // Response builder shortcuts (mirrors your auth service style)
        public ApiResponse<T> Ok<T>(T? data, string message = "Success") => new()
        {
            ResponseCode = ResponseCodes.Success.ResponseCode,
            ResponseMessage = message,
            Data = data
        };

        public ApiResponse<T> Fail<T>(string code, string message) => new()
        {
            ResponseCode = code,
            ResponseMessage = message
        };

        public ApiResponse<T> SystemError<T>() => new()
        {
            ResponseCode = ResponseCodes.SystemMalfunction.ResponseCode,
            ResponseMessage = "An unexpected error occurred. Please try again later."
        };
    }
}
