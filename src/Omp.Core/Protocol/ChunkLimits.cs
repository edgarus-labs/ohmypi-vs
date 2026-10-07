namespace Omp.Core.Protocol;

internal readonly struct ChunkLimits
{
    public static readonly ChunkLimits Default = new ChunkLimits(1024 * 1024, 64 * 1024 * 1024);

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
