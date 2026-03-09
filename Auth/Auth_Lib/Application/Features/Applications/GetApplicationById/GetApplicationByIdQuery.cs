using Auth_Lib.Application.DTOs;
using ErrorOr;
using MediatR;

namespace Auth_Lib.Application.Features.Applications.GetApplicationById;

/// <summary>
/// Query to get an application by ID.
/// </summary>
public record GetApplicationByIdQuery(Guid Id) : IRequest<ErrorOr<ApplicationDto>>;
