namespace Galaxy.Core;

public class GalaxyResult<T>
{
    public int Status { get; set; }
    public string? Message { get; set; }
    public T? Data { get; set; }

    public static GalaxyResult<T> Ok(T data, string? message = null) => new() { Status = 200, Data = data, Message = message };
    public static GalaxyResult<T> Error(int status, string message) => new() { Status = status, Message = message };
}

public class GalaxyResult : GalaxyResult<object>
{
    public static GalaxyResult Ok(string? message = null) => new() { Status = 200, Message = message };
    public new static GalaxyResult Error(int status, string message) => new() { Status = status, Message = message };
}
