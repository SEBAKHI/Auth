using Auth_Lib.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_API.Modules.ApplicationManagement.Queries;

/// <summary>
/// Query to get an application by ID.
/// </summary>
public record GetApplicationByIdQuery(Guid Id) : IRequest<ErrorOr<ApplicationDto>>;
