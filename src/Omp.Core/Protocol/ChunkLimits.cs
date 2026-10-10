namespace Omp.Core.Protocol;

/// <summary>
/// Represents the configuration limits for chunked data transmission, specifying the maximum allowable sizes for individual and reassembled frames.
/// </summary>
internal readonly struct ChunkLimits
{
    /// <summary>
    /// The default.
    /// </summary>
    public static readonly ChunkLimits Default = new ChunkLimits(1024 * 1024, 64 * 1024 * 1024);

    /// <summary>
    /// Initializes a new instance of the ChunkLimits struct with the specified maximum frame size and maximum reassembled frame size.
    /// </summary>
    /// <param name="maxFrameBytes">The max frame bytes.</param>
    /// <param name="maxReassembledFrameBytes">The max reassembled frame bytes.</param>
    public ChunkLimits(long maxFrameBytes, long maxReassembledFrameBytes)
    {
        MaxFrameBytes = maxFrameBytes;
        MaxReassembledFrameBytes = maxReassembledFrameBytes;
    }

    /// <summary>Physical frame ceiling advertised by <c>ready.maxFrameBytes</c>.</summary>
    public long MaxFrameBytes { get; }

    /// <summary>Logical frame ceiling advertised by <c>ready.maxReassembledFrameBytes</c>.</summary>
    public long MaxReassembledFrameBytes { get; }
}
