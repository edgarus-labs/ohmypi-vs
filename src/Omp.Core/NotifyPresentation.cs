namespace Omp.Core;

/// <summary>
/// Represents a request to display a notification message to the user with a specified notice level.
/// </summary>
/// <summary>
/// Gets or sets the level.
/// </summary>
/// <summary>
/// Gets or sets the message.
/// </summary>
public sealed class NotifyPresentation : PresentationRequest { public string Message { get; set; } = ""; public NoticeLevel Level { get; set; } }
