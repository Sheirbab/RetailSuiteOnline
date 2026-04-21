namespace RetailSuite.Shared
{
    public class ApiResponse<T>
    {
        public bool Success { get; set; } = true;
        public string? Message { get; set; }
        public T? Data { get; set; }

        public ApiResponse() { }

        public ApiResponse(bool success, string? message, T? data)
        {
            Success = success;
            Message = message;
            Data = data;
        }

        public static ApiResponse<T> Ok(T data, string? message = null)
            => new(true, message, data);

        public static ApiResponse<T> Fail(string message)
            => new(false, message, default);
    }
}
