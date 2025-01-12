using System.Text.Json.Serialization;

namespace Snowberry.IO.SingleFile;

/// <summary>
/// The bundler options.
/// </summary>
public class BundlerOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether to use compression for the files.
    /// </summary>
    /// <remarks>It depends on the bundle version whether compressing is supported.</remarks>
    [JsonPropertyName(nameof(UseCompression))]
    public bool UseCompression { get; set; } = true;

    /// <summary>
    /// Gets or sets the compression threshold. 
    /// </summary>
    /// <remarks>If the compressed size is smaller than the original size multiplied by this value, the compressed data will be used.</remarks>
    [JsonPropertyName(nameof(CompressionThreshold))]
    public float CompressionThreshold { get; set; } = 0.75F;

    /// <summary>
    /// Gets or sets a value indicating whether to force compression for the files even if the compressed size exceeds the compression threshold.
    /// </summary>
    [JsonPropertyName(nameof(ForceCompression))]
    public bool ForceCompression { get; set; }
}