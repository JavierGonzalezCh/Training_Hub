namespace GSM.Application.Identity.Contracts;

public sealed record CreateStaffRequest(string Email, string FullName);

public sealed record StaffResponse(Guid Id, string Email, string FullName, bool IsActive);
