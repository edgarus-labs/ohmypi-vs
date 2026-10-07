using System;
using System.Collections.Generic;

namespace Omp.Core;

public sealed class QueueView
{
    public IReadOnlyList<string> Steering { get; set; } = Array.Empty<string>();

    public IReadOnlyList<string> FollowUp { get; set; } = Array.Empty<string>();
}
