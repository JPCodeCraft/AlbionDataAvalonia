using System;
using System.Net;

namespace AlbionDataAvalonia.Auth.Services
{
    public class AuthServiceException : Exception
    {
        public HttpStatusCode? StatusCode { get; }
        public bool IsInvalidRefreshToken { get; }

        public AuthServiceException(string message, HttpStatusCode? statusCode = null, bool isInvalidRefreshToken = false, Exception? innerException = null)
            : base(message, innerException)
        {
            StatusCode = statusCode;
            IsInvalidRefreshToken = isInvalidRefreshToken;
        }

        public static AuthServiceException RefreshTokenError(HttpStatusCode statusCode, string responseBody)
        {
            var isInvalid = statusCode == HttpStatusCode.BadRequest || statusCode == HttpStatusCode.Unauthorized;
            return new AuthServiceException($"Failed to refresh Firebase token: {statusCode}, {responseBody}", statusCode, isInvalid);
        }

        public static AuthServiceException TokenExchangeError(HttpStatusCode statusCode, string responseBody)
        {
            return new AuthServiceException($"Failed to get Firebase token from auth code: {statusCode}, {responseBody}", statusCode);
        }
    }
}
