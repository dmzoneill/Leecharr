using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using NLog;

namespace NzbDrone.Core.Authentication;

public class ClaimsRoleMappingService : IClaimsRoleMappingService
{
    private readonly Logger _logger;

    public ClaimsRoleMappingService(Logger logger)
    {
        _logger = logger;
    }

    public List<string> ResolveRoles(IdentityProviderDefinition provider, IReadOnlyList<string> rawGroups, bool isFirstUser)
    {
        if (isFirstUser)
        {
            return new List<string> { "Admin" };
        }

        if (rawGroups == null || rawGroups.Count == 0 || provider == null || string.IsNullOrWhiteSpace(provider.RoleMappingRules))
        {
            return new List<string> { "User" };
        }

        try
        {
            var rules = JsonSerializer.Deserialize<Dictionary<string, string>>(provider.RoleMappingRules);
            if (rules != null)
            {
                var assignedRoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var kvp in rules)
                {
                    var role = kvp.Key;
                    var regexPattern = kvp.Value;
                    if (string.IsNullOrWhiteSpace(regexPattern))
                    {
                        continue;
                    }

                    var regex = new Regex(regexPattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                    if (rawGroups.Any(g => regex.IsMatch(g)))
                    {
                        assignedRoles.Add(role);
                    }
                }

                if (assignedRoles.Count > 0)
                {
                    return assignedRoles.ToList();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warn(ex, "Failed to parse RoleMappingRules for provider {0}", provider.Name);
        }

        // Direct matching fallback
        if (rawGroups.Any(g => g.Equals("admin", StringComparison.OrdinalIgnoreCase) ||
                               g.Equals("admins", StringComparison.OrdinalIgnoreCase) ||
                               g.Equals("leecharr-admins", StringComparison.OrdinalIgnoreCase)))
        {
            return new List<string> { "Admin" };
        }

        return new List<string> { "User" };
    }
}
