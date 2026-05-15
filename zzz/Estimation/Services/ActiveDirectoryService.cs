using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;
using Serilog;

namespace Estimation.Services;

public class UserDetailedInfo
{
    public string? DisplayName { get; set; }

    public string? SamAccountName { get; set; }

    public string? EmployeeId { get; set; }

    public string? EmailAddress { get; set; }
}

public interface IActiveDirectoryService
{
    UserDetailedInfo? GetUser(string windowsUserName);
}

[SupportedOSPlatform("windows")]
public class ActiveDirectoryService : IActiveDirectoryService
{
    public UserDetailedInfo? GetUser(string windowsUserName)
    {
        if (string.IsNullOrWhiteSpace(windowsUserName))
        {
            return null;
        }

        var (domain, sam) = ParseWindowsUserName(windowsUserName);

        try
        {
            using var context = domain is null
                ? new PrincipalContext(ContextType.Domain)
                : new PrincipalContext(ContextType.Domain, domain);
            using var userPrincipal = UserPrincipal.FindByIdentity(context, IdentityType.SamAccountName, sam);

            if (userPrincipal is null)
            {
                return null;
            }

            return new UserDetailedInfo
            {
                DisplayName = userPrincipal.DisplayName,
                SamAccountName = userPrincipal.SamAccountName,
                EmployeeId = userPrincipal.EmployeeId,
                EmailAddress = userPrincipal.EmailAddress,
            };
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Active Directory lookup failed for {WindowsUserName}", windowsUserName);
            return null;
        }
    }

    private static (string? Domain, string Sam) ParseWindowsUserName(string windowsUserName)
    {
        var name = windowsUserName.Trim();
        var slashIndex = name.IndexOf('\\');
        if (slashIndex > 0 && slashIndex < name.Length - 1)
        {
            return (name[..slashIndex], name[(slashIndex + 1)..]);
        }

        return (null, name);
    }
}
