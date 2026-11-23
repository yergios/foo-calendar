namespace Api.Services;

// Each type is only ever built from the rule's own message, which is what the caller is
// shown, so the parameterless and inner-exception constructors would only invite a domain
// error with nothing to say.
#pragma warning disable CA1032

/// <summary>A request broke a rule of the domain, on purpose rather than through a bug.
/// DomainExceptionHandler answers one with its <see cref="StatusCode"/> and the rule's
/// message as problem details; any other exception is a 500 that says nothing about
/// itself. Throw one of the four below.</summary>
public abstract class DomainException(string message) : Exception(message)
{
    public abstract int StatusCode { get; }

    /// <summary>The problem details title, the same for every rule of a kind.</summary>
    public abstract string Title { get; }
}

/// <summary>The request itself is invalid, whatever is stored: 400.</summary>
public sealed class InvalidInputException(string message) : DomainException(message)
{
    public override int StatusCode => StatusCodes.Status400BadRequest;
    public override string Title => "Invalid request";
}

/// <summary>The caller acted on something that exists but is not theirs: 403.</summary>
public sealed class NotOwnerException(string message) : DomainException(message)
{
    public override int StatusCode => StatusCodes.Status403Forbidden;
    public override string Title => "Forbidden";
}

/// <summary>What the request names does not exist: 404.</summary>
public sealed class NotFoundException(string message) : DomainException(message)
{
    public override int StatusCode => StatusCodes.Status404NotFound;
    public override string Title => "Not found";
}

/// <summary>The request is valid on its own but conflicts with what is stored: 409.
/// </summary>
public sealed class ConflictException(string message) : DomainException(message)
{
    public override int StatusCode => StatusCodes.Status409Conflict;
    public override string Title => "Conflict";
}

#pragma warning restore CA1032
