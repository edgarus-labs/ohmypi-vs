using System;
using System.Collections.Generic;

namespace Omp.Core;

public sealed class AskQuestionView
{
    public string Id { get; set; } = "";

    public string Question { get; set; } = "";

    public string? Header { get; set; }

    public IReadOnlyList<AskOptionView> Options { get; set; } = Array.Empty<AskOptionView>();

    public bool Multi { get; set; }

    public int? Recommended { get; set; }
}
