using System.Text.Json.Serialization;
using HideAnyWindowManager.Models;

namespace HideAnyWindowManager.Services;

/// <summary>
/// Source-generated JSON metadata for the Config graph. Lets Release builds
/// run with PublishTrimmed, which strips the reflection-based serializer
/// (the only reflection this app needs).
/// </summary>
[JsonSerializable(typeof(Config))]
[JsonSerializable(typeof(Rule))]
[JsonSerializable(typeof(HotkeySettings))]
internal sealed partial class ConfigJsonContext : JsonSerializerContext
{
}
