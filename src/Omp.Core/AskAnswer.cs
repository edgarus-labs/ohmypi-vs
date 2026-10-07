using System;
using System.Collections.Generic;

namespace Omp.Core;

public sealed class AskAnswer
{
    public string Id { get; set; } = "";

    public IReadOnlyList<string> SelectedOptions { get; set; } = Array.Empty<string>();

    public string? CustomInput { get; set; }
}
