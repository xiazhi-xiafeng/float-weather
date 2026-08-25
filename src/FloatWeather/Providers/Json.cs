using System.Text.Json;
using FloatWeather.Models.Dto;

namespace FloatWeather.Providers;

/// <summary>共享 Json 序列化选项</summary>
internal static class Json
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}