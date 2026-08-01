using System.Text.Json.Serialization;
using BuzzahBuddy.Models;

namespace BuzzahBuddy.Services.Storage;

/// <summary>
/// Source-generated System.Text.Json metadata for everything
/// <see cref="PreferencesStorageService"/> persists.
/// </summary>
/// <remarks>
/// Reflection-based serialization cannot be used here. Under iOS Release (full AOT,
/// aot-only mode) STJ's reflection accessor constructs objects through
/// ConstructorInfo.Invoke, and Mono has no runtime-invoke wrapper for those
/// constructors because the AOT compiler never saw a static call to them. The result
/// at runtime is:
///
///   ExecutionEngineException: Attempting to JIT compile method
///   '(wrapper dynamic-method) object object:.ctor ()' while running in aot-only mode
///
/// which surfaced as a "Connection Error" alert on every BLE connect, since
/// SaveLastDeviceAsync runs inside the connect flow. Source generation emits the
/// converters at build time, so no reflection or JIT is involved.
///
/// Every type persisted by PreferencesStorageService needs an entry here; adding a
/// new one without registering it fails at runtime on device, not at compile time.
/// Options live on JsonSourceGenerationOptions rather than a JsonSerializerOptions
/// instance so the generator bakes them in.
/// </remarks>
[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(GloveDevice))]
[JsonSerializable(typeof(List<TherapySession>))]
internal partial class StorageJsonContext : JsonSerializerContext
{
}
