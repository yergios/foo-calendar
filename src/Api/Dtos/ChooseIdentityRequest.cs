using System.ComponentModel.DataAnnotations;

using Api.Configuration;

namespace Api.Dtos;

/// <summary>The name a visitor claims on a deployment with no identity provider. The cap
/// is the one Person.Username carries: this value reaches that column through
/// PeopleEndpoints.CreateMe, which reads it back off the identity cookie.</summary>
public sealed record ChooseIdentityRequest(
    [property: Required(AllowEmptyStrings = false)][property: MaxLength(100)] string Username)
    : IValidatableObject
{
    /// <summary>The claimed name without surrounding whitespace, which is what gets stored.
    /// </summary>
    public string Claimed => Username?.Trim();

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.IsNullOrWhiteSpace(Username))
            yield return new ValidationResult("A username is required.", [nameof(Username)]);

        // The name the handler gives a visitor who hasn't chosen one. Claiming it would put
        // them on the same row as everyone who hasn't chosen either.
        else if (Claimed == AuthOptions.AnonymousUsername)
            yield return new ValidationResult(
                $"\"{AuthOptions.AnonymousUsername}\" is reserved and cannot be a username.",
                [nameof(Username)]);
    }
}
