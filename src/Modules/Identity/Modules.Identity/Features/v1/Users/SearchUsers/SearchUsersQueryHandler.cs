using DES.Framework.Core.Context;
using DES.Framework.Persistence;
using DES.Framework.Shared.Persistence;
using DES.Modules.Identity.Contracts.DTOs;
using DES.Modules.Identity.Contracts.v1.Users.SearchUsers;
using DES.Modules.Identity.Data;
using DES.Modules.Identity.Domain;
using Mediator;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Linq.Expressions;

namespace DES.Modules.Identity.Features.v1.Users.SearchUsers;

public sealed class SearchUsersQueryHandler : IQueryHandler<SearchUsersQuery, PagedResponse<UserDto>>
{
    private readonly UserManager<DesUser> _userManager;
    private readonly IdentityDbContext _dbContext;
    private readonly IRequestContext _requestContext;

    public SearchUsersQueryHandler(
        UserManager<DesUser> userManager,
        IdentityDbContext dbContext,
        IRequestContext requestContext)
    {
        _userManager = userManager;
        _dbContext = dbContext;
        _requestContext = requestContext;
    }

    public async ValueTask<PagedResponse<UserDto>> Handle(SearchUsersQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);

        IQueryable<DesUser> users = _userManager.Users.AsNoTracking();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string term = query.Search.ToLowerInvariant();
            users = users.Where(u =>
                (u.FirstName != null && u.FirstName.ToLower().Contains(term)) ||
                (u.LastName != null && u.LastName.ToLower().Contains(term)) ||
                (u.Email != null && u.Email.ToLower().Contains(term)) ||
                (u.UserName != null && u.UserName.ToLower().Contains(term)));
        }

        if (query.IsActive.HasValue)
        {
            users = users.Where(u => u.IsActive == query.IsActive.Value);
        }

        if (query.EmailConfirmed.HasValue)
        {
            users = users.Where(u => u.EmailConfirmed == query.EmailConfirmed.Value);
        }

        if (!string.IsNullOrWhiteSpace(query.RoleId))
        {
            var userIdsInRole = await _dbContext.UserRoles
                .Where(ur => ur.RoleId == query.RoleId)
                .Select(ur => ur.UserId)
                .ToListAsync(cancellationToken);

            users = users.Where(u => userIdsInRole.Contains(u.Id));
        }

        // Apply sorting
        users = ApplySorting(users, query.Sort);

        // Project to DTO
        var projected = users.Select(u => new UserDto
        {
            Id = u.Id,
            UserName = u.UserName,
            FirstName = u.FirstName,
            LastName = u.LastName,
            Email = u.Email,
            IsActive = u.IsActive,
            EmailConfirmed = u.EmailConfirmed,
            PhoneNumber = u.PhoneNumber,
            ImageUrl = u.ImageUrl != null ? u.ImageUrl.ToString() : null
        });

        var pagedResult = await projected.ToPagedResponseAsync(query, cancellationToken).ConfigureAwait(false);

        // Resolve image URLs in place — the page is already-materialized UserDto instances we own,
        // so there is no need to allocate a second full list reconstructing every DTO.
        foreach (var u in pagedResult.Items)
        {
            u.ImageUrl = ResolveImageUrl(u.ImageUrl);
        }

        return pagedResult;
    }

    private static readonly Dictionary<string, Expression<Func<DesUser, object?>>> SortableFields = new(StringComparer.OrdinalIgnoreCase)
    {
        ["firstname"] = u => u.FirstName,
        ["lastname"] = u => u.LastName,
        ["email"] = u => u.Email,
        ["username"] = u => u.UserName,
        ["isactive"] = u => u.IsActive
    };

    private static IQueryable<DesUser> ApplySorting(IQueryable<DesUser> query, string? sort)
    {
        if (string.IsNullOrWhiteSpace(sort))
        {
            return query.OrderBy(u => u.FirstName).ThenBy(u => u.LastName);
        }

        var sortParts = sort.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        IOrderedQueryable<DesUser>? orderedQuery = null;

        foreach (var part in sortParts)
        {
            var (field, descending) = ParseSortField(part);

            if (!SortableFields.TryGetValue(field, out var selector))
            {
                selector = u => u.FirstName; // Default fallback
            }

            orderedQuery = ApplySortExpression(query, orderedQuery, selector, descending);
        }

        return orderedQuery ?? query.OrderBy(u => u.FirstName);
    }

    private static (string field, bool descending) ParseSortField(string part)
    {
        var descending = part.StartsWith('-');
        var field = descending ? part[1..] : part;
        return (field, descending);
    }

    private static IOrderedQueryable<DesUser> ApplySortExpression(
        IQueryable<DesUser> query,
        IOrderedQueryable<DesUser>? orderedQuery,
        Expression<Func<DesUser, object?>> selector,
        bool descending)
    {
        if (orderedQuery is null)
        {
            return descending
                ? query.OrderByDescending(selector)
                : query.OrderBy(selector);
        }

        return descending
            ? orderedQuery.ThenByDescending(selector)
            : orderedQuery.ThenBy(selector);
    }

    private string? ResolveImageUrl(string? imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            return null;
        }

        if (Uri.TryCreate(imageUrl, UriKind.Absolute, out _))
        {
            return imageUrl;
        }

        var origin = _requestContext.Origin;
        if (string.IsNullOrEmpty(origin))
        {
            return imageUrl;
        }

        var relativePath = imageUrl.TrimStart('/');
        return $"{origin}/{relativePath}";
    }
}