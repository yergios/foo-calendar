namespace Api.Dtos;

/// <summary>One email queued for delivery. Both bodies are always built: HtmlBody is what
/// most clients render, TextBody is the fallback for the ones that don't.</summary>
public sealed record EmailMessage(
    string ToAddress, string ToName, string Subject, string HtmlBody, string TextBody);
