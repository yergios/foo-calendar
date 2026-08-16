using System.ComponentModel.DataAnnotations;

namespace Api.Dtos;

public sealed record RejectAbsenceRequest([MaxLength(500)] string Note);
