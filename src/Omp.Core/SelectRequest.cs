using System;
using System.Collections.Generic;

namespace Omp.Core;

public sealed class SelectRequest : InteractionRequest
{
    public string Title { get; set; } = "";

    public IReadOnlyList<SelectOptionView> Options { get; set; } = Array.Empty<SelectOptionView>();
}
