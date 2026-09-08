using System.DirectoryServices.Protocols;
using System.Net;
using EncoreAuthDemo.Authorization;
using EncoreAuthDemo.Models;
using EncoreAuthDemo.Options;
using Microsoft.Extensions.Options;

namespace EncoreAuthDemo.Authentication;

public sealed class ActiveDirectoryAuthenticationService(
    IOptions<ActiveDirectoryOptions> activeDirectoryOptions,
    IRoleMapper roleMapper,
    ILogger<ActiveDirectoryAuthenticationService> logger) : IAuthenticationService
{
    private readonly ActiveDirectoryOptions _options = activeDirectoryOptions.Value;

    public async Task<AuthenticationResult> AuthenticateAsync(
        string employeeId,
        string password,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(employeeId) || string.IsNullOrWhiteSpace(password))
        {
            return AuthenticationResult.Failed(
                AuthenticationFailureCode.MissingCredentials,
                "Employee ID and password are required.");
        }

        if (string.IsNullOrWhiteSpace(_options.Server) || string.IsNullOrWhiteSpace(_options.SearchBaseDn))
        {
            logger.LogWarning("Active Directory authentication is not configured.");
            return AuthenticationResult.Failed(
                AuthenticationFailureCode.NotConfigured,
                "Active Directory authentication is not configured.");
        }

        try
        {
            var userName = FormatBindUserName(employeeId.Trim());
            var identifier = new LdapDirectoryIdentifier(_options.Server, _options.Port);

            using var connection = new LdapConnection(identifier)
            {
                AuthType = _options.AuthType
            };

            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SecureSocketLayer = _options.UseSsl;
            connection.Credential = new NetworkCredential(userName, password);

            await Task.Run(connection.Bind, cancellationToken);

            var directoryUser = await FindDirectoryUserAsync(connection, employeeId.Trim(), cancellationToken);
            if (directoryUser is null)
            {
                logger.LogWarning("Active Directory bind succeeded, but employee {EmployeeId} was not found by search.", employeeId);
                return AuthenticationResult.Failed(
                    AuthenticationFailureCode.UserNotFound,
                    "The employee account could not be resolved.");
            }

            var mappedRole = roleMapper.Map(directoryUser.Groups);
            if (mappedRole is null)
            {
                logger.LogWarning("Employee {EmployeeId} authenticated but did not match a configured Encore role.", employeeId);
                return AuthenticationResult.Failed(
                    AuthenticationFailureCode.NoMappedRole,
                    "The employee is not assigned to an Encore security role.");
            }

            var user = new EncoreUser
            {
                EmployeeId = employeeId.Trim(),
                DisplayName = directoryUser.DisplayName,
                DistinguishedName = directoryUser.DistinguishedName,
                MatchedAdGroup = mappedRole.MatchedGroup,
                Role = mappedRole.Role,
                LegacyAuthorityLevel = mappedRole.LegacyAuthorityLevel,
                ActiveDirectoryGroups = directoryUser.Groups
            };

            return AuthenticationResult.Success(user);
        }
        catch (LdapException ex)
        {
            logger.LogWarning(ex, "Active Directory authentication failed for employee {EmployeeId}.", employeeId);
            var failureCode = ex.ErrorCode == 49
                ? AuthenticationFailureCode.InvalidCredentials
                : AuthenticationFailureCode.DirectoryUnavailable;
            return AuthenticationResult.Failed(
                failureCode,
                failureCode == AuthenticationFailureCode.InvalidCredentials
                    ? "The employee ID or password is invalid."
                    : "Active Directory could not be reached or rejected the connection.");
        }
        catch (DirectoryOperationException ex)
        {
            logger.LogWarning(ex, "Active Directory search failed for employee {EmployeeId}.", employeeId);
            return AuthenticationResult.Failed(
                AuthenticationFailureCode.DirectorySearchFailed,
                "The Active Directory user search failed.");
        }
    }

    private async Task<DirectoryUser?> FindDirectoryUserAsync(
        LdapConnection connection,
        string employeeId,
        CancellationToken cancellationToken)
    {
        var escapedEmployeeId = EscapeLdapFilterValue(employeeId);
        var filter = $"({_options.EmployeeIdAttribute}={escapedEmployeeId})";
        var request = new SearchRequest(
            _options.SearchBaseDn,
            filter,
            SearchScope.Subtree,
            _options.DisplayNameAttribute,
            _options.GroupMembershipAttribute,
            "distinguishedName");

        var response = await Task.Run(
            () => (SearchResponse)connection.SendRequest(request),
            cancellationToken);

        if (response.Entries.Count == 0)
        {
            return null;
        }

        var entry = response.Entries[0];
        var displayName = ReadSingleAttribute(entry, _options.DisplayNameAttribute) ?? employeeId;
        var distinguishedName = ReadSingleAttribute(entry, "distinguishedName") ?? string.Empty;
        var groups = ReadMultiValueAttribute(entry, _options.GroupMembershipAttribute);

        return new DirectoryUser(displayName, distinguishedName, groups);
    }

    private string FormatBindUserName(string employeeId)
    {
        if (!string.IsNullOrWhiteSpace(_options.BindUserNameFormat))
        {
            return _options.BindUserNameFormat
                .Replace("{employeeId}", employeeId, StringComparison.OrdinalIgnoreCase)
                .Replace("{domain}", _options.Domain, StringComparison.OrdinalIgnoreCase);
        }

        if (!string.IsNullOrWhiteSpace(_options.Domain))
        {
            return $@"{_options.Domain}\{employeeId}";
        }

        return employeeId;
    }

    private static string? ReadSingleAttribute(SearchResultEntry entry, string attributeName)
    {
        if (!entry.Attributes.Contains(attributeName) || entry.Attributes[attributeName].Count == 0)
        {
            return null;
        }

        return entry.Attributes[attributeName]
            .GetValues(typeof(string))
            .Cast<string>()
            .FirstOrDefault();
    }

    private static IReadOnlyList<string> ReadMultiValueAttribute(SearchResultEntry entry, string attributeName)
    {
        if (!entry.Attributes.Contains(attributeName))
        {
            return [];
        }

        return entry.Attributes[attributeName]
            .GetValues(typeof(string))
            .Cast<string>()
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
    }

    private static string EscapeLdapFilterValue(string value)
    {
        return value
            .Replace(@"\", @"\5c", StringComparison.Ordinal)
            .Replace("*", @"\2a", StringComparison.Ordinal)
            .Replace("(", @"\28", StringComparison.Ordinal)
            .Replace(")", @"\29", StringComparison.Ordinal)
            .Replace("\0", @"\00", StringComparison.Ordinal);
    }

    private sealed record DirectoryUser(
        string DisplayName,
        string DistinguishedName,
        IReadOnlyList<string> Groups);
}
